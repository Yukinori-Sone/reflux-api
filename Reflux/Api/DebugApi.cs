using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Reflux.Api
{
    /// <summary>
    /// Maintenance endpoints for game updates: view / change offsets.txt and read raw game memory.
    /// Unlike the rest of the API these act on demand: offsets are written to offsets.txt and applied by
    /// re-attaching (the main loop reloads offsets.txt on every attach), memory is read with a separate
    /// read-only process handle owned by the request.
    /// </summary>
    static class DebugApi
    {
        /* Names as written by Offsets.SaveOffsets, in file order */
        static readonly string[] OffsetNames = { "songList", "unlockData", "playSettings", "playData", "currentSong", "judgeData", "dataMap" };
        static readonly object fileLock = new object();

        public const int MaxDumpLength = 16 * 1024 * 1024;
        public const long MaxSearchLength = 1024L * 1024 * 1024;
        const int PageSize = 0x1000;
        const int ChunkSize = 1024 * 1024;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll")]
        static extern bool ReadProcessMemory(IntPtr hProcess, long lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

        #region Offsets
        /// <summary>Parse offsets.txt: version line, then "name = 0xADDRESS"</summary>
        static (string version, Dictionary<string, long> values) ReadOffsetsFile()
        {
            var lines = File.ReadAllLines("offsets.txt");
            var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split('=');
                if (parts.Length != 2) { continue; }
                values[parts[0].Trim()] = Convert.ToInt64(parts[1].Trim(), 16);
            }
            return (lines.Length > 0 ? lines[0].Trim() : "", values);
        }

        static Dictionary<string, string> ToHex(Func<string, long?> value) =>
            OffsetNames.ToDictionary(n => n, n => value(n) is long v ? $"0x{v:X}" : null);

        /// <summary>Offsets currently used by the main loop (null before the first attach)</summary>
        static Dictionary<string, string> InUseOffsets()
        {
            if (Offsets.Version == null) { return null; }
            return ToHex(n => n switch
            {
                "songList" => Offsets.SongList,
                "unlockData" => Offsets.UnlockData,
                "playSettings" => Offsets.PlaySettings,
                "playData" => Offsets.PlayData,
                "currentSong" => Offsets.CurrentSong,
                "judgeData" => Offsets.JudgeData,
                "dataMap" => Offsets.DataMap,
                _ => (long?)null,
            });
        }

        public static string GetOffsetsJson()
        {
            var status = JObject.Parse(ApiState.StatusJson());
            var (fileVersion, fileValues) = ReadOffsetsFile();
            var module = GetModule(out _);
            return JsonConvert.SerializeObject(new
            {
                gameVersion = (string)status["gameVersion"],
                attachState = (string)status["attachState"],
                file = new { version = fileVersion, offsets = ToHex(n => fileValues.TryGetValue(n, out var v) ? v : (long?)null) },
                inUse = Offsets.Version == null ? null : new { version = Offsets.Version, offsets = InUseOffsets() },
                module,
            }, ApiState.JsonSettings);
        }

        /// <summary>
        /// Body: { "version": "P2D:J:B:A:...", "offsets": { "songList": "0x...", ... }, "reattach": true }
        /// Every field is optional: missing offsets keep the file values, version defaults to the detected game version.
        /// The previous file is copied to archive/ before it is replaced.
        /// </summary>
        public static (int code, string json) PutOffsets(string body)
        {
            JObject req;
            try { req = JObject.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body); }
            catch (JsonException e) { return (400, Error("bad_json", e.Message)); }

            var status = JObject.Parse(ApiState.StatusJson());
            lock (fileLock)
            {
                var (fileVersion, values) = ReadOffsetsFile();
                var newValues = OffsetNames.ToDictionary(n => n, n => values.TryGetValue(n, out var v) ? v : 0L);
                if (req["offsets"] is JObject offs)
                {
                    foreach (var p in offs.Properties())
                    {
                        var name = OffsetNames.FirstOrDefault(n => string.Equals(n, p.Name, StringComparison.OrdinalIgnoreCase));
                        if (name == null) { return (400, Error("unknown_offset", $"{p.Name} (known: {string.Join(", ", OffsetNames)})")); }
                        if (!TryParseNumber((string)p.Value, out var v) || v <= 0) { return (400, Error("bad_value", $"{p.Name}: {p.Value}")); }
                        newValues[name] = v;
                    }
                }
                var version = (string)req["version"] ?? (string)status["gameVersion"];
                if (string.IsNullOrWhiteSpace(version)) { version = fileVersion; }
                if (!version.StartsWith("P2D:")) { return (400, Error("bad_version", $"Version should look like P2D:J:B:A:yyyymmddnn, got {version}")); }

                Directory.CreateDirectory("archive");
                var archived = $"archive/{fileVersion.Replace(':', '_')}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                File.Copy("offsets.txt", archived, true);

                var sb = new StringBuilder();
                sb.AppendLine(version);
                foreach (var n in OffsetNames) { sb.AppendLine($"{n} = 0x{newValues[n]:X}"); }
                File.WriteAllText("offsets.txt.tmp", sb.ToString());
                File.Move("offsets.txt.tmp", "offsets.txt", true);

                bool reattach = (bool?)req["reattach"] ?? true;
                Console.WriteLine($"[API] offsets.txt updated by client (version {version}, previous file saved as {archived})");
                if (reattach) { ApiState.RequestRetry(); }
                return (200, JsonConvert.SerializeObject(new
                {
                    saved = true,
                    reattachRequested = reattach,
                    archived,
                    file = new { version, offsets = ToHex(n => newValues[n]) },
                }, ApiState.JsonSettings));
            }
        }

        /// <summary>
        /// Called by the main loop when offsets don't match the game. Instead of starting the interactive search right
        /// away, waits until either the console user presses Enter (→ true, run the interactive search) or a client
        /// re-attach is requested, e.g. by PUT /api/offsets, or the game exits (→ false, start over).
        /// Memory endpoints work while waiting, so the new addresses can be investigated remotely.
        /// </summary>
        public static bool WaitForOffsetsOrConsole(Process process, string gameVersion)
        {
            ApiState.SetAttachState(AttachState.OffsetMismatch,
                $"offsets.txt doesn't match game version {gameVersion}: set offsets with PUT /api/offsets, or press Enter on the Reflux console for the interactive search");
            Console.WriteLine("Waiting for new offsets: send them with PUT /api/offsets, or press ENTER here to start the interactive offset search.");
            while (true)
            {
                if (process.HasExited || ApiState.ConsumeRetry()) { return false; }
                try
                {
                    while (Console.KeyAvailable)
                    {
                        if (Console.ReadKey(true).Key == ConsoleKey.Enter) { return true; }
                    }
                }
                catch (InvalidOperationException) { /* Console input redirected, only the API can continue */ }
                Thread.Sleep(500);
            }
        }
        #endregion

        #region Memory
        /// <summary>Main module of the attached game process, null when not attached</summary>
        static object GetModule(out Process process)
        {
            process = null;
            var status = JObject.Parse(ApiState.StatusJson());
            var pid = (int?)status["processId"];
            if (pid == null || (string)status["attachState"] == "waitingForProcess") { return null; }
            try
            {
                process = Process.GetProcessById(pid.Value);
                if (process.HasExited || process.ProcessName != "bm2dx") { process = null; return null; }
                var m = process.MainModule;
                return new { processId = pid, baseAddress = $"0x{(long)m.BaseAddress:X}", size = $"0x{m.ModuleMemorySize:X}", fileName = m.FileName };
            }
            catch (Exception)
            {
                process = null;
                return null;
            }
        }

        public static (int code, string json) GetInfoJson()
        {
            var module = GetModule(out _);
            if (module == null) { return (409, Error("not_attached", "Not attached to INFINITAS")); }
            return (200, JsonConvert.SerializeObject(new { module, offsets = InUseOffsets() }, ApiState.JsonSettings));
        }

        /// <summary>Address expression: numbers (hex with or without 0x, or decimal with "d" suffix),
        /// "base" (module base) and offset names, joined with + and -. Example: songList+0x630</summary>
        static bool TryResolveAddress(string expr, long moduleBase, out long address, out string error)
        {
            address = 0;
            error = null;
            if (string.IsNullOrWhiteSpace(expr)) { error = "missing address"; return false; }
            var inUse = InUseOffsets();
            var tokens = System.Text.RegularExpressions.Regex.Matches(expr.Replace(" ", ""), @"([+-]?)([^+-]+)");
            foreach (System.Text.RegularExpressions.Match t in tokens)
            {
                var sign = t.Groups[1].Value == "-" ? -1 : 1;
                var term = t.Groups[2].Value;
                long value;
                if (term.Equals("base", StringComparison.OrdinalIgnoreCase))
                {
                    value = moduleBase;
                }
                else if (inUse != null && inUse.Keys.FirstOrDefault(k => k.Equals(term, StringComparison.OrdinalIgnoreCase)) is string key)
                {
                    value = Convert.ToInt64(inUse[key], 16);
                }
                else if (!TryParseNumber(term, out value))
                {
                    error = $"can't parse '{term}'";
                    return false;
                }
                address += sign * value;
            }
            return true;
        }

        static bool TryParseNumber(string s, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) { return false; }
            s = s.Trim();
            if (s.EndsWith("d", StringComparison.OrdinalIgnoreCase) && !s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return long.TryParse(s[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            }
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) { s = s[2..]; }
            return long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Read memory, unreadable pages are left as zero and counted</summary>
        static byte[] Read(IntPtr handle, long address, int length, out int unreadable)
        {
            var result = new byte[length];
            unreadable = 0;
            if (ReadProcessMemory(handle, address, result, length, out var n) && (long)n == length) { return result; }

            /* Partly unreadable: retry page by page */
            var page = new byte[PageSize];
            long pos = address;
            long end = address + length;
            while (pos < end)
            {
                long pageEnd = Math.Min(end, (pos / PageSize + 1) * PageSize);
                int size = (int)(pageEnd - pos);
                if (ReadProcessMemory(handle, pos, page, size, out var read) && (long)read == size)
                {
                    Buffer.BlockCopy(page, 0, result, (int)(pos - address), size);
                }
                else
                {
                    unreadable += size;
                }
                pos = pageEnd;
            }
            return result;
        }

        static bool TryOpen(out Process process, out long moduleBase, out IntPtr handle, out string error)
        {
            handle = IntPtr.Zero;
            moduleBase = 0;
            error = null;
            if (GetModule(out process) == null) { error = "Not attached to INFINITAS"; return false; }
            moduleBase = (long)process.MainModule.BaseAddress;
            handle = OpenProcess(0x0410, false, process.Id); /* PROCESS_QUERY_INFORMATION | PROCESS_VM_READ */
            if (handle == IntPtr.Zero) { error = $"OpenProcess failed ({Marshal.GetLastWin32Error()})"; return false; }
            return true;
        }

        /// <summary>GET /api/memory?address=songList+0x630&amp;length=0x100&amp;format=hex|bin</summary>
        public static void Dump(System.Net.HttpListenerContext ctx)
        {
            var q = ctx.Request.QueryString;
            if (!TryOpen(out _, out var moduleBase, out var handle, out var err)) { ApiServer.Write(ctx, 409, Error("not_attached", err)); return; }
            try
            {
                if (!TryResolveAddress(q["address"], moduleBase, out var address, out err)) { ApiServer.Write(ctx, 400, Error("bad_address", err)); return; }
                if (!TryParseNumber(q["length"] ?? "100", out var length) || length <= 0 || length > MaxDumpLength)
                {
                    ApiServer.Write(ctx, 400, Error("bad_length", $"length must be 1..0x{MaxDumpLength:X}")); return;
                }
                var data = Read(handle, address, (int)length, out var unreadable);
                var headers = new Dictionary<string, string>
                {
                    ["X-Address"] = $"0x{address:X}",
                    ["X-Unreadable-Bytes"] = unreadable.ToString(),
                };
                if ((q["format"] ?? "hex").Equals("bin", StringComparison.OrdinalIgnoreCase))
                {
                    ApiServer.WriteBytes(ctx, 200, "application/octet-stream", data, headers);
                }
                else
                {
                    ApiServer.WriteBytes(ctx, 200, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(HexDump(address, data, q["encoding"])), headers);
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>16 bytes per line: address, hex, text (ascii, or shift_jis when encoding=sjis)</summary>
        static string HexDump(long address, byte[] data, string encoding)
        {
            bool sjis = encoding != null && encoding.StartsWith("s", StringComparison.OrdinalIgnoreCase);
            var enc = sjis ? GetEncoding("shift_jis") : null;
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i += 16)
            {
                int n = Math.Min(16, data.Length - i);
                sb.Append($"{address + i:X12}  ");
                for (int j = 0; j < 16; j++)
                {
                    sb.Append(j < n ? $"{data[i + j]:X2} " : "   ");
                    if (j == 7) { sb.Append(' '); }
                }
                sb.Append(' ');
                if (sjis)
                {
                    /* Multi-byte characters may straddle lines, good enough for spotting strings */
                    sb.Append(new string(enc.GetString(data, i, n).Select(c => char.IsControl(c) ? '.' : c).ToArray()));
                }
                else
                {
                    for (int j = 0; j < n; j++) { var b = data[i + j]; sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.'); }
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        static Encoding GetEncoding(string name)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(name);
        }

        /// <summary>
        /// GET /api/memory/search?start=base&amp;length=0x5000000&amp;hex=06620000 | text=5.1.1.&amp;encoding=sjis|utf16|ascii | int32=25094
        /// Returns up to max (default 100, at most 1000) matching addresses.
        /// </summary>
        public static void Search(System.Net.HttpListenerContext ctx)
        {
            var q = ctx.Request.QueryString;
            if (!TryOpen(out _, out var moduleBase, out var handle, out var err)) { ApiServer.Write(ctx, 409, Error("not_attached", err)); return; }
            try
            {
                if (!TryResolveAddress(q["start"] ?? "base", moduleBase, out var start, out err)) { ApiServer.Write(ctx, 400, Error("bad_start", err)); return; }
                if (!TryParseNumber(q["length"] ?? "0x5000000", out var length) || length <= 0 || length > MaxSearchLength)
                {
                    ApiServer.Write(ctx, 400, Error("bad_length", $"length must be 1..0x{MaxSearchLength:X}")); return;
                }
                if (!TryBuildPattern(q, out var pattern, out err)) { ApiServer.Write(ctx, 400, Error("bad_pattern", err)); return; }
                int max = int.TryParse(q["max"], out var m) ? Math.Clamp(m, 1, 1000) : 100;

                var sw = Stopwatch.StartNew();
                var hits = new List<string>();
                long unreadable = 0;
                for (long pos = start; pos < start + length && hits.Count < max; pos += ChunkSize)
                {
                    /* Overlap by pattern length - 1 so matches across chunk borders are found */
                    int size = (int)Math.Min(ChunkSize + pattern.Length - 1, start + length - pos);
                    if (size < pattern.Length) { break; }
                    var data = Read(handle, pos, size, out var bad);
                    unreadable += Math.Min(bad, ChunkSize);
                    for (int i = 0; i <= size - pattern.Length && i < ChunkSize && hits.Count < max; i++)
                    {
                        if (data[i] != pattern[0]) { continue; }
                        int k = 1;
                        while (k < pattern.Length && data[i + k] == pattern[k]) { k++; }
                        if (k == pattern.Length) { hits.Add($"0x{pos + i:X}"); }
                    }
                }
                ApiServer.Write(ctx, 200, JsonConvert.SerializeObject(new
                {
                    start = $"0x{start:X}",
                    length = $"0x{length:X}",
                    pattern = BitConverter.ToString(pattern).Replace("-", " "),
                    truncated = hits.Count >= max,
                    unreadableBytes = unreadable,
                    elapsedMs = sw.ElapsedMilliseconds,
                    matches = hits,
                }, ApiState.JsonSettings));
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        static bool TryBuildPattern(System.Collections.Specialized.NameValueCollection q, out byte[] pattern, out string error)
        {
            pattern = null;
            error = null;
            try
            {
                if (q["hex"] != null)
                {
                    var hex = new string(q["hex"].Where(Uri.IsHexDigit).ToArray());
                    if (hex.Length == 0 || hex.Length % 2 != 0) { error = "hex needs an even number of hex digits"; return false; }
                    pattern = Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
                }
                else if (q["text"] != null)
                {
                    var enc = (q["encoding"] ?? "sjis").ToLowerInvariant() switch
                    {
                        "utf16" => Encoding.Unicode,
                        "utf-16" => Encoding.Unicode,
                        "unicode" => Encoding.Unicode,
                        "utf8" => Encoding.UTF8,
                        "utf-8" => Encoding.UTF8,
                        "ascii" => Encoding.ASCII,
                        _ => GetEncoding("shift_jis"),
                    };
                    pattern = enc.GetBytes(q["text"]);
                }
                else if (q["int32"] != null)
                {
                    pattern = BitConverter.GetBytes(int.Parse(q["int32"], CultureInfo.InvariantCulture));
                }
                else if (q["int64"] != null)
                {
                    pattern = BitConverter.GetBytes(long.Parse(q["int64"], CultureInfo.InvariantCulture));
                }
                else
                {
                    error = "give one of hex, text, int32, int64";
                    return false;
                }
            }
            catch (Exception e) when (e is FormatException || e is OverflowException || e is ArgumentException)
            {
                error = e.Message;
                return false;
            }
            if (pattern.Length == 0) { error = "empty pattern"; return false; }
            return true;
        }
        #endregion

        static string Error(string code, string message) =>
            JsonConvert.SerializeObject(new { error = code, message }, ApiState.JsonSettings);
    }
}
