using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Reflux.Api
{
    /// <summary>A fixed size string field inside a record</summary>
    public class StringField
    {
        public int Offset;
        public int Length;
        /// <summary>utf16, sjis or utf8. The string ends at the first NUL character</summary>
        public string Encoding;

        public StringField() { }
        public StringField(int offset, int length, string encoding) { Offset = offset; Length = length; Encoding = encoding; }
    }

    /// <summary>
    /// Layout of one song list entry. Offsets are relative to the start of the entry.
    /// Defaults are for INFINITAS P2D:J:B:A:2026101400.
    /// </summary>
    public class SongLayout
    {
        /// <summary>Size of one entry, the next song starts right after</summary>
        public int EntrySize = 0x730;
        public StringField Title = new StringField(0x000, 0x100, "utf16");
        /// <summary>Romanized / English title</summary>
        public StringField Title2 = new StringField(0x100, 0x40, "sjis");
        public StringField Genre = new StringField(0x140, 0x80, "utf16");
        public StringField Artist = new StringField(0x1C0, 0x80, "utf16");
        /// <summary>Version folder number, 1 byte</summary>
        public int Folder = 0x2D8;
        /// <summary>Levels, 10 bytes: SPB SPN SPH SPA SPL DPB DPN DPH DPA DPL</summary>
        public int Levels = 0x2E0;
        /// <summary>BPM, 10 pairs of int32 (max, min), same chart order as levels</summary>
        public int Bpm = 0x2F0;
        /// <summary>Note counts, 10 int32, same chart order as levels</summary>
        public int Notes = 0x370;
        /// <summary>Song ID, int32</summary>
        public int Id = 0x5B0;
    }

    public class StructLayouts
    {
        public SongLayout Song = new SongLayout();
    }

    /// <summary>
    /// Struct layouts read from layout.json (missing file or fields = built-in defaults), so a game update that only
    /// moves fields can be handled without a rebuild: try a layout with POST /api/layout/preview, save it with PUT /api/layout.
    /// The file is reloaded when it changes, the song list is read with the new layout on the next attach.
    /// </summary>
    static class Layout
    {
        const string FileName = "layout.json";
        static readonly object fileLock = new object();
        static StructLayouts current;
        static DateTime loadedStamp = DateTime.MinValue;

        public static StructLayouts Current
        {
            get
            {
                lock (fileLock)
                {
                    var stamp = File.Exists(FileName) ? File.GetLastWriteTimeUtc(FileName) : DateTime.MinValue;
                    if (current == null || stamp != loadedStamp)
                    {
                        current = new StructLayouts();
                        if (stamp != DateTime.MinValue)
                        {
                            try
                            {
                                var err = Merge(current, JObject.Parse(File.ReadAllText(FileName)));
                                if (err != null) { throw new FormatException(err); }
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"[API] {FileName} is invalid, using the built-in layout: {e.Message}");
                                current = new StructLayouts();
                            }
                        }
                        loadedStamp = stamp;
                    }
                    return current;
                }
            }
        }

        public static SongLayout Song => Current.Song;

        #region Parsing
        static Encoding GetEncoding(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "utf16":
                case "utf-16":
                case "unicode":
                    return System.Text.Encoding.Unicode;
                case "utf8":
                case "utf-8":
                    return System.Text.Encoding.UTF8;
                case "sjis":
                case "shift_jis":
                case "shift-jis":
                    System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    return System.Text.Encoding.GetEncoding("shift_jis");
                default:
                    return null;
            }
        }

        static string ReadString(byte[] e, StringField f)
        {
            var enc = GetEncoding(f.Encoding);
            int len = f.Length;
            if (enc == System.Text.Encoding.Unicode)
            {
                /* Find the 2-byte NUL on an even position */
                int end = 0;
                while (end + 1 < len && (e[f.Offset + end] != 0 || e[f.Offset + end + 1] != 0)) { end += 2; }
                len = end;
            }
            else
            {
                int end = Array.IndexOf(e, (byte)0, f.Offset, f.Length);
                if (end >= 0) { len = end - f.Offset; }
            }
            return enc.GetString(e, f.Offset, len).TrimEnd();
        }

        /// <summary>
        /// Parse one song list entry. Returns a SongInfo with title null at the end of the list (entry starts with 4 zero bytes).
        /// </summary>
        public static SongInfo ParseSong(byte[] e, SongLayout l)
        {
            if (BitConverter.ToInt32(e, 0) == 0) { return new SongInfo(); }
            var bpms = new string[10];
            for (int i = 0; i < 10; i++)
            {
                var max = BitConverter.ToInt32(e, l.Bpm + i * 8);
                var min = BitConverter.ToInt32(e, l.Bpm + i * 8 + 4);
                bpms[i] = min != 0 ? $"{min:000}~{max:000}" : $"{max:000}";
            }
            return new SongInfo
            {
                ID = BitConverter.ToInt32(e, l.Id).ToString("D5"),
                title = ReadString(e, l.Title),
                title_english = ReadString(e, l.Title2),
                genre = ReadString(e, l.Genre),
                artist = ReadString(e, l.Artist),
                bpms = bpms,
                totalNotes = Enumerable.Range(0, 10).Select(i => BitConverter.ToInt32(e, l.Notes + i * 4)).ToArray(),
                level = Enumerable.Range(0, 10).Select(i => (int)e[l.Levels + i]).ToArray(),
                folder = e[l.Folder],
            };
        }
        #endregion

        /// <summary>Sanity counts over a parsed song list. Song IDs are sorted in the game's list, so every count is 0 for a right layout</summary>
        public class SongListCheck
        {
            public int Entries;
            public int UniqueIds;
            public int IdsNotIncreasing;
            public int IdsOutOfRange;
            public int NoNotes;
            public int BadNotes;
            public int BadLevels;
            public int EmptyTitle;
            public bool LooksValid => Entries >= 100 && IdsNotIncreasing == 0 && IdsOutOfRange == 0 && BadNotes == 0 && BadLevels == 0 && EmptyTitle == 0;
            public override string ToString() =>
                $"{Entries} songs, ids not increasing {IdsNotIncreasing}, ids out of range {IdsOutOfRange}, bad notes {BadNotes}, bad levels {BadLevels}, empty titles {EmptyTitle}";
        }

        public static SongListCheck CheckSongs(IList<SongInfo> songs)
        {
            var ids = songs.Select(s => int.TryParse(s.ID, out var i) ? i : -1).ToList();
            return new SongListCheck
            {
                Entries = songs.Count,
                UniqueIds = ids.Distinct().Count(),
                IdsNotIncreasing = Enumerable.Range(1, Math.Max(0, ids.Count - 1)).Count(i => ids[i] <= ids[i - 1]),
                IdsOutOfRange = ids.Count(i => i < 1000 || i > 99999),
                NoNotes = songs.Count(s => s.totalNotes.All(n => n == 0)),
                BadNotes = songs.Count(s => s.totalNotes.Any(n => n < 0 || n > 10000)),
                BadLevels = songs.Count(s => s.level.Any(v => v > 12)),
                EmptyTitle = songs.Count(s => string.IsNullOrEmpty(s.title)),
            };
        }

        #region JSON
        static string CamelCase(string s) => char.ToLowerInvariant(s[0]) + s.Substring(1);

        static JObject ToJson(object layout)
        {
            var o = new JObject();
            foreach (var f in layout.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var v = f.GetValue(layout);
                o[CamelCase(f.Name)] = v switch
                {
                    int i => (JToken)$"0x{i:X}",
                    StringField s => new JObject { ["offset"] = $"0x{s.Offset:X}", ["length"] = $"0x{s.Length:X}", ["encoding"] = s.Encoding },
                    _ => ToJson(v),
                };
            }
            return o;
        }

        static bool TryParseInt(JToken t, out int value)
        {
            value = 0;
            if (t == null) { return false; }
            if (t.Type == JTokenType.Integer) { value = (int)t; return true; }
            var s = ((string)t ?? "").Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            }
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Overwrite the fields given in json (numbers as 0x-hex strings or decimal), returns an error message or null</summary>
        static string Merge(object target, JObject json, string path = "")
        {
            var fields = target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var p in json.Properties())
            {
                var f = fields.FirstOrDefault(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                var name = path + p.Name;
                if (f == null) { return $"unknown field {name} (known: {string.Join(", ", fields.Select(x => CamelCase(x.Name)))})"; }
                if (f.FieldType == typeof(int))
                {
                    if (!TryParseInt(p.Value, out var v)) { return $"{name}: not a number ({p.Value})"; }
                    f.SetValue(target, v);
                }
                else if (f.FieldType == typeof(StringField))
                {
                    if (!(p.Value is JObject so)) { return $"{name}: expected {{ offset, length, encoding }}"; }
                    var s = (StringField)f.GetValue(target);
                    var n = new StringField(s.Offset, s.Length, s.Encoding);
                    if (so["offset"] != null && !TryParseInt(so["offset"], out n.Offset)) { return $"{name}.offset: not a number"; }
                    if (so["length"] != null && !TryParseInt(so["length"], out n.Length)) { return $"{name}.length: not a number"; }
                    if (so["encoding"] != null) { n.Encoding = (string)so["encoding"]; }
                    f.SetValue(target, n);
                }
                else
                {
                    if (!(p.Value is JObject child)) { return $"{name}: expected an object"; }
                    var err = Merge(f.GetValue(target), child, name + ".");
                    if (err != null) { return err; }
                }
            }
            return Validate(target);
        }

        static string Validate(object target)
        {
            if (!(target is SongLayout l)) { return null; }
            if (l.EntrySize < 0x40 || l.EntrySize > 0x10000) { return "song.entrySize must be 0x40..0x10000"; }
            var checks = new (string name, int offset, int size)[]
            {
                ("title", l.Title.Offset, l.Title.Length), ("title2", l.Title2.Offset, l.Title2.Length),
                ("genre", l.Genre.Offset, l.Genre.Length), ("artist", l.Artist.Offset, l.Artist.Length),
                ("folder", l.Folder, 1), ("levels", l.Levels, 10), ("bpm", l.Bpm, 80), ("notes", l.Notes, 40), ("id", l.Id, 4),
            };
            foreach (var (name, offset, size) in checks)
            {
                if (offset < 0 || size <= 0 || offset + size > l.EntrySize) { return $"song.{name} (0x{offset:X}, 0x{size:X} bytes) is outside the entry (0x{l.EntrySize:X})"; }
            }
            foreach (var (name, f) in new[] { ("title", l.Title), ("title2", l.Title2), ("genre", l.Genre), ("artist", l.Artist) })
            {
                if (GetEncoding(f.Encoding) == null) { return $"song.{name}.encoding must be utf16, sjis or utf8 (got {f.Encoding})"; }
            }
            return null;
        }

        static StructLayouts Copy(StructLayouts l)
        {
            var c = new StructLayouts();
            Merge(c, ToJson(l));
            return c;
        }
        #endregion

        #region Endpoints
        /// <summary>GET /api/layout: layout in use, whether layout.json exists, and the built-in defaults</summary>
        public static string GetJson()
        {
            return JsonConvert.SerializeObject(new
            {
                file = File.Exists(FileName) ? FileName : null,
                layout = ToJson(Current),
                defaults = ToJson(new StructLayouts()),
            }, ApiState.JsonSettings);
        }

        /// <summary>
        /// PUT /api/layout. Body: { "song": { "entrySize": "0x730", "title": { "offset": "0x0" }, ... }, "reset": false, "reattach": true }
        /// Fields not given keep their current values, reset starts from the built-in defaults.
        /// The previous layout.json is copied to archive/ before it is replaced.
        /// </summary>
        public static (int code, string json) Put(string body)
        {
            JObject req;
            try { req = JObject.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body); }
            catch (JsonException e) { return (400, Error("bad_json", e.Message)); }

            lock (fileLock)
            {
                var layout = ((bool?)req["reset"] ?? false) ? new StructLayouts() : Copy(Current);
                var err = Merge(layout, new JObject(req.Properties().Where(p => p.Name != "reset" && p.Name != "reattach")));
                if (err != null) { return (400, Error("bad_layout", err)); }

                string archived = null;
                if (File.Exists(FileName))
                {
                    Directory.CreateDirectory("archive");
                    archived = $"archive/layout_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                    File.Copy(FileName, archived, true);
                }
                File.WriteAllText(FileName + ".tmp", ToJson(layout).ToString(Formatting.Indented));
                File.Move(FileName + ".tmp", FileName, true);

                bool reattach = (bool?)req["reattach"] ?? true;
                Console.WriteLine($"[API] {FileName} updated by client" + (archived != null ? $" (previous file saved as {archived})" : ""));
                if (reattach) { ApiState.RequestRetry(); }
                return (200, JsonConvert.SerializeObject(new { saved = true, reattachRequested = reattach, archived, layout = ToJson(layout) }, ApiState.JsonSettings));
            }
        }

        /// <summary>
        /// GET|POST /api/layout/preview?start=songList&amp;count=5. Reads the song list from game memory with the layout in use,
        /// or with the current layout overridden by the POST body (same format as PUT, nothing is saved), and returns the first
        /// count entries, the last 3 and a summary of the whole list for judging whether the layout is right.
        /// </summary>
        public static void Preview(System.Net.HttpListenerContext ctx, string body)
        {
            var q = ctx.Request.QueryString;
            var layout = Copy(Current);
            if (!string.IsNullOrWhiteSpace(body))
            {
                JObject req;
                try { req = JObject.Parse(body); }
                catch (JsonException e) { ApiServer.Write(ctx, 400, Error("bad_json", e.Message)); return; }
                var err = Merge(layout, req);
                if (err != null) { ApiServer.Write(ctx, 400, Error("bad_layout", err)); return; }
            }
            int count = int.TryParse(q["count"], out var c) ? Math.Clamp(c, 0, 500) : 5;
            var l = layout.Song;

            var scan = DebugApi.ReadRecords(q["start"] ?? "songList", l.EntrySize, 10000, e => BitConverter.ToInt32(e, 0) == 0);
            if (scan.error != null) { ApiServer.Write(ctx, scan.code, Error(scan.errorCode, scan.error)); return; }

            var songs = scan.records.Select(e => ParseSong(e, l)).ToList();
            var check = CheckSongs(songs);
            object Entry(int i) => new
            {
                index = i,
                address = $"0x{scan.start + (long)i * l.EntrySize:X}",
                id = songs[i].ID,
                title = songs[i].title,
                title2 = songs[i].title_english,
                genre = songs[i].genre,
                artist = songs[i].artist,
                folder = songs[i].folder,
                levels = songs[i].level,
                notes = songs[i].totalNotes,
                bpm = songs[i].bpms,
            };
            ApiServer.Write(ctx, 200, JsonConvert.SerializeObject(new
            {
                start = $"0x{scan.start:X}",
                layout = ToJson(layout.Song),
                summary = new
                {
                    entries = check.Entries,
                    endFound = scan.endFound,
                    uniqueIds = check.UniqueIds,
                    idsNotIncreasing = check.IdsNotIncreasing,
                    idsOutOfRange = check.IdsOutOfRange,
                    noNotes = check.NoNotes,
                    badNotes = check.BadNotes,
                    badLevels = check.BadLevels,
                    emptyTitle = check.EmptyTitle,
                    /* The same check the main loop uses before accepting the song list */
                    looksValid = scan.endFound && check.LooksValid,
                },
                first = Enumerable.Range(0, Math.Min(count, songs.Count)).Select(Entry),
                last = Enumerable.Range(Math.Max(0, songs.Count - 3), Math.Min(3, songs.Count)).Select(Entry),
            }, ApiState.JsonSettings));
        }
        #endregion

        static string Error(string code, string message) =>
            JsonConvert.SerializeObject(new { error = code, message }, ApiState.JsonSettings);
    }
}
