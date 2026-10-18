using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Reflux.Api
{
    class RivalDto
    {
        /// <summary>SP / DP (rivals are registered separately per play side)</summary>
        public string Side { get; set; }
        /// <summary>0-based registration order within the side</summary>
        public int Index { get; set; }
        public string InfinitasId { get; set; }
        public string DjName { get; set; }
        /// <summary>Probably the prefecture id (1..47 = JIS code). Not verified</summary>
        public int PrefId { get; set; }
        /// <summary>Probably SP / DP dan grade ids, -1 = none. Not verified</summary>
        public int GradeSp { get; set; }
        public int GradeDp { get; set; }
        public int Charts { get; set; }
    }

    class RivalScoreDto
    {
        public string Side { get; set; }
        public int Index { get; set; }
        public string SongId { get; set; }
        /// <summary>SPB .. DPL</summary>
        public string Difficulty { get; set; }
        public int ExScore { get; set; }
        /// <summary>NP F AC EC NC HC EX FC (the game stores no PERFECT)</summary>
        public string Lamp { get; set; }
    }

    /// <summary>
    /// Rival scores as received by the game (GetRivalInfo). bm2dx base + rival.spData / dpData points to a vector
    /// { begin, end } holding the decoded KBin response; inside it the rival_data binary is big endian:
    ///   [blob length][rival count] then per rival
    ///   [len]["C" + 12 digits][len][DJ NAME][len][shop name][pref][grade sp][grade dp]   (strings NUL terminated, padded to 4)
    ///   records: [song id][0x14][EX score x5 (B N H A L)][0x14][lamp x5]   (52 bytes each, as long as the 0x14 markers match)
    ///   then other data (recent plays, comments) up to the next rival's id
    /// Read once when Reflux becomes ready (the game only fetches rivals at startup).
    /// </summary>
    static class Rivals
    {
        const int MaxBuffer = 16 * 1024 * 1024;
        static readonly Regex IdPattern = new Regex(@"\x00\x00\x00\x0EC\d{12}\x00", RegexOptions.Compiled);
        static readonly string[] Charts = { "B", "N", "H", "A", "L" };
        static readonly string[] Lamps = { "NP", "F", "AC", "EC", "NC", "HC", "EX", "FC", "PFC" };

        static int Be32(byte[] d, int p) => (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];

        /// <summary>Read the response buffer of one side. read(address, length) reads game memory</summary>
        public static byte[] ReadBuffer(Func<long, int, byte[]> read, long pointerAddress, RivalLayout l, out string error)
        {
            error = null;
            long Ptr(long a) => BitConverter.ToInt64(read(a, 8), 0);
            long vec = Ptr(pointerAddress);
            if (vec == 0) { error = "pointer is null"; return null; }
            long begin = Ptr(vec + l.Begin), end = Ptr(vec + l.End);
            long size = end - begin;
            if (begin == 0 || size <= 0 || size > MaxBuffer) { error = $"vector 0x{vec:X} doesn't look right (begin 0x{begin:X}, end 0x{end:X})"; return null; }
            return read(begin, (int)size);
        }

        /// <summary>Parse the rival_data binary out of the buffer. Returns null when it can't be found</summary>
        public static (List<RivalDto> rivals, List<RivalScoreDto> scores) Parse(byte[] buf, string side, out string error)
        {
            error = null;
            /* Latin1 maps every byte to one char, so regex positions are byte offsets */
            var text = Encoding.GetEncoding("ISO-8859-1").GetString(buf);
            var ids = IdPattern.Matches(text).Cast<Match>().Select(m => m.Index).ToList();
            var rivals = new List<RivalDto>();
            var scores = new List<RivalScoreDto>();
            if (ids.Count == 0) { error = "no rival id found"; return (rivals, scores); } /* No rivals registered is fine */

            /* The first id whose preceding [blob length][count] makes sense starts the rival data */
            int first = -1, count = 0, blobEnd = 0;
            foreach (var id in ids)
            {
                int start = id - 8;
                if (start < 0) { continue; }
                int len = Be32(buf, start), n = Be32(buf, start + 4);
                if (n >= 1 && n <= 10 && len > 0 && start + 4 + len <= buf.Length)
                {
                    first = id;
                    count = n;
                    blobEnd = start + 4 + len;
                    break;
                }
            }
            if (first < 0) { error = "rival data header ([length][count] before the first id) not found"; return (null, null); }

            string Str(ref int p)
            {
                int n = Be32(buf, p);
                if (n < 0 || n > 1024 || p + 4 + n > blobEnd) { throw new FormatException($"bad string length {n} at 0x{p:X}"); }
                var s = Encoding.UTF8.GetString(buf, p + 4, n).Split('\0')[0];
                p += 4 + ((n + 3) & ~3);
                return s;
            }

            try
            {
                int pos = first;
                for (int i = 0; i < count; i++)
                {
                    var r = new RivalDto { Side = side, Index = i };
                    r.InfinitasId = Str(ref pos);
                    r.DjName = Str(ref pos);
                    Str(ref pos); /* shop name */
                    r.PrefId = Be32(buf, pos);
                    r.GradeSp = Be32(buf, pos + 4);
                    r.GradeDp = Be32(buf, pos + 8);
                    pos += 12;
                    while (pos + 52 <= blobEnd && Be32(buf, pos + 4) == 20 && Be32(buf, pos + 28) == 20)
                    {
                        int song = Be32(buf, pos);
                        for (int d = 0; d < 5; d++)
                        {
                            int ex = Be32(buf, pos + 8 + d * 4);
                            int lamp = Be32(buf, pos + 32 + d * 4);
                            if (ex == 0 && lamp == 0) { continue; }
                            scores.Add(new RivalScoreDto
                            {
                                Side = side,
                                Index = i,
                                SongId = song.ToString("D5"),
                                Difficulty = side + Charts[d],
                                ExScore = ex,
                                Lamp = lamp >= 0 && lamp < Lamps.Length ? Lamps[lamp] : "NP",
                            });
                            r.Charts++;
                        }
                        pos += 52;
                    }
                    rivals.Add(r);
                    if (i + 1 < count)
                    {
                        var next = ids.FirstOrDefault(x => x >= pos);
                        if (next == 0 || next >= blobEnd) { throw new FormatException($"rival {i + 1} not found"); }
                        pos = next;
                    }
                }
            }
            catch (FormatException e)
            {
                error = e.Message;
                return (null, null);
            }
            return (rivals, scores);
        }

        /// <summary>Read and publish both sides with the main loop's handle. Never throws</summary>
        public static void Refresh()
        {
            if (!Config.ApiServer_Enabled) { return; }
            try
            {
                var pid = ApiState.ProcessId;
                if (pid == null) { return; }
                long moduleBase = (long)Process.GetProcessById(pid.Value).MainModule.BaseAddress;
                var l = Layout.Rival;
                byte[] Read(long a, int n) => Utils.ReadRaw(a, n);
                var rivals = new List<RivalDto>();
                var scores = new List<RivalScoreDto>();
                bool complete = true;
                foreach (var (side, offset) in new[] { ("SP", l.SpData), ("DP", l.DpData) })
                {
                    var buf = ReadBuffer(Read, moduleBase + offset, l, out var error);
                    var parsed = buf == null ? (null, null) : Parse(buf, side, out error);
                    if (parsed.rivals == null)
                    {
                        Console.WriteLine($"[API] {side} rivals not available ({error}). Check GET /api/layout/preview?struct=rival");
                        complete = false;
                        continue;
                    }
                    rivals.AddRange(parsed.rivals);
                    scores.AddRange(parsed.scores);
                }
                ApiState.PublishRivals(rivals, scores, complete);
            }
            catch (Exception e)
            {
                Utils.Except(e, "Rivals");
            }
        }
    }
}
