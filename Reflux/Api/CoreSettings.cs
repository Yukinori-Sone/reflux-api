using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Reflux.Api
{
    /// <summary>
    /// Runtime settings of the main loop that can be changed through the API (GET|PUT /api/settings) and are kept in
    /// coresettings.json. Defaults behave like upstream Reflux. config.ini is left in upstream format.
    /// </summary>
    static class CoreSettings
    {
        const string FileName = "coresettings.json";
        public static readonly int[] PollIntervals = { 500, 1000, 2000, 3000, 5000 };
        public static readonly string[] TrackerTsvModes = { "always", "onChange", "never" };
        public static readonly string[] Priorities = { "normal", "belowNormal", "idle" };

        class Values
        {
            /// <summary>Main loop interval</summary>
            public int PollIntervalMs = 2000;
            /// <summary>Unlock state check on song select: 0 every poll, -1 only when returning to song select, N at most every N seconds</summary>
            public int UnlockCheckSeconds = 0;
            /// <summary>tracker.tsv on song select: always (every poll), onChange (after a play, an unlock change or attach), never</summary>
            public string TrackerTsv = "always";
            /// <summary>Process priority of Reflux itself</summary>
            public string ProcessPriority = "normal";
        }

        static readonly object sync = new object();
        static Values values = Load();
        static bool unlockCheckDue = true;
        static DateTime lastUnlockCheck = DateTime.MinValue;
        static bool trackerDirty = true;

        static CoreSettings() => ApplyPriority();

        static Values Load()
        {
            var v = new Values();
            if (!File.Exists(FileName)) { return v; }
            try
            {
                var err = Merge(v, JObject.Parse(File.ReadAllText(FileName)));
                if (err != null) { throw new FormatException(err); }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[API] {FileName} is invalid, using defaults: {e.Message}");
                v = new Values();
            }
            return v;
        }

        static string Merge(Values v, JObject json)
        {
            foreach (var p in json.Properties())
            {
                switch (p.Name.ToLowerInvariant())
                {
                    case "pollintervalms":
                        if (p.Value.Type != JTokenType.Integer || !PollIntervals.Contains((int)p.Value)) { return $"pollIntervalMs must be one of {string.Join(", ", PollIntervals)}"; }
                        v.PollIntervalMs = (int)p.Value;
                        break;
                    case "unlockcheckseconds":
                        if (p.Value.Type != JTokenType.Integer || (int)p.Value < -1 || (int)p.Value > 3600) { return "unlockCheckSeconds must be -1 (on return to song select), 0 (every poll) or 1..3600"; }
                        v.UnlockCheckSeconds = (int)p.Value;
                        break;
                    case "trackertsv":
                        var t = TrackerTsvModes.FirstOrDefault(m => string.Equals(m, (string)p.Value, StringComparison.OrdinalIgnoreCase));
                        if (t == null) { return $"trackerTsv must be one of {string.Join(", ", TrackerTsvModes)}"; }
                        v.TrackerTsv = t;
                        break;
                    case "processpriority":
                        var pr = Priorities.FirstOrDefault(m => string.Equals(m, (string)p.Value, StringComparison.OrdinalIgnoreCase));
                        if (pr == null) { return $"processPriority must be one of {string.Join(", ", Priorities)}"; }
                        v.ProcessPriority = pr;
                        break;
                    default:
                        return $"unknown setting {p.Name} (known: pollIntervalMs, unlockCheckSeconds, trackerTsv, processPriority)";
                }
            }
            return null;
        }

        static void ApplyPriority()
        {
            try
            {
                Process.GetCurrentProcess().PriorityClass = values.ProcessPriority switch
                {
                    "belowNormal" => ProcessPriorityClass.BelowNormal,
                    "idle" => ProcessPriorityClass.Idle,
                    _ => ProcessPriorityClass.Normal,
                };
            }
            catch (Exception e)
            {
                Utils.Except(e, "CoreSettingsPriority");
            }
        }

        #region Main loop
        public static int PollIntervalMs { get { lock (sync) { return values.PollIntervalMs; } } }

        /// <summary>Called by ApiState when the game (re)enters song select, including right after attaching</summary>
        public static void OnSongSelect()
        {
            lock (sync) { unlockCheckDue = true; }
        }

        /// <summary>tracker.tsv content changed (a play was recorded, or Reflux attached)</summary>
        public static void MarkTrackerChanged()
        {
            lock (sync) { trackerDirty = true; }
        }

        /// <summary>Whether the main loop should read the unlock states on this song select poll</summary>
        public static bool ShouldCheckUnlocks()
        {
            lock (sync)
            {
                var now = DateTime.UtcNow;
                bool check = values.UnlockCheckSeconds switch
                {
                    0 => true,
                    -1 => unlockCheckDue,
                    var s => unlockCheckDue || (now - lastUnlockCheck).TotalSeconds >= s,
                };
                if (check)
                {
                    unlockCheckDue = false;
                    lastUnlockCheck = now;
                }
                return check;
            }
        }

        /// <summary>Whether the main loop should write tracker.tsv on this song select poll</summary>
        public static bool ShouldWriteTrackerTsv(bool unlocksChanged)
        {
            lock (sync)
            {
                switch (values.TrackerTsv)
                {
                    case "never":
                        return false;
                    case "onChange":
                        bool write = trackerDirty || unlocksChanged;
                        trackerDirty = false;
                        return write;
                    default:
                        return true;
                }
            }
        }
        #endregion

        #region Endpoints
        static JObject ToJson(Values v) => JObject.FromObject(v, JsonSerializer.Create(ApiState.JsonSettings));

        /// <summary>GET /api/settings: current values, defaults and the allowed choices</summary>
        public static string GetJson()
        {
            lock (sync)
            {
                return JsonConvert.SerializeObject(new
                {
                    file = File.Exists(FileName) ? FileName : null,
                    values = ToJson(values),
                    defaults = ToJson(new Values()),
                    choices = new
                    {
                        pollIntervalMs = PollIntervals,
                        unlockCheckSeconds = "-1 = only when returning to song select, 0 = every poll, 1..3600 = at most every N seconds",
                        trackerTsv = TrackerTsvModes,
                        processPriority = Priorities,
                    },
                }, ApiState.JsonSettings);
            }
        }

        /// <summary>PUT /api/settings: { "pollIntervalMs": 2000, ... }, only the given settings change. Applied immediately and saved</summary>
        public static (int code, string json) Put(string body)
        {
            JObject req;
            try { req = JObject.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body); }
            catch (JsonException e) { return (400, JsonConvert.SerializeObject(new { error = "bad_json", message = e.Message }, ApiState.JsonSettings)); }
            lock (sync)
            {
                var v = ToJson(values).ToObject<Values>(JsonSerializer.Create(ApiState.JsonSettings));
                var err = Merge(v, req);
                if (err != null) { return (400, JsonConvert.SerializeObject(new { error = "bad_setting", message = err }, ApiState.JsonSettings)); }
                File.WriteAllText(FileName + ".tmp", ToJson(v).ToString(Formatting.Indented));
                File.Move(FileName + ".tmp", FileName, true);
                values = v;
                trackerDirty = true; /* Write once with the new mode */
                unlockCheckDue = true;
            }
            ApplyPriority();
            Console.WriteLine($"[API] {FileName} updated by client");
            return (200, GetJson());
        }
        #endregion
    }
}
