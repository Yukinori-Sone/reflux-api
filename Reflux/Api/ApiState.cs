using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Reflux.Api
{
    enum AttachState
    {
        /// <summary>Searching for bm2dx process</summary>
        WaitingForProcess,
        /// <summary>Hooked, comparing game version with offsets</summary>
        CheckingVersion,
        /// <summary>Offsets didn't match, waiting for PUT /api/offsets or Enter on the console (only with the API enabled)</summary>
        OffsetMismatch,
        /// <summary>Offsets didn't match, interactive offset search is running on the console</summary>
        OffsetSearch,
        /// <summary>Waiting for the song list and reading databases</summary>
        Loading,
        /// <summary>The song list still doesn't look right after a while: the struct layout probably moved with an INFINITAS update (fix with PUT /api/layout)</summary>
        LayoutMismatch,
        /// <summary>Monitoring plays</summary>
        Ready,
    }

    class CurrentChartDto
    {
        public string SongId { get; set; }
        public string Title { get; set; }
        public string Difficulty { get; set; }
    }

    class StatusDto
    {
        public int ApiVersion { get; set; } = 1;
        /// <summary>Changes every time Reflux starts, revisions are only comparable within one instance</summary>
        public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");
        public string RefluxVersion { get; set; }
        public AttachState AttachState { get; set; }
        public DateTime AttachStateSince { get; set; }
        public string Message { get; set; }
        public int? ProcessId { get; set; }
        public string GameVersion { get; set; }
        public string OffsetsVersion { get; set; }
        /// <summary>INFINITAS ID of the logged-in player ("C" + 12 digits), null until read</summary>
        public string InfinitasId { get; set; }
        /// <summary>DJ NAME of the logged-in player, null until read (the game loads the profile a little after the login)</summary>
        public string DjName { get; set; }
        /// <summary>False if play state marker wasn't found, play results can't be recorded then</summary>
        public bool PlayStateDetection { get; set; }
        /// <summary>songSelect / playing / resultScreen, null when not attached</summary>
        public string GameState { get; set; }
        public CurrentChartDto CurrentChart { get; set; }
        /// <summary>Incremented when /api/songs and /api/charts content changes</summary>
        public int SongsRevision { get; set; }
        /// <summary>Incremented when /api/scores content changes</summary>
        public int ScoresRevision { get; set; }
        /// <summary>Incremented when /api/clearrates content changes</summary>
        public int ClearRatesRevision { get; set; }
        /// <summary>Incremented when /api/rivals and /api/rivals/scores content changes</summary>
        public int RivalsRevision { get; set; }
        public int SessionPlays { get; set; }
        /// <summary>
        /// Data the core can't read while attached: rivals, clearRates, playCounts. The struct layout or the static pointers may have
        /// moved with an INFINITAS update. Empty when everything is read (replaced, never changed in place)
        /// </summary>
        public List<string> DataWarnings { get; set; } = new List<string>();
        public DateTime ServerTime { get; set; }

        public StatusDto Clone() => (StatusDto)MemberwiseClone();
    }

    /// <summary>
    /// State shared between the main loop (writer) and the API server thread (reader).
    /// The main loop never waits for the API; API requests never read game memory.
    /// </summary>
    static class ApiState
    {
        static readonly object sync = new object();
        static readonly StatusDto status = new StatusDto();
        static int retryRequested = 0;

        static CachedJson songs = new CachedJson(new SongDto[0]);
        static CachedJson charts = new CachedJson(new ChartDto[0]);
        static CachedJson scores = new CachedJson(new ScoreDto[0]);
        static CachedJson latest = new CachedJson(null);
        static readonly List<Dictionary<string, string>> session = new List<Dictionary<string, string>>();
        static CachedJson sessionJson = new CachedJson(new object[0]);
        static CachedJson clearRates = new CachedJson(new ClearRateDto[0]);
        /* The same data as objects, for the master tables (replaced, never changed in place) */
        static List<SongDto> songList = new List<SongDto>();
        static List<ChartDto> chartList = new List<ChartDto>();
        static List<ClearRateDto> clearRateList = new List<ClearRateDto>();
        static string clearRatesKey = "";
        static CachedJson rivals = new CachedJson(new RivalDto[0]);
        static CachedJson rivalScores = new CachedJson(new RivalScoreDto[0]);
        static string rivalsKey = "";
        static bool rivalsLoaded;
        static DateTime readyAt, lastRivalTry;
        static GameState gameState;
        static readonly SortedSet<string> dataWarnings = new SortedSet<string>();

        /// <summary>Mark a kind of data (rivals, clearRates, playCounts) as unreadable, or readable again with ok</summary>
        public static void SetDataWarning(string what, bool unreadable)
        {
            lock (sync)
            {
                if (!(unreadable ? dataWarnings.Add(what) : dataWarnings.Remove(what))) { return; }
                status.DataWarnings = dataWarnings.ToList();
            }
        }

        /// <summary>
        /// Main loop while the song list doesn't pass Layout.CheckSongs: loading at first (the game may still be filling it),
        /// layoutMismatch when it still fails 30 s after loading started
        /// </summary>
        public static void SongListNotReady(string check)
        {
            bool mismatch;
            lock (sync)
            {
                mismatch = status.AttachState == AttachState.LayoutMismatch ||
                    (status.AttachState == AttachState.Loading && (DateTime.UtcNow - status.AttachStateSince).TotalSeconds >= 30);
            }
            if (mismatch)
            {
                SetAttachState(AttachState.LayoutMismatch, $"Song list still doesn't look right ({check}): the struct layout probably changed with an INFINITAS update. Check it with GET /api/layout/preview and fix it with PUT /api/layout");
            }
            else
            {
                SetAttachState(AttachState.Loading, $"Song list doesn't look right yet ({check}), retrying");
            }
        }

        /// <summary>
        /// gameState as the API reports it (call under sync): "starting" instead of songSelect after attaching until the game has
        /// received its data. The rivals arrive last (during NOW LOADING, seen on 2026101400), so this waits for them, at most as
        /// long as zero rivals isn't accepted (players without rivals, or a rival layout that no longer matches)
        /// </summary>
        static void UpdateGameStateText()
        {
            if (status.GameState == null) { return; } /* Not attached */
            status.GameState = gameState == GameState.songSelect && !rivalsLoaded && (DateTime.UtcNow - readyAt).TotalMinutes < 2 ? "starting" : gameState.ToString();
        }

        public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) },
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        };

        static ApiState()
        {
            status.RefluxVersion = BuildInfo.DisplayVersion;
            status.AttachState = AttachState.WaitingForProcess;
            status.AttachStateSince = DateTime.UtcNow;
        }

        #region Writers (main loop)
        public static void SetAttachState(AttachState state, string message = null)
        {
            lock (sync)
            {
                if (status.AttachState != state)
                {
                    status.AttachStateSince = DateTime.UtcNow;
                }
                status.AttachState = state;
                status.Message = message;
                if (state == AttachState.WaitingForProcess)
                {
                    status.ProcessId = null;
                    status.GameVersion = null;
                    status.InfinitasId = null;
                    status.DjName = null;
                    status.GameState = null;
                    dataWarnings.Clear();
                    status.DataWarnings = new List<string>();
                    status.CurrentChart = null;
                    status.PlayStateDetection = false;
                }
            }
        }
        public static void SetProcess(int pid)
        {
            lock (sync) { status.ProcessId = pid; }
        }
        public static string GameVersion { get { lock (sync) { return status.GameVersion; } } }
        /// <summary>Songs, charts and clear rates as published (for MasterTables, don't modify)</summary>
        public static (List<SongDto> songs, List<ChartDto> charts, List<ClearRateDto> rates) MasterSource()
        {
            lock (sync) { return (songList, chartList, clearRateList); }
        }
        public static void SetVersions(string gameVersion, string offsetsVersion)
        {
            lock (sync)
            {
                status.GameVersion = gameVersion;
                status.OffsetsVersion = offsetsVersion;
            }
        }
        public static void SetReady(bool playStateDetection)
        {
            lock (sync) { status.PlayStateDetection = playStateDetection; }
            CoreSettings.MarkTrackerChanged(); /* Write tracker.tsv once after attaching, whatever the mode */
            lock (sync) { rivalsLoaded = false; readyAt = DateTime.UtcNow; } /* Read rivals again after (re)attaching */
            var infinitasId = Player.ReadInfinitasId(); /* The pass can be switched between INFINITAS starts */
            var djName = Player.ReadDjName(infinitasId);
            lock (sync) { status.InfinitasId = infinitasId; status.DjName = djName; }
            if (infinitasId != null) { PlayLog.ImportSessionsOnce(infinitasId); }
            SetGameState(GameState.songSelect); /* The main loop starts from songSelect and only reports changes */
            SetAttachState(AttachState.Ready, playStateDetection ? null : "Play state marker not found, play results are not recorded");
        }
        /// <summary>The DJ NAME when it wasn't there at attach time (the profile is loaded after the login)</summary>
        static void RetryDjName()
        {
            string id;
            lock (sync)
            {
                if (status.DjName != null || status.InfinitasId == null) { return; }
                id = status.InfinitasId;
            }
            var name = Player.ReadDjName(id);
            if (name != null) { lock (sync) { status.DjName = name; } }
        }

        public static void SetGameState(GameState state)
        {
            CurrentChartDto current = null;
            if (state == GameState.playing)
            {
                try
                {
                    var c = Utils.FetchCurrentChart();
                    current = new CurrentChartDto
                    {
                        SongId = c.songID,
                        Title = Utils.songDb.TryGetValue(c.songID, out var s) ? s.title : null,
                        Difficulty = c.difficulty.ToString(),
                    };
                }
                catch (Exception e)
                {
                    Utils.Except(e, "ApiCurrentChart");
                }
            }
            lock (sync)
            {
                gameState = state;
                status.GameState = state.ToString();
                UpdateGameStateText();
                if (state == GameState.playing)
                {
                    status.CurrentChart = current;
                }
            }
            if (state == GameState.songSelect)
            {
                CoreSettings.OnSongSelect();
                if (PlayCounts.Refresh()) { PublishScores(); } /* The game updates clear / play counts after every play */
                ClearRates.Refresh(); /* Server statistics may have been updated while playing */
                RetryDjName();
                if (SongReadings.ShouldRetry()) { PublishSongs(); } /* The reading table wasn't there yet when the songs were published */
                bool loaded;
                lock (sync) { loaded = rivalsLoaded; }
                if (!loaded) { Rivals.Refresh(); } /* The game fetches rivals only at startup, retry until both sides were read */
            }
        }
        /// <summary>
        /// Main loop, every song select poll: retry the rivals every 5 s until both sides were read. Reflux can become ready while
        /// the game is still receiving them (seen on 2026101400: ready 2 s before the rivals arrived, during NOW LOADING)
        /// </summary>
        public static void RetryRivals()
        {
            lock (sync)
            {
                UpdateGameStateText(); /* Ends "starting" after the time limit too */
                /* Every 5 s in the first minutes (the game receives them while starting), then every minute */
                var every = (DateTime.UtcNow - readyAt).TotalMinutes < 2 ? 5 : 60;
                if (rivalsLoaded || (DateTime.UtcNow - lastRivalTry).TotalSeconds < every) { return; }
                lastRivalTry = DateTime.UtcNow;
            }
            Rivals.Refresh();
        }
        /// <summary>No rivals in the game's data counts as read only a while after attaching (players without rivals)</summary>
        public static bool AcceptNoRivals { get { lock (sync) { return (DateTime.UtcNow - readyAt).TotalMinutes >= 2; } } }
        public static int? ProcessId { get { lock (sync) { return status.ProcessId; } } }
        /// <summary>Publish rivals and their scores, the revision only changes when something changed</summary>
        public static void PublishRivals(List<RivalDto> list, List<RivalScoreDto> scores, bool complete)
        {
            var key = string.Join(";", list.Select(r => $"{r.Side}{r.Index}{r.InfinitasId}{r.Charts}")) + "|" + scores.Sum(s => (long)s.ExScore);
            lock (sync)
            {
                rivalsLoaded = complete;
                UpdateGameStateText();
                if (key == rivalsKey) { return; }
                rivalsKey = key;
                rivals = new CachedJson(list);
                rivalScores = new CachedJson(scores);
                status.RivalsRevision++;
            }
        }
        /// <summary>Publish clear rates, the revision only changes when a value changed</summary>
        public static void PublishClearRates(List<ClearRateDto> rates)
        {
            var key = string.Join(";", rates.Select(r => $"{r.SongId}{r.Difficulty}{r.Rate}"));
            lock (sync)
            {
                if (key == clearRatesKey) { return; }
                clearRatesKey = key;
                clearRates = new CachedJson(rates);
                clearRateList = rates;
                status.ClearRatesRevision++;
            }
        }
        /// <summary>Publish song and chart data, call after song db and unlock db are loaded</summary>
        public static void PublishSongs()
        {
            try
            {
                var (s, c) = ApiDataBuilder.BuildSongs();
                lock (sync)
                {
                    songs = new CachedJson(s);
                    charts = new CachedJson(c);
                    songList = s;
                    chartList = c;
                    status.SongsRevision++;
                }
            }
            catch (Exception e)
            {
                Utils.Except(e, "ApiPublishSongs");
            }
        }
        /// <summary>Publish score data, call after tracker db or unlock db changed</summary>
        public static void PublishScores()
        {
            try
            {
                var s = ApiDataBuilder.BuildScores();
                lock (sync)
                {
                    scores = new CachedJson(s);
                    status.ScoresRevision++;
                }
            }
            catch (Exception e)
            {
                Utils.Except(e, "ApiPublishScores");
            }
        }
        /// <summary>
        /// Called after a play result has been processed by the main loop
        /// </summary>
        public static void OnPlayRecorded(PlayData play)
        {
            CoreSettings.MarkTrackerChanged();
            try
            {
                var form = play.ToPostForm();
                form.Remove("apikey");
                form["timestamp"] = DateTime.UtcNow.ToString("o");
                if (play.Unsaved)
                {
                    /* DOUBLE BATTLE / H-RANDOM: built by Reflux (UnsavedPlay), not kept by the game */
                    form["unsaved"] = "true";
                    form["battle"] = play.Battle.ToString().ToLower();
                    form["hran"] = play.Hran.ToString().ToLower();
                }

                if (play.DataAvailable && !play.Unsaved)
                {
                    var c = new Chart() { songID = play.Chart.songid, difficulty = play.Chart.difficulty };
                    if (!Config.Save_local)
                    {
                        /* Main loop only updates the tracker when saving locally, keep in-memory bests current for the API */
                        if (Tracker.trackerDb.TryGetValue(c, out var e))
                        {
                            e.grade = (Grade)Math.Max((int)e.grade, (int)play.Grade);
                            e.lamp = (Lamp)Math.Max((int)e.lamp, (int)play.Lamp);
                            if (play.MissCountValid) { e.misscount = Math.Min(e.misscount, play.MissCount); }
                            e.ex_score = Math.Max(e.ex_score, play.ExScore);
                            Tracker.trackerDb[c] = e;
                        }
                    }
                    if (Tracker.trackerDb.TryGetValue(c, out var best))
                    {
                        BestDetail.Update(c.songID, c.difficulty, best, play.ExScore,
                            int.Parse(form["pgreat"]), int.Parse(form["great"]),
                            play.MissCountValid ? (int)play.MissCount : (int?)null,
                            int.Parse(form["bad"]), int.Parse(form["poor"]));
                    }
                }

                if (play.DataAvailable)
                {
                    /* Plays read from the current chart fallback (no play data) are not kept, as with the session TSV */
                    string id;
                    lock (sync) { id = status.InfinitasId; }
                    int? miss = play.MissCountValid ? (int)play.MissCount : (int?)null;
                    var prev = play.Unsaved ? null : PlayCounts.PrevBest(play.Chart.songid, play.Chart.difficulty.ToString());
                    PlayLog.Append(form, id, miss, prev);
                    /* A play ended prematurely still raises the game's EX score / lamp (miss is null for it); not one the game doesn't keep */
                    if (!play.Unsaved) { PlayCounts.Merge(play.Chart.songid, play.Chart.difficulty.ToString(), play.Lamp, play.ExScore, miss); }
                }
                lock (sync)
                {
                    session.Add(form);
                    sessionJson = new CachedJson(session.ToArray());
                    latest = new CachedJson(form);
                    status.SessionPlays = session.Count;
                }
                if (play.DataAvailable)
                {
                    PublishScores();
                }
            }
            catch (Exception e)
            {
                Utils.Except(e, "ApiPlayRecorded");
            }
        }
        /// <summary>
        /// Returns true once if a client requested re-attach
        /// </summary>
        public static bool ConsumeRetry()
        {
            return Interlocked.Exchange(ref retryRequested, 0) == 1;
        }
        #endregion

        #region Readers (API thread)
        public static void RequestRetry()
        {
            Interlocked.Exchange(ref retryRequested, 1);
        }
        public static string StatusJson()
        {
            StatusDto copy;
            lock (sync)
            {
                copy = status.Clone();
            }
            copy.ServerTime = DateTime.UtcNow;
            return JsonConvert.SerializeObject(copy, JsonSettings);
        }
        public static string SongsJson() { lock (sync) { return songs.Json; } }
        public static string ChartsJson() { lock (sync) { return charts.Json; } }
        public static string ScoresJson() { lock (sync) { return scores.Json; } }
        public static string LatestJson() { lock (sync) { return latest.Json; } }
        public static string SessionJson() { lock (sync) { return sessionJson.Json; } }
        public static string ClearRatesJson() { lock (sync) { return clearRates.Json; } }
        public static string RivalsJson() { lock (sync) { return rivals.Json; } }
        public static string RivalScoresJson() { lock (sync) { return rivalScores.Json; } }
        #endregion

        /// <summary>
        /// Immutable data serialized at most once, on first request
        /// </summary>
        class CachedJson
        {
            readonly object data;
            string json;
            public CachedJson(object data) { this.data = data; }
            public string Json => json ??= JsonConvert.SerializeObject(data, JsonSettings);
        }
    }
}
