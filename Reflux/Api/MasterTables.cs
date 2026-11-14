using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reflux.Api
{
    /// <summary>
    /// Master tables built from the core's own snapshot (no game memory read): song, chart, series and pack, in the same
    /// shape as the exe tables of GET /api/masters. They match the Score Manager's master tab, which builds them from its
    /// database (Names.DefaultSeries / Names.Packs there hold the same names as here; the client's series names can be edited
    /// in its database, these are the defaults). Built once per songs / clear rates revision and kept as JSON.
    /// </summary>
    static class MasterTables
    {
        public static readonly string[] Keys = { "song", "chart", "series", "pack" };
        static readonly string[] Diffs = { "SPB", "SPN", "SPH", "SPA", "SPL", "DPB", "DPN", "DPH", "DPA", "DPL" };
        static readonly Dictionary<char, string> ChartTypes = new Dictionary<char, string>
        {
            ['B'] = "BEGINNER", ['N'] = "NORMAL", ['H'] = "HYPER", ['A'] = "ANOTHER", ['L'] = "LEGGENDARIA",
        };

        /// <summary>
        /// Song packs in the order of the game's pack folders (folder table No 50..95): item ID of the unlock data, folder name on
        /// song select and folder image. Same as the client's Names.Packs (checked against the game on 2026101400, 2026-11-04)
        /// </summary>
        public static readonly (string itemId, string name, string image)[] Packs = BuildPacks();

        static (string, string, string)[] BuildPacks()
        {
            var list = new List<(string, string, string)>();
            int[] music = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 16, 18, 20, 23, 24, 26, 27, 28, 30, 31, 32, 35, 37, 38, 40, 42, 45 };
            for (int i = 0; i < music.Length; i++) { list.Add(($"I18{music[i]:D5}", $"楽曲パック vol.{i + 1}", $"so_pack_mu{i + 1}")); }
            int[] startup = { 14, 17, 19 };
            for (int i = 0; i < startup.Length; i++) { list.Add(($"I18{startup[i]:D5}", $"スタートアップセレクション 楽曲パック vol.{i + 1}", $"so_pack_st{i + 1}")); }
            (int item, int ot, string name)[] collab =
            {
                (15, 1, "pop'n music セレクション 楽曲パック vol.1"), (29, 5, "pop'n music セレクション 楽曲パック vol.2"),
                (41, 10, "GITADORA セレクション 楽曲パック vol.1"), (21, 2, "jubeat セレクション 楽曲パック vol.1"),
                (25, 4, "SOUND VOLTEX セレクション 楽曲パック vol.1"), (39, 9, "SOUND VOLTEX セレクション 楽曲パック vol.2"),
                (46, 13, "DanceDanceRevolution セレクション 楽曲パック vol.1"), (36, 8, "ULTIMATE MOBILE セレクション 楽曲パック vol.1"),
                (22, 3, "BPL セレクション 楽曲パック vol.1"), (33, 6, "BPL セレクション 楽曲パック vol.2"), (44, 12, "BPL セレクション 楽曲パック vol.3"),
                (43, 11, "The 4th セレクション 楽曲パック vol.1"), (34, 7, "東方Project セレクション 楽曲パック vol.1"),
            };
            foreach (var c in collab) { list.Add(($"I18{c.item:D5}", c.name, $"so_pack_ot{c.ot}")); }
            return list.ToArray();
        }

        static readonly object sync = new object();
        static object builtFrom;
        static Dictionary<string, string> built = new Dictionary<string, string>();
        static List<Masters.Master> builtTables = new List<Masters.Master>();

        /// <summary>
        /// JSON of each table (Masters.Master), rebuilt when the songs or clear rates changed. Before Reflux has read the song list
        /// (INFINITAS not started since Reflux started), the last tables read (masters-cache.json, cached = true)
        /// </summary>
        public static Dictionary<string, string> Json()
        {
            var (songs, charts, rates, songsAt, ratesAt, version) = ApiState.MasterSource();
            if (songs.Count == 0)
            {
                var cached = Keys.Select(k => (k, json: MasterCache.GetJson(k))).Where(c => c.json != null).ToDictionary(c => c.k, c => c.json);
                if (cached.Count == Keys.Length) { return cached; }
            }
            var key = Tuple.Create<object, object, object>(songs, charts, rates);
            lock (sync)
            {
                if (!Equals(key, builtFrom))
                {
                    var tables = Build(songs, charts, rates);
                    if (songs.Count > 0)
                    {
                        foreach (var m in tables)
                        {
                            m.ReadAt = m.Key == "chart" && ratesAt > songsAt ? ratesAt : songsAt; /* The chart table has the clear rates too */
                            m.GameVersion = version;
                        }
                    }
                    builtTables = tables;
                    built = tables.ToDictionary(m => m.Key, m => JsonConvert.SerializeObject(m, ApiState.JsonSettings));
                    builtFrom = key;
                }
                MasterCache.Save(Trusted(builtTables, songs.Count, rates.Count)); /* Every request: writes a deleted cache file again */
                return built;
            }
        }

        /// <summary>
        /// The tables that may replace the cached ones. Not while Reflux isn't ready (offsets or song list don't match the game),
        /// not the song table while the readings can't be read, not the chart table without clear rates (not received yet, or
        /// unreadable after an update), and none when far fewer songs than cached (a broken read rather than removed songs).
        /// Called on every request, so a deleted cache file is written again while the game is running
        /// </summary>
        static IEnumerable<Masters.Master> Trusted(List<Masters.Master> tables, int songs, int rates)
        {
            if (songs == 0 || !ApiState.IsReady) { return Enumerable.Empty<Masters.Master>(); }
            int cachedSongs = MasterCache.CachedRows("song");
            if (cachedSongs > 0 && songs < cachedSongs * 0.9)
            {
                Console.WriteLine($"[API] masters-cache.json kept: {songs} songs now against {cachedSongs} cached");
                return Enumerable.Empty<Masters.Master>();
            }
            return tables.Where(m => !(m.Key == "song" && ApiState.HasDataWarning("readings")) &&
                                     !(m.Key == "chart" && (rates == 0 || ApiState.HasDataWarning("clearRates"))));
        }

        static Masters.Column C(string key, string header, bool numeric = false) => new Masters.Column { Key = key, Header = header, Numeric = numeric };

        static List<Masters.Master> Build(List<SongDto> songs, List<ChartDto> charts, List<ClearRateDto> rates)
        {
            string warning = songs.Count == 0 ? "曲データがまだありません（Reflux の起動後、まだゲームの曲リストを読んでいない。INFINITAS を起動すると入る）" : null;
            var bySong = charts.GroupBy(c => c.SongId).ToDictionary(g => g.Key, g => g.ToDictionary(c => c.Difficulty, c => c.Level));
            var titles = songs.ToDictionary(s => s.Id, s => s.Title);
            var rate = rates.GroupBy(r => (r.SongId, r.Difficulty)).ToDictionary(g => g.Key, g => g.First().Rate);
            string SeriesName(int no) => Masters.SeriesName(no) ?? $"Ver.{no}";

            var song = new Masters.Master
            {
                Key = "song", Name = "曲", Source = "reflux", Warning = warning,
                Note = "曲名（英字）はゲームの英字タイトル（ローマ字読み・英訳など）。読みはゲームの並び替え用の表（カタカナ。選曲画面のフォルダの中はこの順）。listed = false はゲームの解禁データに無い曲（NOT DISPLAYED）。SPB〜DPL は譜面のレベル（譜面が無ければ null）",
                Columns = new[] { C("id", "ID", true), C("title", "曲名"), C("titleReading", "曲名の読み"), C("title2", "曲名（英字）"), C("artist", "アーティスト"),
                    C("artistReading", "アーティストの読み"), C("genre", "ジャンル"), C("seriesNo", "シリーズ番号", true), C("series", "登場作品"),
                    C("unlock", "解禁種別"), C("label", "ラベル"), C("pack", "楽曲パック"), C("packName", "楽曲パックの名前"), C("listed", "表示") }
                    .Concat(Diffs.Select(d => C(d.ToLowerInvariant(), d, true))).ToList(), /* Row keys are camel cased by the JSON settings: spb .. dpl */
            };
            var packNames = Packs.ToDictionary(p => p.itemId, p => p.name);
            foreach (var s in songs.OrderBy(s => s.Id))
            {
                var row = new Dictionary<string, object>
                {
                    ["id"] = s.Id, ["title"] = s.Title, ["titleReading"] = s.TitleReading, ["title2"] = s.Title2, ["artist"] = s.Artist,
                    ["artistReading"] = s.ArtistReading, ["genre"] = s.Genre, ["seriesNo"] = s.Folder, ["series"] = SeriesName(s.Folder),
                    ["unlock"] = s.UnlockType, ["label"] = s.Label, ["pack"] = s.PackItemId,
                    ["packName"] = s.PackItemId == null ? null : packNames.TryGetValue(s.PackItemId, out var pn) ? pn : null,
                    ["listed"] = s.Listed,
                };
                bySong.TryGetValue(s.Id, out var levels);
                foreach (var d in Diffs) { row[d.ToLowerInvariant()] = levels != null && levels.TryGetValue(d, out var lv) ? (object)lv : null; }
                song.Rows.Add(row);
            }

            var chart = new Masters.Master
            {
                Key = "chart", Name = "譜面", Source = "reflux", Warning = warning,
                Note = "譜面専用の ID は無く、ゲームはどこでも「曲ID ＋ 難易度」で譜面を表す。BPM は譜面ごと（曲全体の BPM はゲームに無い）。全国クリアレートは値の無い譜面は null",
                Columns = new List<Masters.Column> { C("songId", "曲ID", true), C("title", "曲名"), C("difficulty", "難易度"), C("chartType", "譜面の種類"),
                    C("level", "レベル", true), C("notes", "ノーツ数", true), C("bpm", "BPM"), C("clearRate", "全国クリアレート（%）", true) },
            };
            foreach (var c in charts.OrderBy(c => c.SongId).ThenBy(c => Array.IndexOf(Diffs, c.Difficulty)))
            {
                chart.Rows.Add(new Dictionary<string, object>
                {
                    ["songId"] = c.SongId, ["title"] = titles.TryGetValue(c.SongId, out var t) ? t : null, ["difficulty"] = c.Difficulty,
                    ["chartType"] = c.Difficulty.Length == 3 && ChartTypes.TryGetValue(c.Difficulty[2], out var ct) ? ct : null,
                    ["level"] = c.Level, ["notes"] = c.Notes, ["bpm"] = c.Bpm,
                    ["clearRate"] = rate.TryGetValue((c.SongId, c.Difficulty), out var r) ? (object)r : null,
                });
            }

            var series = new Masters.Master
            {
                Key = "series", Name = "シリーズ（登場作品）", Source = "reflux", Warning = warning,
                Note = "曲リストのバージョンフォルダ番号（0 = 1st style、1 = substream、2 以降 = バージョン、80 = INFINITAS）。名前は Reflux の定義（Score Manager では DB で変えられる）",
                Columns = new List<Masters.Column> { C("id", "ID", true), C("name", "シリーズ名"), C("songs", "曲数", true) },
            };
            var songCounts = songs.GroupBy(s => s.Folder).ToDictionary(g => g.Key, g => g.Count());
            foreach (var no in Masters.SeriesNumbers.Union(songCounts.Keys).OrderBy(n => n))
            {
                series.Rows.Add(new Dictionary<string, object> { ["id"] = no, ["name"] = SeriesName(no), ["songs"] = songCounts.TryGetValue(no, out var n) ? n : 0 });
            }

            var pack = new Masters.Master
            {
                Key = "pack", Name = "楽曲パック", Source = "reflux", Warning = warning,
                Note = "商品ID はゲームの解禁データの music_pack_item_id（パックで売っている曲だけにある）。No と名前は選曲画面の楽曲パックフォルダの並び（実機で確認）。名前の無い商品ID は新しいパック（Reflux への追加待ち）。ラベルは customtypes.txt（本家同梱、2026-03 以降のパックは無い）",
                Columns = new List<Masters.Column> { C("no", "No", true), C("item", "商品ID"), C("name", "名前"), C("image", "フォルダの画像名"), C("songs", "曲数", true), C("labels", "ラベル") },
            };
            var bySongPack = songs.Where(s => s.PackItemId != null).GroupBy(s => s.PackItemId).ToDictionary(g => g.Key, g => g.ToList());
            var items = Packs.Select(p => p.itemId).Concat(bySongPack.Keys.Where(k => !packNames.ContainsKey(k)).OrderBy(k => k));
            foreach (var item in items)
            {
                int no = Array.FindIndex(Packs, p => p.itemId == item);
                bySongPack.TryGetValue(item, out var packSongs);
                var labels = packSongs?.Where(s => s.Label != null).Select(s => s.Label).Distinct().ToList();
                pack.Rows.Add(new Dictionary<string, object>
                {
                    ["no"] = no >= 0 ? (object)(no + 1) : null, ["item"] = item, ["name"] = no >= 0 ? Packs[no].name : null,
                    ["image"] = no >= 0 ? Packs[no].image : null, ["songs"] = packSongs?.Count ?? 0,
                    ["labels"] = labels == null || labels.Count == 0 ? null : string.Join(",", labels),
                });
            }
            return new List<Masters.Master> { song, chart, series, pack };
        }
    }
}
