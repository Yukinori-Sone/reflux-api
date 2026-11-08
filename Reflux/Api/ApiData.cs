using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Reflux.Api
{
    /* DTOs returned by the API. Song, chart and score are kept separate so clients can store them as separate tables. */

    class SongDto
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Title2 { get; set; }
        public string Artist { get; set; }
        public string Genre { get; set; }
        /// <summary>Version folder number as stored in the game (series)</summary>
        public int Folder { get; set; }
        /// <summary>Base / Bits / Sub</summary>
        public string UnlockType { get; set; }
        /// <summary>Label from customtypes.txt (song pack, DJP unlock etc.), null if none</summary>
        public string Label { get; set; }
        /// <summary>Song pack item ID from the unlock data ("I1800001".."), null if the song isn't sold in a pack</summary>
        public string PackItemId { get; set; }
        /// <summary>Title reading in katakana (song select sort order), null when not read</summary>
        public string TitleReading { get; set; }
        /// <summary>Artist reading in katakana</summary>
        public string ArtistReading { get; set; }
        /// <summary>Artist in upper-case latin letters</summary>
        public string ArtistEnglish { get; set; }
        /// <summary>False when the song is missing from the game's unlock data: it isn't shown on song select at all
        /// (unlike a locked song, which is shown greyed out and can be unlocked)</summary>
        public bool Listed { get; set; }
    }

    class ChartDto
    {
        public string SongId { get; set; }
        /// <summary>SPB, SPN, SPH, SPA, SPL, DPN, DPH, DPA, DPL</summary>
        public string Difficulty { get; set; }
        public int Level { get; set; }
        public int Notes { get; set; }
        /// <summary>"150" or "100~200"</summary>
        public string Bpm { get; set; }
    }

    class ScoreDto
    {
        public string SongId { get; set; }
        public string Difficulty { get; set; }
        public bool Unlocked { get; set; }
        /// <summary>NP, F, AC, EC, NC, HC, EX, FC, PFC</summary>
        public string Lamp { get; set; }
        /// <summary>NP, F, E, D, C, B, A, AA, AAA</summary>
        public string Grade { get; set; }
        public int ExScore { get; set; }
        /// <summary>null when unknown (no play etc.)</summary>
        public int? MissCount { get; set; }
        /// <summary>Judge breakdown of the best EX score, only known if the play was recorded by Reflux</summary>
        public int? PGreat { get; set; }
        public int? Great { get; set; }
        /// <summary>Judge breakdown of the best miss count, only known if the play was recorded by Reflux</summary>
        public int? Bad { get; set; }
        public int? Poor { get; set; }
        /// <summary>Clear / play counts kept by the game, null until read (Api/PlayCounts.cs)</summary>
        public int? ClearCount { get; set; }
        public int? PlayCount { get; set; }
    }

    /// <summary>
    /// Builds API DTOs from the static databases. Must be called from the main (memory reading) thread.
    /// </summary>
    static class ApiDataBuilder
    {
        public static (List<SongDto> songs, List<ChartDto> charts) BuildSongs()
        {
            var songs = new List<SongDto>();
            var charts = new List<ChartDto>();
            var packs = ReadPackItemIds();
            var readings = SongReadings.Read();
            foreach (var kv in Utils.songDb)
            {
                var song = kv.Value;
                songs.Add(new SongDto
                {
                    Id = song.ID,
                    Title = song.title,
                    Title2 = song.title_english,
                    Artist = song.artist,
                    Genre = song.genre,
                    Folder = song.folder,
                    UnlockType = song.type.ToString(),
                    Label = Utils.customTypes.TryGetValue(song.ID, out var label) ? label : null,
                    Listed = Utils.unlockDb.ContainsKey(song.ID),
                    PackItemId = packs.TryGetValue(song.ID, out var pack) ? pack : null,
                    TitleReading = readings.TryGetValue(song.ID, out var reading) ? reading.Title : null,
                    ArtistReading = reading?.Artist,
                    ArtistEnglish = reading?.ArtistEnglish,
                });
                for (int i = 0; i < song.level.Length; i++)
                {
                    if (song.level[i] == 0) { continue; } /* Chart doesn't exist */
                    charts.Add(new ChartDto
                    {
                        SongId = song.ID,
                        Difficulty = ((Difficulty)i).ToString(),
                        Level = song.level[i],
                        Notes = song.totalNotes[i],
                        Bpm = song.bpms[i],
                    });
                }
            }
            return (songs, charts);
        }

        /// <summary>Song pack item IDs of the unlock data entries (layout unlock.packItemId), song ID -> item ID. Main loop thread only</summary>
        static Dictionary<string, string> ReadPackItemIds()
        {
            var result = new Dictionary<string, string>();
            try
            {
                var l = Layout.Unlock;
                int count = Utils.unlockDb.Count;
                var buf = Utils.ReadRaw(Offsets.UnlockData, l.EntrySize * count);
                var e = new byte[l.EntrySize];
                for (int i = 0; i < count; i++)
                {
                    Array.Copy(buf, i * l.EntrySize, e, 0, l.EntrySize);
                    int id = BitConverter.ToInt32(e, l.Id);
                    if (id == 0) { break; }
                    var pack = Layout.ReadString(e, l.PackItemId);
                    if (pack != "") { result[id.ToString("D5")] = pack; }
                }
            }
            catch (Exception ex)
            {
                Utils.Except(ex, "ApiPackItemIds");
            }
            return result;
        }

        public static List<ScoreDto> BuildScores()
        {
            var result = new List<ScoreDto>();
            foreach (var kv in Tracker.trackerDb)
            {
                var chart = kv.Key;
                var info = kv.Value;
                if (!Utils.songDb.ContainsKey(chart.songID)) { continue; }

                bool played = !(info.lamp == Reflux.Lamp.NP && info.ex_score == 0);
                int? miss = (!played || info.misscount == uint.MaxValue) ? (int?)null : (int)info.misscount;
                var detail = BestDetail.Get(chart);

                var dto = new ScoreDto
                {
                    SongId = chart.songID,
                    Difficulty = chart.difficulty.ToString(),
                    Unlocked = IsUnlocked(chart),
                    Lamp = info.lamp.ToString(),
                    Grade = info.grade.ToString(),
                    ExScore = info.ex_score,
                    MissCount = miss,
                };
                if (PlayCounts.Get(chart.songID, chart.difficulty.ToString()) is (int clear, int play))
                {
                    dto.ClearCount = clear;
                    dto.PlayCount = play;
                }
                if (detail != null && detail.ExScore == info.ex_score && played)
                {
                    dto.PGreat = detail.PGreat;
                    dto.Great = detail.Great;
                }
                if (detail != null && miss.HasValue && detail.MissCount == miss)
                {
                    dto.Bad = detail.Bad;
                    dto.Poor = detail.Poor;
                }
                result.Add(dto);
            }
            return result;
        }

        /// <summary>
        /// Same rule as the tracker.tsv output (Tracker.GetTrackerEntries)
        /// </summary>
        static bool IsUnlocked(Chart chart)
        {
            if (!Utils.unlockDb.ContainsKey(chart.songID)) { return false; }
            if (chart.difficulty == Difficulty.SPL || chart.difficulty == Difficulty.DPL)
            {
                var type = Utils.unlockDb[chart.songID].type;
                return type != UnlockType.Sub
                    || (Utils.GetUnlockStateForDifficulty(chart.songID, Difficulty.SPA) && Utils.GetUnlockStateForDifficulty(chart.songID, Difficulty.DPA));
            }
            return Utils.GetUnlockStateForDifficulty(chart.songID, chart.difficulty);
        }
    }

    /// <summary>
    /// Judge breakdown of best plays recorded by Reflux. The game itself only keeps EX score and miss count.
    /// Persisted to bestdetail.json.
    /// </summary>
    static class BestDetail
    {
        public class Entry
        {
            public int? ExScore { get; set; }
            public int? PGreat { get; set; }
            public int? Great { get; set; }
            public int? MissCount { get; set; }
            public int? Bad { get; set; }
            public int? Poor { get; set; }
        }

        const string FileName = "bestdetail.json";
        static Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

        static string Key(string songId, Difficulty diff) => $"{songId}_{diff}";

        public static void Load()
        {
            try
            {
                if (File.Exists(FileName))
                {
                    entries = JsonConvert.DeserializeObject<Dictionary<string, Entry>>(File.ReadAllText(FileName))
                        ?? new Dictionary<string, Entry>();
                }
            }
            catch (Exception e)
            {
                Utils.Except(e, "BestDetailLoad");
                entries = new Dictionary<string, Entry>();
            }
        }

        public static Entry Get(Chart chart)
        {
            return entries.TryGetValue(Key(chart.songID, chart.difficulty), out var e) ? e : null;
        }

        /// <summary>
        /// Store breakdown if the play matches the current best EX score / miss count
        /// </summary>
        public static void Update(string songId, Difficulty diff, TrackerInfo best, int exScore, int pgreat, int great, int? missCount, int bad, int poor)
        {
            var key = Key(songId, diff);
            if (!entries.TryGetValue(key, out var entry))
            {
                entry = new Entry();
            }
            bool changed = false;
            if (exScore > 0 && exScore == best.ex_score)
            {
                entry.ExScore = exScore;
                entry.PGreat = pgreat;
                entry.Great = great;
                changed = true;
            }
            if (missCount.HasValue && (uint)missCount.Value == best.misscount)
            {
                entry.MissCount = missCount;
                entry.Bad = bad;
                entry.Poor = poor;
                changed = true;
            }
            if (!changed) { return; }
            entries[key] = entry;
            try
            {
                File.WriteAllText(FileName, JsonConvert.SerializeObject(entries, Formatting.Indented));
            }
            catch (Exception e)
            {
                Utils.Except(e, "BestDetailSave");
            }
        }
    }
}
