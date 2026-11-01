using System;
using System.Collections.Generic;
using System.Linq;

namespace Reflux.Api
{
    /// <summary>One chart of the game's personal best hash map</summary>
    struct GameBest
    {
        public int Clear, Play, ExScore;
        public int? MissCount;
        public Lamp Lamp;
    }

    /// <summary>
    /// Clear and play counts of every chart, kept by the game in the personal best hash map (offsets.txt dataMap, nodes at
    /// scoreMap.clearCount / playCount). The game updates them after each play, so they are re-read when the game returns
    /// to song select (from ApiState.SetGameState, main loop thread) and published with /api/scores.
    /// The same walk keeps the game's own lamp / EX score / miss count of the logged in player: the best before the next play,
    /// recorded with each play as prevLamp / prevExScore / prevMissCount (PrevBest). tracker.db is not used for that, its lamps
    /// are the maximum with the game's and are not kept by player.
    /// </summary>
    static class PlayCounts
    {
        const int MaxNodes = 200000;
        const long SkipBucket = 0x494fdce0; /* Skipped by ScoreMap.LoadMap as well */
        static readonly object sync = new object();
        static Dictionary<(string song, string difficulty), GameBest> current = new Dictionary<(string, string), GameBest>();
        static bool available;
        static string currentKey = "";

        public static (int clear, int play)? Get(string song, string difficulty)
        {
            lock (sync) { return current.TryGetValue((song, difficulty), out var v) ? (v.Clear, v.Play) : ((int, int)?)null; }
        }

        /// <summary>
        /// The game's best of the chart as of the last song select (plus the plays recorded since, see Merge). A chart missing
        /// from the map was never played (NP). Null when the map could not be read.
        /// </summary>
        public static (Lamp lamp, int exScore, int? missCount)? PrevBest(string song, string difficulty)
        {
            lock (sync)
            {
                if (!available) { return null; }
                return current.TryGetValue((song, difficulty), out var v) ? (v.Lamp, v.ExScore, v.MissCount) : (Lamp.NP, 0, (int?)null);
            }
        }

        /// <summary>
        /// Fold a recorded play into the kept bests until the next song select re-reads the game (quick retry plays again without
        /// going back to song select)
        /// </summary>
        public static void Merge(string song, string difficulty, Lamp lamp, int exScore, int? missCount)
        {
            lock (sync)
            {
                if (!available) { return; }
                current.TryGetValue((song, difficulty), out var v);
                if (lamp > v.Lamp) { v.Lamp = lamp; }
                if (exScore > v.ExScore) { v.ExScore = exScore; }
                if (missCount.HasValue && (!v.MissCount.HasValue || missCount < v.MissCount)) { v.MissCount = missCount; }
                current[(song, difficulty)] = v;
            }
        }

        /// <summary>Walk the map. read(address, length) reads game memory. Null when the map doesn't look right</summary>
        static Dictionary<(string, string), GameBest> Walk(Func<long, int, byte[]> read, long dataMap, ScoreMapLayout l, out string error)
        {
            error = null;
            long Ptr(long a) => BitConverter.ToInt64(read(a, 8), 0);
            long nullNode = Ptr(dataMap + l.NullNode);
            long start = Ptr(dataMap + l.BucketsStart), end = Ptr(dataMap + l.BucketsEnd);
            long size = end - start;
            if (size <= 0 || size > 64 * 1024 * 1024 || size % 8 != 0) { error = $"buckets 0x{start:X}..0x{end:X} don't look right"; return null; }
            var buckets = read(start, (int)size);
            var result = new Dictionary<(string, string), GameBest>();
            var seen = new HashSet<long>();
            for (int i = 0; i < buckets.Length / 8; i++)
            {
                long p = BitConverter.ToInt64(buckets, i * 8);
                while (p != 0 && p != nullNode && p != SkipBucket && seen.Add(p))
                {
                    if (seen.Count > MaxNodes) { error = "too many nodes"; return null; }
                    var n = read(p, l.NodeSize);
                    int diff = BitConverter.ToInt32(n, l.Difficulty), playType = BitConverter.ToInt32(n, l.PlayType);
                    if (diff >= 0 && diff <= 4 && playType >= 0 && playType <= 1)
                    {
                        var key = (BitConverter.ToInt32(n, l.Song).ToString("D5"), ((Difficulty)(diff + playType * 5)).ToString());
                        var lamp = (Lamp)BitConverter.ToInt32(n, l.Lamp);
                        int ex = BitConverter.ToInt32(n, l.Score);
                        uint miss = BitConverter.ToUInt32(n, l.MissCount);
                        result[key] = new GameBest
                        {
                            Clear = BitConverter.ToInt32(n, l.ClearCount),
                            Play = BitConverter.ToInt32(n, l.PlayCount),
                            Lamp = lamp,
                            ExScore = ex,
                            /* As /api/scores: no miss count for unplayed charts or when the game has none (uint max) */
                            MissCount = (lamp == Lamp.NP && ex == 0) || miss == uint.MaxValue ? (int?)null : (int)miss,
                        };
                    }
                    p = BitConverter.ToInt64(n, l.Next);
                }
            }
            return result;
        }

        /// <summary>Re-read the counts with the main loop's handle. Returns true when they changed. Never throws</summary>
        public static bool Refresh()
        {
            if (!Config.ApiServer_Enabled) { return false; }
            try
            {
                var counts = Walk((a, n) => Utils.ReadRaw(a, n), Offsets.DataMap, Layout.ScoreMap, out var error);
                if (counts == null)
                {
                    lock (sync) { available = false; }
                    Console.WriteLine($"[API] Play counts not available ({error}). Check GET /api/layout/preview?struct=scoreMap");
                    ApiState.SetDataWarning("playCounts", true);
                    return false;
                }
                ApiState.SetDataWarning("playCounts", false);
                /* The bests are replaced every time (Merge may have changed them), only the counts are published with /api/scores */
                var key = string.Join(";", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key.Item1}{kv.Key.Item2}{kv.Value.Clear}/{kv.Value.Play}"));
                lock (sync)
                {
                    current = counts;
                    available = counts.Count > 0; /* An empty map (game still loading) would make every chart look unplayed */
                }
                if (key == currentKey) { return false; }
                currentKey = key;
                return true;
            }
            catch (Exception e)
            {
                Utils.Except(e, "PlayCounts");
                return false;
            }
        }
    }
}
