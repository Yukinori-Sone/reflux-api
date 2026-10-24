using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Reflux.Api
{
    /// <summary>
    /// Play history kept by the core in plays.jsonl (one JSON object per line, appended), independent of the session TSV
    /// settings. Every record has the INFINITAS ID that played it (one KONAMI ID can switch between several e-amusement
    /// passes, each with its own INFINITAS ID). Served by GET /api/plays.
    /// </summary>
    static class PlayLog
    {
        const string FileName = "plays.jsonl";
        static readonly object fileLock = new object();

        /// <summary>Append one recorded play. form is the same dictionary as /api/latest</summary>
        public static void Append(Dictionary<string, string> form, string infinitasId, int? missCount)
        {
            if (!Config.ApiServer_Enabled) { return; } /* Without the API Reflux writes only what upstream writes */
            var o = new JObject { ["infinitasId"] = infinitasId, ["source"] = "reflux" };
            foreach (var kv in form) { o[kv.Key] = kv.Value; }
            o["misscount"] = missCount?.ToString();
            lock (fileLock)
            {
                File.AppendAllText(FileName, o.ToString(Formatting.None) + "\n", new UTF8Encoding(false));
            }
        }

        /// <summary>Records with timestamp after since (all when null), oldest first, at most limit</summary>
        public static List<JObject> Read(DateTime? since, int limit, string infinitasId)
        {
            var result = new List<JObject>();
            lock (fileLock)
            {
                if (!File.Exists(FileName)) { return result; }
                using var stream = new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) { continue; }
                    JObject o;
                    try { o = JObject.Parse(line); } catch (JsonException) { continue; }
                    if (since.HasValue && (!DateTime.TryParse((string)o["timestamp"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) || t <= since.Value)) { continue; }
                    if (infinitasId != null && (string)o["infinitasId"] != infinitasId) { continue; }
                    result.Add(o);
                }
            }
            return result.Take(limit).ToList();
        }

        /// <summary>
        /// Once, when plays.jsonl doesn't exist yet: import the plays in sessions/Session_*.tsv so the history starts with what
        /// Reflux already recorded. The TSV has no song ID or INFINITAS ID: songs are matched by title, the ID is the current one.
        /// Call from the main loop thread after the song list is loaded.
        /// </summary>
        public static void ImportSessionsOnce(string infinitasId)
        {
            if (!Config.ApiServer_Enabled) { return; }
            try
            {
                lock (fileLock)
                {
                    if (File.Exists(FileName) || !Directory.Exists("sessions")) { return; }
                    var byTitle = new Dictionary<string, string>();
                    foreach (var s in Utils.songDb.Values) { byTitle.TryAdd(s.title, s.ID); }
                    var records = new List<JObject>();
                    foreach (var path in Directory.GetFiles("sessions", "Session_*.tsv").OrderBy(p => p))
                    {
                        var lines = File.ReadAllLines(path, Encoding.UTF8);
                        if (lines.Length < 2) { continue; }
                        var header = lines[0].Split('\t');
                        foreach (var line in lines.Skip(1))
                        {
                            var f = line.Split('\t');
                            if (f.Length != header.Length) { continue; }
                            var row = header.Zip(f, (h, v) => (h, v)).ToDictionary(x => x.h, x => x.v);
                            if (!row.TryGetValue("title", out var title) || !byTitle.TryGetValue(title, out var songId)) { continue; }
                            if (!row.TryGetValue("date", out var date) ||
                                !DateTime.TryParseExact(date, "yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) { continue; }
                            t = Config.UseLocaltime ? t.ToUniversalTime() : DateTime.SpecifyKind(t, DateTimeKind.Utc);
                            var o = new JObject
                            {
                                ["infinitasId"] = infinitasId,
                                ["source"] = "sessionTsv",
                                ["timestamp"] = t.ToString("o"),
                                ["songid"] = songId,
                                ["diff"] = row.GetValueOrDefault("difficulty"),
                            };
                            foreach (var (key, name) in new[] {
                                ("title", "title"), ("title2", "title2"), ("bpm", "bpm"), ("artist", "artist"), ("genre", "genre"),
                                ("notecount", "notecount"), ("level", "level"), ("playtype", "playtype"), ("grade", "grade"), ("lamp", "lamp"),
                                ("misscount", "misscount"), ("gaugepercent", "gaugepercent"), ("exscore", "exscore"), ("pgreat", "pgreat"),
                                ("great", "great"), ("good", "good"), ("bad", "bad"), ("poor", "poor"), ("combobreak", "combobreak"),
                                ("fast", "fast"), ("slow", "slow"), ("style", "style"), ("style2", "style2"), ("gauge", "gauge"),
                                ("assist", "assist"), ("range", "range") })
                            {
                                if (row.TryGetValue(key, out var v)) { o[name] = v == "-" ? null : v; }
                            }
                            records.Add(o);
                        }
                    }
                    var text = string.Concat(records.OrderBy(o => (string)o["timestamp"], StringComparer.Ordinal).Select(o => o.ToString(Formatting.None) + "\n"));
                    File.WriteAllText(FileName, text, new UTF8Encoding(false));
                    Console.WriteLine($"[API] {FileName} created, {records.Count} plays imported from the session TSV files");
                }
            }
            catch (Exception e)
            {
                Utils.Except(e, "PlayLogImport");
            }
        }
    }

    /// <summary>The logged-in player as stored by the game (see PlayerLayout)</summary>
    static class Player
    {
        static readonly Regex IdPattern = new Regex(@"^C\d{12}$");

        /// <summary>INFINITAS ID of the logged-in player ("C" + 12 digits), null when not found. Main loop thread only</summary>
        public static string ReadInfinitasId()
        {
            try
            {
                var pid = ApiState.ProcessId;
                if (pid == null) { return null; }
                long moduleBase = (long)Process.GetProcessById(pid.Value).MainModule.BaseAddress;
                /* Only the 13 characters of the ID are read: the login token that follows it is never touched */
                var id = Encoding.ASCII.GetString(Utils.ReadRaw(moduleBase + Layout.Player.InfinitasId, 13));
                if (IdPattern.IsMatch(id)) { return id; }
                Console.WriteLine("[API] INFINITAS ID not found. Check layout.json player.infinitasId");
                return null;
            }
            catch (Exception e)
            {
                Utils.Except(e, "PlayerId");
                return null;
            }
        }
    }
}
