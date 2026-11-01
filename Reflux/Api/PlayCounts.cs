using System;
using System.Collections.Generic;
using System.Linq;

namespace Reflux.Api
{
    /// <summary>
    /// Clear and play counts of every chart, kept by the game in the personal best hash map (offsets.txt dataMap, nodes at
    /// scoreMap.clearCount / playCount). The game updates them after each play, so they are re-read when the game returns
    /// to song select (from ApiState.SetGameState, main loop thread) and published with /api/scores.
    /// </summary>
    static class PlayCounts
    {
        const int MaxNodes = 200000;
        const long SkipBucket = 0x494fdce0; /* Skipped by ScoreMap.LoadMap as well */
        static Dictionary<(string song, string difficulty), (int clear, int play)> current =
            new Dictionary<(string, string), (int, int)>();
        static string currentKey = "";

        public static (int clear, int play)? Get(string song, string difficulty) =>
            current.TryGetValue((song, difficulty), out var v) ? v : ((int, int)?)null;

        /// <summary>Walk the map. read(address, length) reads game memory. Null when the map doesn't look right</summary>
        public static Dictionary<(string, string), (int, int)> Walk(Func<long, int, byte[]> read, long dataMap, ScoreMapLayout l, out string error)
        {
            error = null;
            long Ptr(long a) => BitConverter.ToInt64(read(a, 8), 0);
            long nullNode = Ptr(dataMap + l.NullNode);
            long start = Ptr(dataMap + l.BucketsStart), end = Ptr(dataMap + l.BucketsEnd);
            long size = end - start;
            if (size <= 0 || size > 64 * 1024 * 1024 || size % 8 != 0) { error = $"buckets 0x{start:X}..0x{end:X} don't look right"; return null; }
            var buckets = read(start, (int)size);
            var result = new Dictionary<(string, string), (int, int)>();
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
                        result[key] = (BitConverter.ToInt32(n, l.ClearCount), BitConverter.ToInt32(n, l.PlayCount));
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
                    Console.WriteLine($"[API] Play counts not available ({error}). Check GET /api/layout/preview?struct=scoreMap");
                    ApiState.SetDataWarning("playCounts", true);
                    return false;
                }
                ApiState.SetDataWarning("playCounts", false);
                var key = string.Join(";", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key.Item1}{kv.Key.Item2}{kv.Value.Item1}/{kv.Value.Item2}"));
                if (key == currentKey) { return false; }
                current = counts;
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
