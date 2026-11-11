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
        /// <summary>Up to 128 characters, runs until the unused bytes before the folder number</summary>
        public StringField Artist = new StringField(0x1C0, 0x100, "utf16");
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

    /// <summary>One entry of the unlock data array (offsets.txt unlockData), one per song in song list order</summary>
    public class UnlockLayout
    {
        public int EntrySize = 0x20;
        /// <summary>Song ID, int32. An entry with ID 0 ends the array</summary>
        public int Id = 0x0;
        /// <summary>Unlock type (1 Base, 2 Bits, 3 Sub), int32</summary>
        public int Type = 0x4;
        /// <summary>Unlocked charts, int32 bit field by difficulty (bit 0 = SPB ... bit 9 = DPL)</summary>
        public int Unlocks = 0x8;
        /// <summary>Song pack item ID (music_pack_item_id, "I1800001".."I1800046"), empty for songs not sold in a pack.
        /// Confirmed 2026-11-04 against the pack folders on song select</summary>
        public StringField PackItemId = new StringField(0xC, 0x14, "ascii");
    }

    /// <summary>
    /// Personal best hash map (offsets.txt dataMap). Header offsets are relative to dataMap, node offsets to the node.
    /// Buckets between start and end are 8 byte pointers to linked lists of nodes.
    /// </summary>
    public class ScoreMapLayout
    {
        /// <summary>Pointer to the null node (empty bucket marker), int64</summary>
        public int NullNode = -0x10;
        /// <summary>Pointer to the first bucket, int64</summary>
        public int BucketsStart = 0x0;
        /// <summary>Pointer past the last bucket, int64</summary>
        public int BucketsEnd = 0x8;
        public int NodeSize = 0x40;
        /// <summary>Next node pointer, int64</summary>
        public int Next = 0x0;
        /// <summary>Previous node pointer, int64</summary>
        public int Prev = 0x8;
        /// <summary>Difficulty 0..4 (B N H A L), int32</summary>
        public int Difficulty = 0x10;
        public int Song = 0x14;
        /// <summary>0 SP, 1 DP, int32</summary>
        public int PlayType = 0x18;
        public int Score = 0x20;
        public int MissCount = 0x24;
        /// <summary>Clear count of the chart (confirmed 2026-10-15)</summary>
        public int ClearCount = 0x28;
        /// <summary>Play count of the chart (confirmed 2026-10-15)</summary>
        public int PlayCount = 0x2C;
        public int Lamp = 0x30;
    }

    /// <summary>Play data shown on the result screen (offsets.txt playData), int32 fields</summary>
    public class PlayDataLayout
    {
        public int Song = 0x0;
        /// <summary>Difficulty 0..9 (SPB ... DPL)</summary>
        public int Difficulty = 0x4;
        /// <summary>EX score of the play, used to tell this play's data from the previous one</summary>
        public int ExScore = 0x8;
        public int Lamp = 0x18;
    }

    /// <summary>Judge counts of the current / last play (offsets.txt judgeData), int32 fields. P1 and P2 sides are separate</summary>
    public class JudgeLayout
    {
        public int P1Pgreat = 0x00;
        public int P1Great = 0x04;
        public int P1Good = 0x08;
        public int P1Bad = 0x0C;
        public int P1Poor = 0x10;
        public int P2Pgreat = 0x14;
        public int P2Great = 0x18;
        public int P2Good = 0x1C;
        public int P2Bad = 0x20;
        public int P2Poor = 0x24;
        public int P1ComboBreak = 0x28;
        public int P2ComboBreak = 0x2C;
        public int P1Fast = 0x30;
        public int P2Fast = 0x34;
        public int P1Slow = 0x38;
        public int P2Slow = 0x3C;
        /// <summary>Non zero when the play ended early</summary>
        public int P1MeasureEnd = 0x40;
        public int P2MeasureEnd = 0x44;
        /// <summary>Gauge percent</summary>
        public int P1Gauge = 0x134;
        public int P2Gauge = 0x138;
    }

    /// <summary>Play options (offsets.txt playSettings), int32 fields for the P1 side, P2 side values are at + p2Offset</summary>
    public class SettingsLayout
    {
        public int Style = 0x00;
        public int Gauge = 0x04;
        public int Assist = 0x08;
        public int Flip = 0x0C;
        public int Range = 0x10;
        /// <summary>Style of the 2P side in DP</summary>
        public int Style2 = 0x14;
        public int Battle = 0x28; /* 0x20 before 2026101400 (BATTLE and H-RAN moved 8 bytes; checked with DOUBLE BATTLE on 2026-11-04) */
        public int HRan = 0x2C;
        public int P2Offset = 0x48;
    }

    /// <summary>Chart selected / playing (offsets.txt currentSong), int32 fields</summary>
    public class CurrentSongLayout
    {
        public int Song = 0x0;
        public int Difficulty = 0x4;
    }

    /// <summary>
    /// Game state detection. The play marker is found by searching from judgeData + markerSearchStart for three
    /// values over 10000 placed 8 bytes apart, the marker is markerFromMagic after the second one (non zero while playing).
    /// The song select marker (1 on song select) is relative to playSettings.
    /// </summary>
    public class GameStateLayout
    {
        public int MarkerSearchStart = 0x90;
        public int MarkerSearchLength = 0x60;
        public int MarkerFromMagic = 0x18;
        public int SongSelectMarker = -0x18;
    }

    /// <summary>
    /// Clear rates of every player (server statistics, rate × 100, -1 when there is none), SP and DP in separate MSVC std::map trees.
    /// bm2dx base + spMap / dpMap holds a pointer to an object { vtable, head node*, size }. Tree node offsets are relative to the node.
    /// Defaults for 2026111100 (2026101400: 0x25D0FA0 / 0x25D0FA8). On 2026101400 the object vtable was 0x1404E5D70 (search the heap
    /// for it when the static pointer moves).
    /// </summary>
    public class ClearRateLayout
    {
        /// <summary>Position of the pointer to the SP object, relative to the bm2dx module base</summary>
        public int SpMap = 0x25D1FA0;
        public int DpMap = 0x25D1FA8;
        public int ObjectHead = 0x8;
        public int ObjectSize = 0x10;
        public int NodeSize = 0x30;
        public int Left = 0x0;
        public int Parent = 0x8;
        public int Right = 0x10;
        /// <summary>1 byte, non zero for the head node</summary>
        public int IsNil = 0x19;
        public int Song = 0x1C;
        /// <summary>0..4 (B N H A L)</summary>
        public int Difficulty = 0x20;
        public int Rate = 0x24;
    }

    /// <summary>
    /// Rival scores (GetRivalInfo response). bm2dx base + spData / dpData holds a pointer to a vector { begin, end } of the
    /// decoded response; the rival data inside is parsed by its own big endian format (see Rivals.cs). Defaults for 2026111100
    /// (2026101400: 0x25D0FC0 / 0x25D0FC8).
    /// </summary>
    public class RivalLayout
    {
        /// <summary>Position of the pointer to the SP vector, relative to the bm2dx module base</summary>
        public int SpData = 0x25D1FC0;
        public int DpData = 0x25D1FC8;
        public int Begin = 0x0;
        public int End = 0x8;
    }

    /// <summary>
    /// Logged-in player. bm2dx base + infinitasId is the INFINITAS ID string ("C" + 12 digits) of the current login
    /// (2026111100: 0x1425D1ED4, 2026101400: 0x1425D0ED4). A login token follows it: only the 13 ID characters are ever read.
    /// </summary>
    public class PlayerLayout
    {
        public int InfinitasId = 0x25D1ED4;
        /// <summary>
        /// The player's profile in the bm2dx image (found 2026-11-11 on 2026111100, after the game version string): a copy of the
        /// INFINITAS ID (13 characters + NUL), then the DJ NAME (ASCII, up to 6 characters, NUL padded). The DJ NAME is used only when
        /// the copy matches the ID above
        /// </summary>
        public int Profile = 0x699D40;
        public int ProfileDjName = 0xE;
    }

    public class StructLayouts
    {
        public SongLayout Song = new SongLayout();
        public UnlockLayout Unlock = new UnlockLayout();
        public ScoreMapLayout ScoreMap = new ScoreMapLayout();
        public PlayDataLayout PlayData = new PlayDataLayout();
        public JudgeLayout Judge = new JudgeLayout();
        public SettingsLayout Settings = new SettingsLayout();
        public CurrentSongLayout CurrentSong = new CurrentSongLayout();
        public GameStateLayout GameState = new GameStateLayout();
        public ClearRateLayout ClearRate = new ClearRateLayout();
        public RivalLayout Rival = new RivalLayout();
        public ReadingLayout Reading = new ReadingLayout();
        public PlayerLayout Player = new PlayerLayout();
        public MastersLayout Masters = new MastersLayout();
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
        public static UnlockLayout Unlock => Current.Unlock;
        public static ScoreMapLayout ScoreMap => Current.ScoreMap;
        public static PlayDataLayout PlayData => Current.PlayData;
        public static JudgeLayout Judge => Current.Judge;
        public static SettingsLayout Settings => Current.Settings;
        public static CurrentSongLayout CurrentSong => Current.CurrentSong;
        public static GameStateLayout GameState => Current.GameState;
        public static ClearRateLayout ClearRate => Current.ClearRate;
        public static RivalLayout Rival => Current.Rival;
        public static PlayerLayout Player => Current.Player;

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
                case "ascii":
                    return System.Text.Encoding.ASCII;
                case "sjis":
                case "shift_jis":
                case "shift-jis":
                    System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    return System.Text.Encoding.GetEncoding("shift_jis");
                default:
                    return null;
            }
        }

        public static string ReadString(byte[] e, StringField f)
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
                    int i => (JToken)Hex(i),
                    StringField s => new JObject { ["offset"] = Hex(s.Offset), ["length"] = Hex(s.Length), ["encoding"] = s.Encoding },
                    _ => ToJson(v),
                };
            }
            return o;
        }

        static string Hex(int i) => i < 0 ? $"-0x{-(long)i:X}" : $"0x{i:X}";

        /// <summary>Number as JSON integer, decimal string or (-)0x hex string</summary>
        static bool TryParseInt(JToken t, out int value)
        {
            value = 0;
            if (t == null) { return false; }
            if (t.Type == JTokenType.Integer) { value = (int)t; return true; }
            var s = ((string)t ?? "").Trim();
            int sign = 1;
            if (s.StartsWith("-")) { sign = -1; s = s.Substring(1).Trim(); }
            bool ok;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                ok = int.TryParse(s.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value) && value >= 0;
            }
            else
            {
                ok = int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
            }
            value *= sign;
            return ok;
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

        /// <summary>Fields (name, offset, size) that must fit inside a record of the given size</summary>
        static string CheckInside(string prefix, int recordSize, params (string name, int offset, int size)[] fields)
        {
            foreach (var (name, offset, size) in fields)
            {
                if (offset < 0 || offset + size > recordSize) { return $"{prefix}.{name} (0x{offset:X}, {size} bytes) is outside the record (0x{recordSize:X})"; }
            }
            return null;
        }

        static string Validate(object target)
        {
            if (target is UnlockLayout u)
            {
                if (u.EntrySize < 4 || u.EntrySize > 0x1000) { return "unlock.entrySize must be 4..0x1000"; }
                return CheckInside("unlock", u.EntrySize, ("id", u.Id, 4), ("type", u.Type, 4), ("unlocks", u.Unlocks, 4), ("packItemId", u.PackItemId.Offset, u.PackItemId.Length));
            }
            if (target is ReadingLayout rd)
            {
                if (rd.EntrySize < 0x10 || rd.EntrySize > 0x1000) { return "reading.entrySize must be 0x10..0x1000"; }
                return CheckInside("reading", rd.EntrySize, ("id", rd.Id, 4), ("artistReading", rd.ArtistReading, 0x20), ("titleReading", rd.TitleReading, 0x20),
                    ("searchKey", rd.SearchKey, 0x20), ("artistKey", rd.ArtistKey, 0x20));
            }
            if (target is ScoreMapLayout m)
            {
                if (m.NodeSize < 0x10 || m.NodeSize > 0x1000) { return "scoreMap.nodeSize must be 0x10..0x1000"; }
                return CheckInside("scoreMap", m.NodeSize, ("next", m.Next, 8), ("prev", m.Prev, 8), ("difficulty", m.Difficulty, 4),
                    ("song", m.Song, 4), ("playType", m.PlayType, 4), ("score", m.Score, 4), ("missCount", m.MissCount, 4), ("clearCount", m.ClearCount, 4), ("playCount", m.PlayCount, 4), ("lamp", m.Lamp, 4));
            }
            if (target is ClearRateLayout c)
            {
                if (c.NodeSize < 0x10 || c.NodeSize > 0x1000) { return "clearRate.nodeSize must be 0x10..0x1000"; }
                return CheckInside("clearRate", c.NodeSize, ("left", c.Left, 8), ("parent", c.Parent, 8), ("right", c.Right, 8),
                    ("isNil", c.IsNil, 1), ("song", c.Song, 4), ("difficulty", c.Difficulty, 4), ("rate", c.Rate, 4));
            }
            if (target is GameStateLayout g)
            {
                if (g.MarkerSearchLength < 12 || g.MarkerSearchLength > 0x10000 || g.MarkerSearchLength % 4 != 0) { return "gameState.markerSearchLength must be a multiple of 4 in 12..0x10000"; }
                return null;
            }
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
            var which = (q["struct"] ?? "song").ToLowerInvariant();
            if (which != "song")
            {
                using var reader = DebugApi.OpenReader(out var code, out var errorCode, out var error);
                if (reader == null) { ApiServer.Write(ctx, code, Error(errorCode, error)); return; }
                object result;
                try
                {
                    result = which switch
                    {
                        "unlock" => PreviewUnlock(reader, layout.Unlock, q["start"] ?? "unlockData", count),
                        "scoremap" => PreviewScoreMap(reader, layout.ScoreMap, q["start"] ?? "dataMap", count),
                        "playdata" => PreviewFields(reader, layout.PlayData, q["start"] ?? "playData"),
                        "judge" => PreviewFields(reader, layout.Judge, q["start"] ?? "judgeData"),
                        "settings" => PreviewSettings(reader, layout.Settings, q["start"] ?? "playSettings"),
                        "currentsong" => PreviewFields(reader, layout.CurrentSong, q["start"] ?? "currentSong"),
                        "gamestate" => PreviewGameState(reader, layout.GameState),
                        "clearrate" => PreviewClearRate(reader, layout.ClearRate, q["songs"], count),
                        "reading" => PreviewReading(reader, layout.Reading, q["songs"], count),
                        "rival" => PreviewRival(reader, layout.Rival, q["songs"]),
                        "masters" => new { layout = ToJson(layout.Masters), masters = Masters.ReadExe(reader, layout.Masters, null) },
                        _ => null,
                    };
                }
                catch (ArgumentException e)
                {
                    ApiServer.Write(ctx, 400, Error("bad_start", e.Message)); return;
                }
                if (result == null)
                {
                    ApiServer.Write(ctx, 400, Error("unknown_struct", "struct must be song, unlock, scoreMap, playData, judge, settings, currentSong, gameState, clearRate, rival, reading or masters")); return;
                }
                ApiServer.Write(ctx, 200, JsonConvert.SerializeObject(result, ApiState.JsonSettings));
                return;
            }
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

        static long Resolve(DebugApi.MemoryReader r, string expr)
        {
            if (!r.TryResolve(expr, out var address, out var err)) { throw new ArgumentException(err); }
            return address;
        }

        static IEnumerable<FieldInfo> IntFields(object layout) =>
            layout.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => f.FieldType == typeof(int));

        /// <summary>Every int32 field of a flat layout read at start + offset</summary>
        static object PreviewFields(DebugApi.MemoryReader r, object layout, string startExpr)
        {
            var start = Resolve(r, startExpr);
            var values = new JObject();
            foreach (var f in IntFields(layout)) { values[CamelCase(f.Name)] = r.Int32(start + (int)f.GetValue(layout)); }
            return new { start = $"0x{start:X}", layout = ToJson(layout), values };
        }

        static object PreviewSettings(DebugApi.MemoryReader r, SettingsLayout l, string startExpr)
        {
            var start = Resolve(r, startExpr);
            var p1 = new JObject();
            var p2 = new JObject();
            foreach (var f in IntFields(l).Where(f => f.Name != nameof(SettingsLayout.P2Offset)))
            {
                int offset = (int)f.GetValue(l);
                p1[CamelCase(f.Name)] = r.Int32(start + offset);
                p2[CamelCase(f.Name)] = r.Int32(start + l.P2Offset + offset);
            }
            return new
            {
                start = $"0x{start:X}",
                layout = ToJson(l),
                p1,
                p2,
                /* Values as decoded by Settings.cs */
                legend = "style 0 OFF 1 RANDOM 2 R-RANDOM 3 S-RANDOM 4 MIRROR 5 SYNCHRONIZE 6 SYMMETRY / gauge 0 OFF 1 ASSIST EASY 2 EASY 3 HARD 4 EX HARD / " +
                         "assist 0 OFF 1 AUTO SCRATCH 2 5KEYS 3 LEGACY NOTE 4 KEY ASSIST 5 ANY KEY / range 0 OFF 1 SUDDEN+ 2 HIDDEN+ 3 SUD+ & HID+ 4 LIFT 5 LIFT & SUD+",
            };
        }

        static object PreviewUnlock(DebugApi.MemoryReader r, UnlockLayout l, string startExpr, int count)
        {
            var start = Resolve(r, startExpr);
            var entries = new List<(long address, int id, int type, int unlocks, string pack)>();
            bool endFound = false;
            const int perChunk = 256;
            while (!endFound && entries.Count < 10000)
            {
                long chunkStart = start + (long)entries.Count * l.EntrySize;
                var data = r.Read(chunkStart, perChunk * l.EntrySize);
                for (int i = 0; i < perChunk; i++)
                {
                    int o = i * l.EntrySize;
                    int id = BitConverter.ToInt32(data, o + l.Id);
                    if (id == 0) { endFound = true; break; }
                    var e = new byte[l.EntrySize];
                    Array.Copy(data, o, e, 0, l.EntrySize);
                    entries.Add((chunkStart + o, id, BitConverter.ToInt32(data, o + l.Type), BitConverter.ToInt32(data, o + l.Unlocks), ReadString(e, l.PackItemId)));
                }
            }
            var db = Utils.songDb;
            int notIncreasing = Enumerable.Range(1, Math.Max(0, entries.Count - 1)).Count(i => entries[i].id <= entries[i - 1].id);
            int outOfRange = entries.Count(e => e.id < 1000 || e.id > 99999);
            int badType = entries.Count(e => e.type < 1 || e.type > 3);
            int badBits = entries.Count(e => (e.unlocks & ~0x3FF) != 0);
            object Entry((long address, int id, int type, int unlocks, string pack) e) => new { address = $"0x{e.address:X}", id = e.id.ToString("D5"), e.type, unlocks = $"0x{e.unlocks:X3}", packItemId = e.pack };
            return new
            {
                start = $"0x{start:X}",
                layout = ToJson(l),
                summary = new
                {
                    entries = entries.Count,
                    endFound,
                    songListEntries = db.Count,
                    notInSongList = db.Count == 0 ? (int?)null : entries.Count(e => !db.ContainsKey(e.id.ToString("D5"))),
                    idsNotIncreasing = notIncreasing,
                    idsOutOfRange = outOfRange,
                    badType,
                    badUnlockBits = badBits,
                    /* Song packs: only Sub songs have an item ID */
                    packItemIds = entries.Where(e => e.pack != "").Select(e => e.pack).Distinct().Count(),
                    packSongsNotSub = entries.Count(e => e.pack != "" && e.type != 3),
                    looksValid = endFound && entries.Count >= 100 && outOfRange == 0 && badType == 0 && badBits == 0,
                },
                first = entries.Take(count).Select(Entry),
                last = entries.Skip(Math.Max(0, entries.Count - 3)).Select(Entry),
            };
        }

        static object PreviewScoreMap(DebugApi.MemoryReader r, ScoreMapLayout l, string startExpr, int count)
        {
            var map = Resolve(r, startExpr);
            long nullNode = r.Int64(map + l.NullNode);
            long bucketsStart = r.Int64(map + l.BucketsStart);
            long bucketsEnd = r.Int64(map + l.BucketsEnd);
            long size = bucketsEnd - bucketsStart;
            var header = new { map = $"0x{map:X}", nullNode = $"0x{nullNode:X}", bucketsStart = $"0x{bucketsStart:X}", bucketsEnd = $"0x{bucketsEnd:X}" };
            if (size <= 0 || size > 64 * 1024 * 1024 || size % 8 != 0)
            {
                return new { layout = ToJson(l), header, summary = new { looksValid = false, error = "bucketsEnd - bucketsStart is not a sane size" } };
            }
            var buckets = r.Read(bucketsStart, (int)size);
            var db = Utils.songDb;
            var seen = new HashSet<long>();
            var nodes = new List<(long address, int song, int diff, int playType, int score, int miss, int lamp)>();
            int nonEmpty = 0;
            bool truncated = false;
            for (int i = 0; i < buckets.Length / 8 && !truncated; i++)
            {
                long p = BitConverter.ToInt64(buckets, i * 8);
                if (p == 0 || p == nullNode || p == 0x494fdce0) { continue; } /* 0x494fdce0: skipped by ScoreMap.LoadMap as well */
                nonEmpty++;
                while (p != 0 && p != nullNode && seen.Add(p))
                {
                    if (nodes.Count >= 200000) { truncated = true; break; }
                    var n = r.Read(p, l.NodeSize);
                    nodes.Add((p, BitConverter.ToInt32(n, l.Song), BitConverter.ToInt32(n, l.Difficulty), BitConverter.ToInt32(n, l.PlayType),
                        BitConverter.ToInt32(n, l.Score), BitConverter.ToInt32(n, l.MissCount), BitConverter.ToInt32(n, l.Lamp)));
                    p = BitConverter.ToInt64(n, l.Next);
                }
            }
            int badDiff = nodes.Count(n => n.diff < 0 || n.diff > 4);
            int badPlayType = nodes.Count(n => n.playType < 0 || n.playType > 1);
            int badLamp = nodes.Count(n => n.lamp < 0 || n.lamp > 7);
            int badScore = nodes.Count(n => n.score < 0 || n.score > 20000);
            int unknownSong = db.Count == 0 ? 0 : nodes.Count(n => !db.ContainsKey(n.song.ToString("D5")));
            return new
            {
                layout = ToJson(l),
                header,
                summary = new
                {
                    buckets = buckets.Length / 8,
                    nonEmptyBuckets = nonEmpty,
                    nodes = nodes.Count,
                    truncated,
                    songListEntries = db.Count,
                    unknownSong,
                    badDifficulty = badDiff,
                    badPlayType,
                    badLamp,
                    badScore,
                    looksValid = nodes.Count > 0 && badDiff == 0 && badPlayType == 0 && badLamp == 0 && badScore == 0,
                },
                first = nodes.Take(count).Select(n => new
                {
                    address = $"0x{n.address:X}", song = n.song.ToString("D5"), difficulty = n.diff, playType = n.playType,
                    score = n.score, missCount = n.miss, lamp = n.lamp,
                }),
            };
        }

        /// <summary>Both clear rate trees; songs=12023,25094 lists those songs, otherwise the first count rates of each side</summary>
        static object PreviewClearRate(DebugApi.MemoryReader r, ClearRateLayout l, string songs, int count)
        {
            var moduleBase = Resolve(r, "base");
            var watch = (songs ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().PadLeft(5, '0')).ToHashSet();
            object Side(string side, int pointerOffset)
            {
                var nodes = ClearRates.Walk(r.Read, moduleBase + pointerOffset, l, out var error);
                if (nodes == null) { return new { pointer = $"0x{moduleBase + pointerOffset:X}", looksValid = false, error }; }
                var dtos = ClearRates.ToDtos(nodes, side).OrderBy(d => d.SongId).ThenBy(d => d.Difficulty).ToList();
                return new
                {
                    pointer = $"0x{moduleBase + pointerOffset:X}",
                    looksValid = nodes.Count > 100,
                    nodes = nodes.Count,
                    songs = nodes.Select(n => n.song).Distinct().Count(),
                    withRate = dtos.Count,
                    rates = watch.Count > 0 ? dtos.Where(d => watch.Contains(d.SongId)).ToList() : dtos.Take(count).ToList(),
                };
            }
            return new { layout = ToJson(l), sp = Side("SP", l.SpMap), dp = Side("DP", l.DpMap) };
        }

        /// <summary>Song readings (sort / search table); songs=25094 lists those songs, otherwise the first ones</summary>
        static object PreviewReading(DebugApi.MemoryReader r, ReadingLayout l, string songs, int count)
        {
            var moduleBase = Resolve(r, "base");
            var watch = (songs ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().PadLeft(5, '0')).ToHashSet();
            var readings = SongReadings.Read(r.Read, moduleBase, l, out var error);
            var db = Utils.songDb;
            int missingTitle = readings.Values.Count(x => string.IsNullOrEmpty(x.Title));
            return new
            {
                layout = ToJson(l),
                pointer = $"0x{moduleBase + l.Vector:X}",
                error,
                summary = new
                {
                    entries = readings.Count,
                    songListEntries = db.Count,
                    notInSongList = db.Count == 0 ? (int?)null : readings.Keys.Count(k => !db.ContainsKey(k)),
                    withoutTitleReading = missingTitle,
                    looksValid = readings.Count >= 100 && missingTitle < readings.Count / 10,
                },
                songs = readings.Where(kv => watch.Count == 0 || watch.Contains(kv.Key)).Take(watch.Count > 0 ? 500 : count)
                    .Select(kv => new { id = kv.Key, titleReading = kv.Value.Title, artistReading = kv.Value.Artist }),
            };
        }

        /// <summary>Both rival sides; songs=25094 also lists the rival scores of those songs</summary>
        static object PreviewRival(DebugApi.MemoryReader r, RivalLayout l, string songs)
        {
            var moduleBase = Resolve(r, "base");
            var watch = (songs ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().PadLeft(5, '0')).ToHashSet();
            object Side(string side, int pointerOffset)
            {
                var buf = Rivals.ReadBuffer(r.Read, moduleBase + pointerOffset, l, out var error);
                if (buf == null) { return new { pointer = $"0x{moduleBase + pointerOffset:X}", looksValid = false, error }; }
                var (rivals, scores) = Rivals.Parse(buf, side, out error);
                if (rivals == null) { return new { pointer = $"0x{moduleBase + pointerOffset:X}", bufferSize = buf.Length, looksValid = false, error }; }
                return new
                {
                    pointer = $"0x{moduleBase + pointerOffset:X}",
                    bufferSize = buf.Length,
                    looksValid = rivals.All(x => x.Charts > 0),
                    rivals,
                    scores = scores.Where(s => watch.Contains(s.SongId)).ToList(),
                };
            }
            return new { layout = ToJson(l), sp = Side("SP", l.SpData), dp = Side("DP", l.DpData) };
        }

        static object PreviewGameState(DebugApi.MemoryReader r, GameStateLayout l)
        {
            var judge = Resolve(r, "judgeData");
            var settings = Resolve(r, "playSettings");
            /* Same search as Utils.FindPlayStateMarker */
            long searchStart = judge + l.MarkerSearchStart;
            var buf = r.Read(searchStart, l.MarkerSearchLength);
            var big = Enumerable.Range(0, buf.Length / 4).Select(i => i * 4).Where(i => BitConverter.ToInt32(buf, i) > 10000).Take(3).ToList();
            bool found = big.Count >= 3 && big[1] - big[0] == 8 && big[2] - big[1] == 8;
            long? marker = found ? searchStart + big[1] + l.MarkerFromMagic : (long?)null;
            int? markerValue = marker is long m ? r.Int32(m) : (int?)null;
            long songSelect = settings + l.SongSelectMarker;
            int songSelectValue = r.Int32(songSelect);
            var status = JObject.Parse(ApiState.StatusJson());
            return new
            {
                layout = ToJson(l),
                playMarker = new { found, address = marker is long a ? $"0x{a:X}" : null, value = markerValue, meaning = "non zero while playing" },
                songSelectMarker = new { address = $"0x{songSelect:X}", value = songSelectValue, meaning = "1 on song select (checked only after a play)" },
                mainLoop = new { playMarkerInUse = Utils.playMarkerAvailable ? $"0x{Utils.playMarkerAddress:X}" : null, gameState = (string)status["gameState"] },
            };
        }
        #endregion

        static string Error(string code, string message) =>
            JsonConvert.SerializeObject(new { error = code, message }, ApiState.JsonSettings);
    }
}
