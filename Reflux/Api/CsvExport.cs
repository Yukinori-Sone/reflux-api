using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Reflux.Api
{
    /// <summary>
    /// GET /api/export/csv/{who}: my or a rival's scores in the format of the INFINITAS score CSV of the KONAMI site
    /// (…_sp_score.csv / …_dp_score.csv), so tools that read that file can read Reflux's data too.
    ///   who = self (play side by ?style=sp|dp, SP when omitted), sp-self, dp-self, sp-rival-1 .. sp-rival-5, dp-rival-1 .. dp-rival-5
    /// Same shape as the site's file: UTF-8 with BOM, LF line ends, the header ending with a comma, one row per song with at least one
    /// played chart, songs by version (1st style and substream together as "1st&amp;substream", INFINITAS last) then by the title in
    /// lower case. Titles and artists have "，" for "," like the site (fields are never quoted), and for the songs the site writes otherwise
    /// than the game, the site's (inf-csv-names.tsv, built in).
    /// The game keeps no dates, so both date columns are always "---". Rivals have no miss count ("---") nor play count (0).
    /// PERFECT is written as FULLCOMBO CLEAR (the site has no PERFECT either).
    /// </summary>
    static class CsvExport
    {
        static readonly (char code, string header)[] Charts =
        {
            ('B', "BEGINNER"), ('N', "NORMAL"), ('H', "HYPER"), ('A', "ANOTHER"), ('L', "LEGGENDARIA"),
        };

        /// <summary>Lamp code of the core → clear type of the site</summary>
        static readonly Dictionary<string, string> ClearTypes = new Dictionary<string, string>
        {
            ["NP"] = "NO PLAY", ["F"] = "FAILED", ["AC"] = "ASSIST CLEAR", ["EC"] = "EASY CLEAR", ["NC"] = "CLEAR",
            ["HC"] = "HARD CLEAR", ["EX"] = "EX HARD CLEAR", ["FC"] = "FULLCOMBO CLEAR", ["PFC"] = "FULLCOMBO CLEAR",
        };

        static readonly Regex Who = new Regex(@"^(?:(self)|(sp|dp)-self|(sp|dp)-rival-([1-5]))$");

        class Entry
        {
            public int Ex;
            public int? Miss;
            public string Lamp;
            public int? Plays;
        }

        public static void Handle(HttpListenerContext ctx, string who)
        {
            var m = Who.Match(who ?? "");
            if (!m.Success)
            {
                ApiServer.Write(ctx, 404, JsonConvert.SerializeObject(new { error = "not_found",
                    message = "use /api/export/csv/self?style=sp|dp, sp-self, dp-self, sp-rival-1 .. sp-rival-5 or dp-rival-1 .. dp-rival-5" }));
                return;
            }
            string side;
            if (m.Groups[1].Success)
            {
                var style = (ctx.Request.QueryString["style"] ?? "sp").ToLowerInvariant();
                if (style != "sp" && style != "dp")
                {
                    ApiServer.Write(ctx, 400, JsonConvert.SerializeObject(new { error = "bad_style", message = "style must be sp or dp" }));
                    return;
                }
                side = style.ToUpperInvariant();
            }
            else
            {
                side = (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value).ToUpperInvariant();
            }
            bool rival = m.Groups[4].Success;

            var (songs, scores, rivals, rivalScores, myId) = ApiState.ExportSource();
            if (songs.Count == 0)
            {
                ApiServer.Write(ctx, 503, JsonConvert.SerializeObject(new { error = "not_ready", message = "song data hasn't been read from the game yet" }));
                return;
            }

            var entries = new Dictionary<string, Dictionary<char, Entry>>();
            void Add(string songId, string difficulty, Entry e)
            {
                if (difficulty == null || difficulty.Length != 3 || !difficulty.StartsWith(side)) { return; }
                if (!entries.TryGetValue(songId, out var map)) { entries[songId] = map = new Dictionary<char, Entry>(); }
                map[difficulty[2]] = e;
            }

            string id;
            string kind;
            if (rival)
            {
                int index = int.Parse(m.Groups[4].Value) - 1;
                var r = rivals.FirstOrDefault(x => x.Side == side && x.Index == index);
                if (r == null)
                {
                    ApiServer.Write(ctx, 404, JsonConvert.SerializeObject(new { error = "not_found",
                        message = $"no {side} rival {index + 1} (the game hasn't sent the rivals yet, or fewer are registered)" }));
                    return;
                }
                id = r.InfinitasId;
                kind = "rivals";
                foreach (var s in rivalScores.Where(x => x.Side == side && x.Index == index))
                {
                    Add(s.SongId, s.Difficulty, new Entry { Ex = s.ExScore, Lamp = s.Lamp });
                }
            }
            else
            {
                id = myId;
                kind = "scores";
                foreach (var s in scores)
                {
                    Add(s.SongId, s.Difficulty, new Entry { Ex = s.ExScore, Miss = s.MissCount, Lamp = s.Lamp, Plays = s.PlayCount });
                }
            }

            var body = Build(songs, entries, rival);
            var headers = ApiState.DataHeaders(kind);
            headers["Content-Disposition"] = $"attachment; filename=\"{FileName(id, side)}\"";
            if (id != null) { headers["X-Infinitas-Id"] = id; }
            ApiServer.WriteBytes(ctx, 200, "text/csv; charset=utf-8", body, headers);
        }

        static bool Played(Entry e) => e != null && ((e.Lamp != null && e.Lamp != "NP") || e.Ex > 0);

        /// <summary>The CSV as bytes (UTF-8 with BOM)</summary>
        static byte[] Build(List<SongDto> songs, Dictionary<string, Dictionary<char, Entry>> entries, bool rival)
        {
            var sb = new StringBuilder();
            sb.Append("バージョン,タイトル,アーティスト,");
            foreach (var (_, h) in Charts)
            {
                sb.Append($"{h} スコア,{h} 最終スコア更新時刻,{h} ミスカウント,{h} クリアタイプ,{h} プレー回数,{h} 最終プレー日時,");
            }
            sb.Append('\n');

            var names = SiteNames();
            var rows = songs
                .Where(s => entries.TryGetValue(s.Id, out var map) && map.Values.Any(Played))
                .Select(s => names.TryGetValue(s.Id, out var n) ? (song: s, title: n.title, artist: n.artist) : (song: s, title: Text(s.Title), artist: Text(s.Artist)))
                .OrderBy(x => x.song.Folder <= 1 ? 0 : x.song.Folder)
                .ThenBy(x => x.title.ToLowerInvariant(), StringComparer.Ordinal)
                .ThenBy(x => x.song.Id, StringComparer.Ordinal);
            foreach (var (song, title, artist) in rows)
            {
                var map = entries[song.Id];
                sb.Append(VersionName(song.Folder)).Append(',').Append(title).Append(',').Append(artist);
                foreach (var (code, _) in Charts)
                {
                    map.TryGetValue(code, out var e);
                    if (!Played(e))
                    {
                        sb.Append(",0,---,---,NO PLAY,0,---");
                        continue;
                    }
                    var clear = ClearTypes.TryGetValue(e.Lamp ?? "NP", out var c) ? c : "NO PLAY";
                    var miss = !rival && e.Miss.HasValue ? e.Miss.Value.ToString() : "---";
                    var plays = !rival && e.Plays.HasValue ? e.Plays.Value : 0;
                    sb.Append($",{e.Ex},---,{miss},{clear},{plays},---");
                }
                sb.Append('\n');
            }
            var utf8 = new UTF8Encoding(true);
            return utf8.GetPreamble().Concat(utf8.GetBytes(sb.ToString())).ToArray();
        }

        static Dictionary<string, (string title, string artist)> siteNames;

        /// <summary>
        /// Songs the site writes otherwise than the game (spaces, quotes, characters, even the genre as the artist): the embedded
        /// inf-csv-names.tsv (song_id, title, artist, memo), the same file as Beat Ledger's
        /// </summary>
        static Dictionary<string, (string title, string artist)> SiteNames()
        {
            if (siteNames != null) { return siteNames; }
            var map = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            using (var stream = typeof(CsvExport).Assembly.GetManifestResourceStream("inf-csv-names.tsv"))
            {
                if (stream != null)
                {
                    using var reader = new System.IO.StreamReader(stream, Encoding.UTF8);
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0 || line[0] == '#' || line.StartsWith("song_id\t", StringComparison.Ordinal)) { continue; }
                        var c = line.Split('\t');
                        if (c.Length >= 3 && c[0].Length == 5) { map[c[0]] = (c[1], c[2]); }
                    }
                }
            }
            return siteNames = map;
        }

        /// <summary>A field as the site writes it: no line breaks, "，" for "," (the site never quotes a field)</summary>
        static string Text(string s) => (s ?? "").Replace("\r", "").Replace("\n", " ").Replace(",", "，");

        /// <summary>Version name as in the site's file: "1st&amp;substream", "2nd style" .. "10th style", "IIDX RED" .. "Sparkle Shower", "INFINITAS"</summary>
        public static string VersionName(int folder)
        {
            if (folder <= 1) { return "1st&substream"; }
            var name = Masters.SeriesName(folder);
            if (name == null) { return $"Ver.{folder}"; }
            var m = Regex.Match(name, @"^\d+ (.+)$"); /* "11 IIDX RED" → "IIDX RED" */
            return m.Success ? m.Groups[1].Value : name;
        }

        /// <summary>The site's file name: C-6170-6736-1181_dp_score.csv</summary>
        static string FileName(string infinitasId, string side)
        {
            var id = infinitasId != null && Regex.IsMatch(infinitasId, @"^C\d{12}$")
                ? $"C-{infinitasId.Substring(1, 4)}-{infinitasId.Substring(5, 4)}-{infinitasId.Substring(9, 4)}"
                : "unknown";
            return $"{id}_{side.ToLowerInvariant()}_score.csv";
        }
    }
}
