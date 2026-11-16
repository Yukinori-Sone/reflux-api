using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Reflux.Api
{
    /// <summary>
    /// Constant tables in the bm2dx image (positions relative to the module base, defaults for 2026111100, see
    /// docs/INFINITAS動作.md "定数"). Most tables only hold image names: display names come from the built-in maps in
    /// Masters, matched by image name, so a table that moved or changed shows up as unknown images.
    /// </summary>
    public class MastersLayout
    {
        /// <summary>Pointers to image names: chart types (decide_diff_b ..), 5</summary>
        public int Difficulty = 0x527AB8;
        public int DifficultyCount = 5;
        /// <summary>Pointers to image names: clear lamps (clear_noplay ..), 8</summary>
        public int Lamp = 0x5A9FB0; /* 2026101400: 0x5A8F40 */
        public int LampCount = 8;
        /// <summary>Pointers to image names: dan grades (gr_d_7kyu ..), 19</summary>
        public int Grade = 0x52E7F0;
        public int GradeCount = 19;
        /// <summary>Pointers to image names: play side (spd2_p1, spd2_p2, spd2_dp), 3</summary>
        public int PlaySide = 0x52E888;
        public int PlaySideCount = 3;
        /// <summary>Pointers to image names: score graph targets (gr_gt_jiko ..), 20</summary>
        public int GraphTarget = 0x52E8A0;
        public int GraphTargetCount = 20;
        /// <summary>Prefectures: entrySize bytes each, pointer to the Shift-JIS name, pointer to the image name, id (s64)</summary>
        public int Pref = 0x6EB968; /* 2026101400: 0x6EA958 */
        public int PrefCount = 54;
        public int PrefEntrySize = 0x18;
        public int PrefName = 0x0;
        public int PrefImage = 0x8;
        public int PrefId = 0x10;
        /// <summary>Song select folders: entrySize bytes each, kind (u32), pointers to image names, category voice (u64)</summary>
        public int Folder = 0x6954C0; /* 2026101400: 0x6944B0 */
        public int FolderCount = 170;
        public int FolderEntrySize = 0x38;
        public int FolderKind = 0x0;
        public int FolderGroupImage = 0x10;
        public int FolderImage = 0x18;
        public int FolderImage2 = 0x20;
        public int FolderImage3 = 0x28;
        public int FolderVoice = 0x30;
    }

    /// <summary>GET /api/masters: master and constant tables, read from the game image on every request plus Reflux's own ones</summary>
    static class Masters
    {
        public class Column
        {
            public string Key { get; set; }
            public string Header { get; set; }
            public bool Numeric { get; set; }
        }

        public class Master
        {
            public string Key { get; set; }
            public string Name { get; set; }
            /// <summary>exe = read from the game image now, reflux = Reflux's own definition</summary>
            public string Source { get; set; }
            public string Address { get; set; }
            public string Note { get; set; }
            /// <summary>Set when the table couldn't be read or has image names the built-in map doesn't know</summary>
            public string Warning { get; set; }
            /// <summary>When the content was read from the game (null for Reflux's own definitions)</summary>
            public DateTime? ReadAt { get; set; }
            /// <summary>Game version the content was read from</summary>
            public string GameVersion { get; set; }
            /// <summary>True when INFINITAS isn't available and this is the last table read (masters-cache.json)</summary>
            public bool Cached { get; set; }
            public List<Column> Columns { get; set; } = new List<Column>();
            public List<Dictionary<string, object>> Rows { get; set; } = new List<Dictionary<string, object>>();
        }

        static Column C(string key, string header, bool numeric = false) => new Column { Key = key, Header = header, Numeric = numeric };

        #region Built-in names
        static readonly Dictionary<string, (string name, string reflux)> DifficultyNames = new Dictionary<string, (string, string)>
        {
            ["decide_diff_b"] = ("BEGINNER", "SPB / DPB"),
            ["decide_diff_n"] = ("NORMAL", "SPN / DPN"),
            ["decide_diff_h"] = ("HYPER", "SPH / DPH"),
            ["decide_diff_a"] = ("ANOTHER", "SPA / DPA"),
            ["decide_diff_l"] = ("LEGGENDARIA", "SPL / DPL"),
        };

        /// <summary>DJ POINT lamp bonus (%) as the client computes it (PERFECT = FULLCOMBO, as the game shows it)</summary>
        static readonly Dictionary<string, (string name, string reflux, int bonus)> LampNames = new Dictionary<string, (string, string, int)>
        {
            ["clear_noplay"] = ("NO PLAY", "NP", 0),
            ["clear_failed"] = ("FAILED", "F", 0),
            ["clear_assist"] = ("ASSIST CLEAR", "AC", 0),
            ["clear_easy"] = ("EASY CLEAR", "EC", 5),
            ["clear_clear"] = ("CLEAR", "NC", 10),
            ["clear_hard"] = ("HARD CLEAR", "HC", 20),
            ["clear_ex"] = ("EX HARD CLEAR", "EX", 25),
            ["clear_fullcombo"] = ("FULLCOMBO", "FC", 30),
        };

        static readonly Dictionary<string, string> GradeNames = new Dictionary<string, string>
        {
            ["gr_d_7kyu"] = "七級", ["gr_d_6kyu"] = "六級", ["gr_d_5kyu"] = "五級", ["gr_d_4kyu"] = "四級", ["gr_d_3kyu"] = "三級",
            ["gr_d_2kyu"] = "二級", ["gr_d_1kyu"] = "一級", ["gr_d_syodan"] = "初段", ["gr_d_2dan"] = "二段", ["gr_d_3dan"] = "三段",
            ["gr_d_4dan"] = "四段", ["gr_d_5dan"] = "五段", ["gr_d_6dan"] = "六段", ["gr_d_7dan"] = "七段", ["gr_d_8dan"] = "八段",
            ["gr_d_9dan"] = "九段", ["gr_d_10dan"] = "十段", ["gr_d_chuden"] = "中伝", ["gr_d_kaiden"] = "皆伝",
        };

        static readonly Dictionary<string, (string name, string reflux)> PlaySideNames = new Dictionary<string, (string, string)>
        {
            ["spd2_p1"] = ("SP 1P 側", "P1"),
            ["spd2_p2"] = ("SP 2P 側", "P2"),
            ["spd2_dp"] = ("DP", "DP"),
        };

        static readonly Dictionary<string, string> GraphTargetNames = new Dictionary<string, string>
        {
            ["gr_gt_jiko"] = "自己ベスト", ["gr_gt_rival"] = "ライバル", ["gr_gt_rival_top"] = "ライバルTOP",
            ["gr_gt_rival_average"] = "ライバル平均", ["gr_gt_altop"] = "全国TOP", ["gr_gt_alave"] = "全国平均",
            ["gr_gt_lotop"] = "県別TOP", ["gr_gt_loave"] = "県別平均", ["gr_gt_dantp"] = "同段位TOP", ["gr_gt_danav"] = "同段位平均",
            ["gr_gt_ghost"] = "ゴースト", ["gr_gt_pacemaker"] = "ペースメーカー",
        };

        static readonly Dictionary<int, string> FolderKinds = new Dictionary<int, string>
        {
            [0] = "レベル", [1] = "バージョン", [2] = "曲名の頭文字", [3] = "全曲", [4] = "BEMANI / BOSS", [5] = "DJ LEVEL",
            [6] = "クリアランプ", [7] = "MY BEST", [8] = "ライバル", [9] = "段位認定 / STEP UP", [10] = "ライバル挑戦状",
            [11] = "FAVORITE", [12] = "COMPETITION", [13] = "BIT", [14] = "ALL BEGINNER", [15] = "楽曲パック", [16] = "LEGGENDARIA",
        };

        static readonly Dictionary<int, string> SeriesNames = new Dictionary<int, string>
        {
            [0] = "1st style", [1] = "substream", [2] = "2nd style", [3] = "3rd style", [4] = "4th style", [5] = "5th style",
            [6] = "6th style", [7] = "7th style", [8] = "8th style", [9] = "9th style", [10] = "10th style", [11] = "11 IIDX RED",
            [12] = "12 HAPPY SKY", [13] = "13 DistorteD", [14] = "14 GOLD", [15] = "15 DJ TROOPERS", [16] = "16 EMPRESS",
            [17] = "17 SIRIUS", [18] = "18 Resort Anthem", [19] = "19 Lincle", [20] = "20 tricoro", [21] = "21 SPADA",
            [22] = "22 PENDUAL", [23] = "23 copula", [24] = "24 SINOBUZ", [25] = "25 CANNON BALLERS", [26] = "26 Rootage",
            [27] = "27 HEROIC VERSE", [28] = "28 BISTROVER", [29] = "29 CastHour", [30] = "30 RESIDENT", [31] = "31 EPOLIS",
            [32] = "32 Pinky Crush", [33] = "33 Sparkle Shower", [80] = "INFINITAS",
        };
        #endregion

        static readonly string[] Order = { "song", "chart", "series", "pack", "mode", "playSide", "difficulty", "lamp", "djLevel", "grade", "pref", "graphTarget", "folder", "unlockType", "option", "endState", "resultKind" };
        static readonly string[] ExeKeys = { "playSide", "difficulty", "lamp", "grade", "pref", "graphTarget", "folder" };

        /// <summary>
        /// tables: null = the constant tables (exe and Reflux's own, as before), "all" = those and the song / chart / series / pack
        /// tables (MasterTables), or a comma separated list of keys. The game image is read only when an exe table is asked for
        /// </summary>
        public static (int code, string json) GetJson(string tables)
        {
            var known = Order;
            HashSet<string> want;
            if (string.IsNullOrWhiteSpace(tables)) { want = new HashSet<string>(Order.Except(MasterTables.Keys)); }
            else if (tables.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)) { want = new HashSet<string>(Order); }
            else
            {
                want = new HashSet<string>();
                foreach (var t in tables.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0))
                {
                    var k = known.FirstOrDefault(x => x.Equals(t, StringComparison.OrdinalIgnoreCase));
                    if (k == null)
                    {
                        return (400, JsonConvert.SerializeObject(new { error = "unknown_table", message = $"unknown table {t} (known: all, {string.Join(", ", known)})" }, ApiState.JsonSettings));
                    }
                    want.Add(k);
                }
            }
            var parts = new List<(string key, string json)>();
            if (want.Overlaps(ExeKeys))
            {
                List<Master> exe;
                string openError;
                using (var reader = DebugApi.OpenReader(out _, out _, out openError)) { exe = ReadExe(reader, Layout.Current.Masters, openError); }
                var now = DateTime.UtcNow;
                var version = ApiState.GameVersion;
                foreach (var m in exe.Where(m => openError == null && m.Rows.Count > 0)) { m.ReadAt = now; m.GameVersion = version; }
                /* Keep only tables read while the game matches (ready: offsets and song list checked) and without a warning (unknown
                   image names = the table moved or changed with an INFINITAS update): the last good tables stay otherwise */
                if (openError == null && ApiState.IsReady) { MasterCache.Save(exe); }
                foreach (var m in exe)
                {
                    /* Not attached (or nothing read): the last table read, when there is one */
                    var cached = openError != null || m.Rows.Count == 0 ? MasterCache.GetJson(m.Key) : null;
                    parts.Add((m.Key, cached ?? JsonConvert.SerializeObject(m, ApiState.JsonSettings)));
                }
            }
            parts.AddRange(BuiltIn().Select(m => (m.Key, JsonConvert.SerializeObject(m, ApiState.JsonSettings))));
            if (want.Overlaps(MasterTables.Keys))
            {
                parts.AddRange(MasterTables.Json().Select(kv => (kv.Key, kv.Value))); /* Cached JSON: the song table alone is about 1 MB */
            }
            var list = parts.Where(p => want.Contains(p.key)).OrderBy(p => Array.IndexOf(Order, p.key) is var i && i >= 0 ? i : 99).Select(p => p.json);
            var head = JsonConvert.SerializeObject(new { gameVersion = ApiState.GameVersion, readAt = DateTime.UtcNow }, ApiState.JsonSettings); /* Now; each table has its own readAt / gameVersion */
            return (200, head.Substring(0, head.Length - 1) + ",\"masters\":[" + string.Join(",", list) + "]}");
        }

        /// <summary>Series (version folder) name, null for a number Reflux doesn't know</summary>
        public static string SeriesName(int no) => SeriesNames.TryGetValue(no, out var s) ? s : null;
        public static IEnumerable<int> SeriesNumbers => SeriesNames.Keys;

        /// <summary>Tables read from the game image. Without a game every table gets the reason as its warning</summary>
        public static List<Master> ReadExe(DebugApi.MemoryReader r, MastersLayout l, string openError)
        {
            long moduleBase = 0;
            string error = openError;
            if (r != null && !r.TryResolve("base", out moduleBase, out var e)) { error = e; }
            if (r == null) { error ??= "not attached"; }

            string Str(long pointer, Encoding enc)
            {
                if (pointer < 0x10000) { return null; }
                var b = r.Read(pointer, 64);
                int n = Array.IndexOf(b, (byte)0);
                return enc.GetString(b, 0, n < 0 ? b.Length : n);
            }
            string Ascii(long pointer) => Str(pointer, Encoding.ASCII);
            Master Table(string key, string name, int offset, string note, params Column[] columns)
            {
                var m = new Master { Key = key, Name = name, Source = "exe", Address = $"base+0x{offset:X}", Note = note };
                m.Columns.AddRange(columns);
                if (error != null) { m.Warning = $"ゲームから読めません（{error}）"; }
                else { m.Address = $"0x{moduleBase + offset:X}（base+0x{offset:X}）"; }
                return m;
            }
            List<string> Images(int offset, int count) =>
                Enumerable.Range(0, count).Select(i => Ascii(r.Int64(moduleBase + offset + i * 8L))).ToList();
            void Unknown(Master m, IEnumerable<string> images)
            {
                var unknown = images.Where(x => x != null).ToList();
                if (unknown.Count > 0) { m.Warning = $"知らない画像名: {string.Join(", ", unknown.Take(5))}（表の位置か並びが変わった可能性）"; }
            }
            var result = new List<Master>();

            /* Chart types */
            var diff = Table("difficulty", "譜面の種類", l.Difficulty, "自己ベストの難易度（0〜4）。Reflux の難易度は「ID ＋ SP/DP × 5」（SPB = 0 … DPL = 9）",
                C("id", "ID", true), C("name", "名前"), C("reflux", "Reflux"), C("image", "画像名"));
            if (error == null)
            {
                var images = Images(l.Difficulty, l.DifficultyCount);
                for (int i = 0; i < images.Count; i++)
                {
                    DifficultyNames.TryGetValue(images[i] ?? "", out var n);
                    diff.Rows.Add(new Dictionary<string, object> { ["id"] = i, ["name"] = n.name, ["reflux"] = n.reflux, ["image"] = images[i] });
                }
                Unknown(diff, images.Where(x => !DifficultyNames.ContainsKey(x ?? "")));
            }
            result.Add(diff);

            /* Clear lamps */
            var lamp = Table("lamp", "クリアランプ", l.Lamp,
                "DJ POINT ＝ EX スコア ×（100 ＋ ランプ補正 ＋ DJ LEVEL 補正）÷ 10000。PERFECT はゲームに保存されず FULLCOMBO 扱い（Reflux が判定から付ける）",
                C("id", "ID", true), C("name", "名前"), C("reflux", "Reflux"), C("djPointBonus", "DJ POINT 補正（%）", true), C("image", "画像名"));
            if (error == null)
            {
                var images = Images(l.Lamp, l.LampCount);
                for (int i = 0; i < images.Count; i++)
                {
                    var known = LampNames.TryGetValue(images[i] ?? "", out var n);
                    lamp.Rows.Add(new Dictionary<string, object>
                    {
                        ["id"] = i, ["name"] = n.name, ["reflux"] = n.reflux, ["djPointBonus"] = known ? n.bonus : (int?)null, ["image"] = images[i],
                    });
                }
                lamp.Rows.Add(new Dictionary<string, object> { ["id"] = 8, ["name"] = "PERFECT（Reflux のみ）", ["reflux"] = "PFC", ["djPointBonus"] = 30, ["image"] = "" });
                Unknown(lamp, images.Where(x => !LampNames.ContainsKey(x ?? "")));
            }
            result.Add(lamp);

            /* Dan grades */
            var grade = Table("grade", "段位", l.Grade, "SP・DP 共通の番号（pdata の grade_id_sp / grade_id_dp、ライバルの段位）。-1 = なし（推定）",
                C("id", "ID", true), C("name", "名前"), C("image", "画像名"));
            if (error == null)
            {
                var images = Images(l.Grade, l.GradeCount);
                for (int i = 0; i < images.Count; i++)
                {
                    grade.Rows.Add(new Dictionary<string, object> { ["id"] = i, ["name"] = GradeNames.GetValueOrDefault(images[i] ?? ""), ["image"] = images[i] });
                }
                Unknown(grade, images.Where(x => !GradeNames.ContainsKey(x ?? "")));
            }
            result.Add(grade);

            /* Play side */
            var side = Table("playSide", "プレー側", l.PlaySide, "リザルトのプレー側。Reflux の PlayType",
                C("id", "ID", true), C("name", "名前"), C("reflux", "Reflux"), C("image", "画像名"));
            if (error == null)
            {
                var images = Images(l.PlaySide, l.PlaySideCount);
                for (int i = 0; i < images.Count; i++)
                {
                    PlaySideNames.TryGetValue(images[i] ?? "", out var n);
                    side.Rows.Add(new Dictionary<string, object> { ["id"] = i, ["name"] = n.name, ["reflux"] = n.reflux, ["image"] = images[i] });
                }
                Unknown(side, images.Where(x => !PlaySideNames.ContainsKey(x ?? "")));
            }
            result.Add(side);

            /* Score graph targets */
            var target = Table("graphTarget", "グラフターゲット", l.GraphTarget, "選曲中の譜面の +0x08、GetMusicGhost の kind。ライバルは 2〜6 が登録順の1〜5人目",
                C("id", "ID", true), C("name", "名前"), C("image", "画像名"));
            if (error == null)
            {
                var images = Images(l.GraphTarget, l.GraphTargetCount);
                for (int i = 0; i < images.Count; i++)
                {
                    var name = GraphTargetNames.GetValueOrDefault(images[i] ?? "");
                    if (images[i] == "gr_gt_rival") { name += $" {i - 1}人目"; }
                    target.Rows.Add(new Dictionary<string, object> { ["id"] = i, ["name"] = name, ["image"] = images[i] });
                }
                Unknown(target, images.Where(x => !GraphTargetNames.ContainsKey(x ?? "")));
            }
            result.Add(target);

            /* Prefectures: the only table with display names in the image (Shift-JIS) */
            var pref = Table("pref", "都道府県・エリア", l.Pref, "pdata の pref_id、ライバルの県。1〜47 は JIS の都道府県コード。名前はゲームの中の文字列",
                C("id", "ID", true), C("name", "名前"), C("image", "画像名"), C("tableId", "表の番号", true));
            if (error == null)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                var sjis = Encoding.GetEncoding("shift_jis");
                for (int i = 0; i < l.PrefCount; i++)
                {
                    long e0 = moduleBase + l.Pref + (long)i * l.PrefEntrySize;
                    pref.Rows.Add(new Dictionary<string, object>
                    {
                        ["id"] = i,
                        ["name"] = Str(r.Int64(e0 + l.PrefName), sjis),
                        ["image"] = Ascii(r.Int64(e0 + l.PrefImage)),
                        ["tableId"] = r.Int64(e0 + l.PrefId) is var t && t >= 0 && t < 1000 ? t : (long?)null, /* The last entry has no number */
                    });
                }
                if ((string)pref.Rows.ElementAtOrDefault(1)?["name"] != "北海道") { pref.Warning = "1 番が北海道ではありません（表の位置が変わった可能性）"; }
            }
            result.Add(pref);

            /* Song select folders */
            var folder = Table("folder", "選曲画面のフォルダ", l.Folder,
                "フォルダ名は画像の中にしか無いので、名前は画像名からの推定。No は表の並び順（ゲームが ID として使っているかは未確認）。バージョンフォルダは「No − 15」が曲のシリーズ番号",
                C("id", "No", true), C("name", "名前（推定）"), C("kind", "種別", true), C("kindName", "種別名"), C("image", "画像名"),
                C("image2", "画像名2"), C("groupImage", "グループの画像名"), C("voice", "ボイス番号", true));
            if (error == null)
            {
                for (int i = 0; i < l.FolderCount; i++)
                {
                    long e0 = moduleBase + l.Folder + (long)i * l.FolderEntrySize;
                    int kind = r.Int32(e0 + l.FolderKind);
                    var image = Ascii(r.Int64(e0 + l.FolderImage));
                    var image2 = Ascii(r.Int64(e0 + l.FolderImage2));
                    folder.Rows.Add(new Dictionary<string, object>
                    {
                        ["id"] = i,
                        ["name"] = FolderName(kind, image, image2, i),
                        ["kind"] = kind,
                        ["kindName"] = FolderKinds.GetValueOrDefault(kind),
                        ["image"] = image,
                        ["image2"] = image2,
                        ["groupImage"] = Ascii(r.Int64(e0 + l.FolderGroupImage)),
                        ["voice"] = r.Int64(e0 + l.FolderVoice),
                    });
                }
                if ((string)folder.Rows.FirstOrDefault(x => (int)x["kind"] == 1)?["image"] != "ss_1st") { folder.Warning = "最初のバージョンフォルダが ss_1st ではありません（表の位置が変わった可能性）"; }
            }
            result.Add(folder);
            return result;
        }

        static string FolderName(int kind, string image, string image2, int no)
        {
            image ??= "";
            if (image.StartsWith("ss_stup_dif")) { return "STEP UP LEVEL " + image.Substring(11); }
            if (image.StartsWith("ss_dif")) { return "LEVEL " + image.Substring(6); }
            switch (image)
            {
                case "ss_all_beg": return "ALL BEGINNER";
                case "ss_all_cn": return "全曲 CN";
                case "ss_legendaria": return "LEGGENDARIA";
                case "ss_bemani": return "BEMANI";
                case "ss_boss": return "BOSS";
                case "ss_number": return "数字";
                case "ss_other": return "その他";
                case "ss_rival": return "ライバル";
                case "ss_rival_win": return "ライバルに勝っている譜面";
                case "ss_rival_lose": return "ライバルに負けている譜面";
                case "ss_setpiece": return "段位認定";
                case "ss_setpiece_kiso": return "段位認定（基礎）";
                case "ss_rival_challenge": return "ライバル挑戦状";
                case "ss_bit": return "BIT";
            }
            if (kind == 1)
            {
                int series = image == "ss_80th" ? 80 : no - 15;
                return SeriesNames.TryGetValue(series, out var s) ? s : $"バージョン {series}";
            }
            if (kind == 15) { return PackFolderName(image2 ?? ""); }
            return FolderKinds.TryGetValue(kind, out var k) ? k : null;
        }

        /// <summary>
        /// Song pack folder names as shown on song select (checked 2026-11-04, 2026101400): so_pack_mu1..30 music packs, st1..3 startup selections,
        /// ot1..13 collaboration packs in release order. The pack of a song is the unlock data's item ID (layout unlock.packItemId), see the client's Names.Packs
        /// </summary>
        static string PackFolderName(string image)
        {
            var m = System.Text.RegularExpressions.Regex.Match(image, @"^so_pack_(mu|st|ot)(\d+)$");
            if (!m.Success) { return "楽曲パック " + image; }
            int n = int.Parse(m.Groups[2].Value);
            switch (m.Groups[1].Value)
            {
                case "mu": return $"楽曲パック vol.{n}";
                case "st": return $"スタートアップセレクション 楽曲パック vol.{n}";
            }
            return CollabPacks.TryGetValue(n, out var name) ? name : $"コラボ楽曲パック {n}";
        }

        static readonly Dictionary<int, string> CollabPacks = new Dictionary<int, string>
        {
            [1] = "pop'n music セレクション 楽曲パック vol.1", [2] = "jubeat セレクション 楽曲パック vol.1", [3] = "BPL セレクション 楽曲パック vol.1",
            [4] = "SOUND VOLTEX セレクション 楽曲パック vol.1", [5] = "pop'n music セレクション 楽曲パック vol.2", [6] = "BPL セレクション 楽曲パック vol.2",
            [7] = "東方Project セレクション 楽曲パック vol.1", [8] = "ULTIMATE MOBILE セレクション 楽曲パック vol.1", [9] = "SOUND VOLTEX セレクション 楽曲パック vol.2",
            [10] = "GITADORA セレクション 楽曲パック vol.1", [11] = "The 4th セレクション 楽曲パック vol.1", [12] = "BPL セレクション 楽曲パック vol.3",
            [13] = "DanceDanceRevolution セレクション 楽曲パック vol.1",
        };

        /// <summary>Reflux's own definitions (not tables in the game, or tables not located yet)</summary>
        static IEnumerable<Master> BuiltIn()
        {
            Master M(string key, string name, string note, Column[] columns, params object[][] rows)
            {
                var m = new Master { Key = key, Name = name, Source = "reflux", Note = note };
                m.Columns.AddRange(columns);
                foreach (var row in rows) { m.Rows.Add(columns.Zip(row, (c, v) => (c.Key, v)).ToDictionary(x => x.Key, x => x.v)); }
                return m;
            }
            yield return M("mode", "モード（SP / DP）", "自己ベストのノードの +0x18、pdata の *_sp / *_dp",
                new[] { C("id", "ID", true), C("name", "名前") },
                new object[] { 0, "SP" }, new object[] { 1, "DP" });
            yield return M("djLevel", "DJ LEVEL",
                "ゲームには保存されず、EX スコアから計算する（理論値 ＝ ノーツ数 × 2）。DJ POINT ＝ EX スコア ×（100 ＋ ランプ補正 ＋ DJ LEVEL 補正）÷ 10000",
                new[] { C("id", "ID", true), C("name", "名前"), C("condition", "条件（EX スコア ÷ 理論値）"), C("djPointBonus", "DJ POINT 補正（%）", true) },
                new object[] { 0, "NO PLAY", "未プレー", 0 }, new object[] { 1, "F", "2/9 未満", 0 }, new object[] { 2, "E", "2/9 以上", 0 },
                new object[] { 3, "D", "3/9 以上", 0 }, new object[] { 4, "C", "4/9 以上", 0 }, new object[] { 5, "B", "5/9 以上", 0 },
                new object[] { 6, "A", "6/9 以上", 10 }, new object[] { 7, "AA", "7/9 以上", 15 }, new object[] { 8, "AAA", "8/9 以上", 20 });
            yield return M("unlockType", "解禁種別", "解禁データの種別。customtypes.txt で曲ごとのラベルを付けている",
                new[] { C("id", "ID", true), C("name", "Reflux"), C("description", "内容") },
                new object[] { 1, "Base", "最初から遊べる" }, new object[] { 2, "Bits", "BIT で解禁" }, new object[] { 3, "Sub", "楽曲パック（customtypes.txt で指定が無ければこれ）" });
            var opts = new List<object[]>();
            void O(string type, string offset, params string[] names) { for (int i = 0; i < names.Length; i++) { opts.Add(new object[] { type, offset, i, names[i] }); } }
            O("譜面（P1 / DP 左）", "style", "OFF", "RANDOM", "R-RANDOM", "S-RANDOM", "MIRROR", "SYNCHRONIZE RANDOM", "SYMMETRY RANDOM");
            O("譜面（DP 右）", "style2", "OFF", "RANDOM", "R-RANDOM", "S-RANDOM", "MIRROR", "SYNCHRONIZE RANDOM", "SYMMETRY RANDOM");
            O("ゲージ", "gauge", "OFF", "ASSIST EASY", "EASY", "HARD", "EX HARD");
            O("アシスト", "assist", "OFF", "AUTO SCRATCH", "LEGACY NOTE", "KEY ASSIST", "ANY KEY");
            O("レーンカバー", "range", "OFF", "SUDDEN+", "HIDDEN+", "SUD+ & HID+", "LIFT", "LIFT & SUD+");
            yield return M("option", "オプション", "Reflux が読むオプションの値（layout.json の settings。P2 は +0x48）",
                new[] { C("type", "種類"), C("field", "layout の項目"), C("id", "値", true), C("name", "名前") }, opts.ToArray());
            yield return M("endState", "プレーの終わり方",
                "判定データ（judgeData）の +0x48（P1）/ +0x4C（P2）。リザルト画面で読む（layout.json の judge.p1EndState / p2EndState）。" +
                "2026111100 で確認。SP で閉じると両側が 5、DP では P1 側だけ。ゲージ残量はゲージが尽きても閉じても 0 になる",
                new[] { C("id", "値", true), C("name", "意味") },
                new object[] { 0, "最後まで完走" }, new object[] { 1, "プレーしていない側" }, new object[] { 2, "ゲージが尽きた（HARD・EX HARD・段位）" },
                new object[] { 3, "放置で落ちた（50ノーツ以上続けて叩かなかった。EASY でも落ちる）" }, new object[] { 5, "自分で閉じた" });
            yield return M("resultKind", "リザルトの区分",
                "Reflux がプレーに付ける区分（/api/latest・/api/plays の resultKind。Api/ResultKind.cs）。モニターはこれで、リザルト画像を残すかどうか決める",
                new[] { C("id", "コード"), C("name", "名前"), C("condition", "条件"), C("keep", "画像の保存（初期値）") },
                new object[] { "first", "完走・クリア（初回）", "直前の自己ベストが NO PLAY・EX 0", "保存する" },
                new object[] { "pb", "完走・クリア（自己ベスト更新あり）", "クリアランプ・EX スコア・ミスカウントのどれかが直前の自己ベストより良い（DJ LEVEL は EX に含まれる。段位ゲージの完走はランプを比べない）", "保存する" },
                new object[] { "nopb", "完走・クリア（自己ベスト更新なし）", "どれも良くなっていない", "保存しない" },
                new object[] { "failed", "完走・FAILED", "", "保存する" },
                new object[] { "dead", "途中落ち", "途中終了で、終わり方が 2（どちらも無ければ HARD・EX HARD・段位ゲージ）", "保存する" },
                new object[] { "quit", "自分で閉じた", "途中終了で、終わり方が 5（どちらも無ければそれ以外のゲージ）", "保存しない" },
                new object[] { "idle", "放置で落ちた", "途中終了で、終わり方が 3（50ノーツ以上続けて叩かなかった）", "保存しない" },
                new object[] { "unrecorded", "プレーの記録が取れなかったもの", "モニターだけの区分（コアの記録が来なかった）", "保存しない" });
        }
    }
}
