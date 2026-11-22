using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Reflux.Api
{
    /// <summary>
    /// GET|PUT /api/outputs: the config.ini switches for the latest play and stream overlay files ([Record] savelatestjson /
    /// savelatesttxt, [Livestream] showplaystate / enablemarquee / enablefullsonginfo). The main loop reads them at every
    /// poll, so a change applies at once; it is also written to config.ini (comments, order and other keys kept) for the
    /// next start. Lets a tool change them without access to the core's folder.
    /// </summary>
    static class OutputSettings
    {
        const string FileName = "config.ini";
        static readonly object sync = new object();

        /// <summary>API name, config.ini section and key, getter and setter of the value Reflux uses</summary>
        static readonly (string name, string section, string key, Func<bool> get, Action<bool> set)[] Items =
        {
            ("latestJson", "Record", "savelatestjson", () => Config.Save_latestJson, v => Config.Save_latestJson = v),
            ("latestTxt", "Record", "savelatesttxt", () => Config.Save_latestTxt, v => Config.Save_latestTxt = v),
            ("playState", "Livestream", "showplaystate", () => Config.Stream_Playstate, v => Config.Stream_Playstate = v),
            ("marquee", "Livestream", "enablemarquee", () => Config.Stream_Marquee, v => Config.Stream_Marquee = v),
            ("fullSongInfo", "Livestream", "enablefullsonginfo", () => Config.Stream_FullSongInfo, v => Config.Stream_FullSongInfo = v),
        };

        public static string GetJson()
        {
            lock (sync)
            {
                var o = new JObject();
                foreach (var i in Items) { o[i.name] = i.get(); }
                return o.ToString(Formatting.None);
            }
        }

        /// <summary>PUT /api/outputs: { "marquee": true, ... }, only the given switches change</summary>
        public static (int code, string json) Put(string body)
        {
            JObject req;
            try { req = JObject.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body); }
            catch (JsonException e) { return (400, Error("bad_json", e.Message)); }
            var changes = new List<((string name, string section, string key, Func<bool> get, Action<bool> set) item, bool value)>();
            foreach (var p in req.Properties())
            {
                var item = Items.FirstOrDefault(i => i.name == p.Name);
                if (item.name == null) { return (400, Error("bad_setting", $"unknown setting {p.Name}")); }
                if (p.Value.Type != JTokenType.Boolean) { return (400, Error("bad_setting", $"{p.Name} must be true or false")); }
                changes.Add((item, p.Value.Value<bool>()));
            }
            lock (sync)
            {
                try
                {
                    var ini = IniText.Load(FileName);
                    foreach (var (item, value) in changes) { ini.Set(item.section, item.key, value ? "true" : "false"); }
                    File.WriteAllBytes(FileName + ".tmp", ini.ToBytes());
                    File.Move(FileName + ".tmp", FileName, true);
                }
                catch (Exception e)
                {
                    return (500, Error("write_failed", e.Message));
                }
                foreach (var (item, value) in changes) { item.set(value); }
            }
            if (changes.Count > 0) { Console.WriteLine($"[API] {FileName} output settings updated by client"); }
            return (200, GetJson());
        }

        static string Error(string code, string message) => JsonConvert.SerializeObject(new { error = code, message }, ApiState.JsonSettings);
    }

    /// <summary>Editing one key of an ini file while keeping comments, order, the other keys, the line ends and a BOM</summary>
    sealed class IniText
    {
        readonly List<string> lines;
        readonly bool bom;
        readonly string newline;

        IniText(List<string> lines, bool bom, string newline) { this.lines = lines; this.bom = bom; this.newline = newline; }

        public static IniText Load(string path)
        {
            var raw = File.Exists(path) ? File.ReadAllBytes(path) : new byte[0];
            bool bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            var text = new UTF8Encoding(false).GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));
            var newline = text.Contains("\r\n") || !text.Contains("\n") ? "\r\n" : "\n";
            return new IniText(text.Replace("\r\n", "\n").Split('\n').ToList(), bom, newline);
        }

        /// <summary>Index of the key's line in the section, -1 when missing. sectionEnd is where a new key would go</summary>
        int Find(string section, string key, out int sectionEnd)
        {
            sectionEnd = -1;
            bool inSection = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    if (inSection) { return -1; }
                    inSection = string.Equals(t.Substring(1, t.Length - 2), section, StringComparison.OrdinalIgnoreCase);
                    if (inSection) { sectionEnd = i + 1; }
                    continue;
                }
                if (!inSection) { continue; }
                if (t.Length > 0) { sectionEnd = i + 1; }
                if (t.StartsWith(";") || t.StartsWith("#")) { continue; }
                int eq = t.IndexOf('=');
                if (eq > 0 && string.Equals(t.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase)) { return i; }
            }
            return -1;
        }

        public void Set(string section, string key, string value)
        {
            int i = Find(section, key, out int end);
            var line = $"{key} = {value}";
            if (i >= 0) { lines[i] = line; return; }
            if (end < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) { lines.Add(""); }
                lines.Add($"[{section}]");
                lines.Add(line);
                return;
            }
            lines.Insert(end, line);
        }

        public byte[] ToBytes()
        {
            var body = Encoding.UTF8.GetBytes(string.Join(newline, lines));
            return bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body;
        }
    }
}
