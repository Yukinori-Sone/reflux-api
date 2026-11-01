using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Reflux.Api
{
    /// <summary>Clear rate of one chart among every player (server statistics shown on the song select screen)</summary>
    class ClearRateDto
    {
        public string SongId { get; set; }
        /// <summary>SPB .. DPL</summary>
        public string Difficulty { get; set; }
        /// <summary>Percent with 2 decimals</summary>
        public double Rate { get; set; }
    }

    /// <summary>
    /// Reads the clear rate trees (see <see cref="ClearRateLayout"/>). Called from the main loop thread through ApiState
    /// when Reflux becomes ready and when the game returns to song select, so API requests still never read game memory.
    /// </summary>
    static class ClearRates
    {
        const int MaxNodes = 200000;
        static readonly string[] ChartTypes = { "B", "N", "H", "A", "L" };
        static int basePid = -1;
        static long moduleBase;
        static bool warned;

        /// <summary>Walk one tree. read(address, length) reads game memory. Returns null when the tree doesn't look right</summary>
        public static List<(int song, int diff, int rate)> Walk(Func<long, int, byte[]> read, long pointerAddress, ClearRateLayout l, out string error)
        {
            error = null;
            long Ptr(long a) => BitConverter.ToInt64(read(a, 8), 0);
            long obj = Ptr(pointerAddress);
            if (obj == 0) { error = "pointer is null"; return null; }
            long head = Ptr(obj + l.ObjectHead);
            long size = Ptr(obj + l.ObjectSize);
            if (head == 0 || size < 0 || size > MaxNodes) { error = $"object 0x{obj:X} doesn't look like a map (head 0x{head:X}, size {size})"; return null; }

            var result = new List<(int, int, int)>();
            var seen = new HashSet<long>();
            var stack = new Stack<long>();
            stack.Push(Ptr(head + l.Parent)); /* The head's parent is the root */
            while (stack.Count > 0)
            {
                long n = stack.Pop();
                if (n == 0 || n == head || !seen.Add(n)) { continue; }
                if (seen.Count > size + 1) { error = $"more nodes than the size {size}"; return null; }
                var node = read(n, l.NodeSize);
                if (node[l.IsNil] != 0) { continue; }
                result.Add((BitConverter.ToInt32(node, l.Song), BitConverter.ToInt32(node, l.Difficulty), BitConverter.ToInt32(node, l.Rate)));
                stack.Push(BitConverter.ToInt64(node, l.Left));
                stack.Push(BitConverter.ToInt64(node, l.Right));
            }
            if (result.Count != size) { error = $"walked {result.Count} nodes, size says {size}"; return null; }
            if (result.Any(r => r.Item2 < 0 || r.Item2 > 4 || r.Item3 < -1 || r.Item3 > 10000)) { error = "difficulty or rate out of range"; return null; }
            return result;
        }

        public static IEnumerable<ClearRateDto> ToDtos(IEnumerable<(int song, int diff, int rate)> nodes, string side) =>
            nodes.Where(n => n.rate >= 0).Select(n => new ClearRateDto
            {
                SongId = n.song.ToString("D5"),
                Difficulty = side + ChartTypes[n.diff],
                Rate = n.rate / 100.0,
            });

        /// <summary>Read both trees with the main loop's handle and publish them. Never throws</summary>
        public static void Refresh()
        {
            if (!Config.ApiServer_Enabled) { return; }
            try
            {
                var pid = ApiState.ProcessId;
                if (pid == null) { return; }
                if (pid != basePid)
                {
                    moduleBase = (long)Process.GetProcessById(pid.Value).MainModule.BaseAddress;
                    basePid = pid.Value;
                }
                var l = Layout.ClearRate;
                byte[] Read(long a, int n) => Utils.ReadRaw(a, n);
                var sp = Walk(Read, moduleBase + l.SpMap, l, out var spError);
                var dp = Walk(Read, moduleBase + l.DpMap, l, out var dpError);
                if (sp == null || dp == null)
                {
                    if (!warned)
                    {
                        Console.WriteLine($"[API] Clear rates not available (SP: {spError ?? "ok"}, DP: {dpError ?? "ok"}). Check GET /api/layout/preview?struct=clearRate");
                        warned = true;
                    }
                    ApiState.SetDataWarning("clearRates", true);
                    return;
                }
                warned = false;
                ApiState.SetDataWarning("clearRates", false);
                ApiState.PublishClearRates(ToDtos(sp, "SP").Concat(ToDtos(dp, "DP")).OrderBy(d => d.SongId).ThenBy(d => d.Difficulty).ToList());
            }
            catch (Exception e)
            {
                Utils.Except(e, "ClearRates");
            }
        }
    }
}
