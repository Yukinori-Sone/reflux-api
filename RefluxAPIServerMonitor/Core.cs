using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace Reflux.ServerMonitor
{
    /// <summary>The Reflux folder: the monitor lives next to Reflux.exe and config.ini</summary>
    static class RefluxFolder
    {
        public static readonly string Dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        public static string ConfigPath => Path.Combine(Dir, "config.ini");
        public static string LogPath => Path.Combine(Dir, "log.txt");
        public static string RefluxExe => Path.Combine(Dir, "Reflux.exe");

        /// <summary>Last lines of log.txt (written by the service, shared for reading)</summary>
        public static string LogTail(int lines)
        {
            try
            {
                if (!File.Exists(LogPath)) { return "（log.txt はありません。エラーが起きると作られます）"; }
                using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var all = reader.ReadToEnd().Replace("\r\n", "\n").Split('\n');
                return string.Join("\r\n", all.Skip(Math.Max(0, all.Length - lines)));
            }
            catch (Exception e)
            {
                return "log.txt を読めません: " + e.Message;
            }
        }
    }

    /// <summary>config.ini editing that keeps comments, order and the other sections as they are</summary>
    sealed class IniFile
    {
        readonly List<string> lines;
        readonly bool bom;

        IniFile(List<string> lines, bool bom) { this.lines = lines; this.bom = bom; }

        public static IniFile Load(string path)
        {
            var raw = File.ReadAllBytes(path);
            bool bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            var text = new UTF8Encoding(false).GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));
            return new IniFile(text.Replace("\r\n", "\n").Split('\n').ToList(), bom);
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

        public string Get(string section, string key, string fallback = "")
        {
            int i = Find(section, key, out _);
            if (i < 0) { return fallback; }
            var v = lines[i].Substring(lines[i].IndexOf('=') + 1).Trim();
            return v.Length >= 2 && v.StartsWith("\"") && v.EndsWith("\"") ? v.Substring(1, v.Length - 2) : v;
        }

        public bool GetBool(string section, string key) => string.Equals(Get(section, key), "true", StringComparison.OrdinalIgnoreCase);

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
            var body = Encoding.UTF8.GetBytes(string.Join("\r\n", lines));
            return bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body;
        }
    }

    /// <summary>The [Api] and output settings Reflux reads from config.ini at start</summary>
    sealed class RefluxConfig
    {
        public bool ApiEnabled;
        public string Host = "localhost";
        public int Port = 8790;
        public string ApiKey = "";
        public bool LatestJson, LatestTxt, PlayState, Marquee, FullSongInfo;

        public static RefluxConfig Read()
        {
            var c = new RefluxConfig();
            if (!File.Exists(RefluxFolder.ConfigPath)) { return c; }
            var ini = IniFile.Load(RefluxFolder.ConfigPath);
            c.ApiEnabled = ini.GetBool("Api", "enabled");
            c.Host = ini.Get("Api", "host", "localhost");
            c.Port = int.TryParse(ini.Get("Api", "port"), out var p) ? p : 8790;
            c.ApiKey = ini.Get("Api", "apikey");
            c.LatestJson = ini.GetBool("Record", "savelatestjson");
            c.LatestTxt = ini.GetBool("Record", "savelatesttxt");
            c.PlayState = ini.GetBool("Livestream", "showplaystate");
            c.Marquee = ini.GetBool("Livestream", "enablemarquee");
            c.FullSongInfo = ini.GetBool("Livestream", "enablefullsonginfo");
            return c;
        }

        /// <summary>config.ini with these values, everything else untouched</summary>
        public byte[] Apply()
        {
            var ini = IniFile.Load(RefluxFolder.ConfigPath);
            string B(bool b) => b ? "true" : "false";
            ini.Set("Api", "enabled", B(ApiEnabled));
            ini.Set("Api", "host", Host);
            ini.Set("Api", "port", Port.ToString());
            ini.Set("Api", "apikey", ApiKey);
            ini.Set("Record", "savelatestjson", B(LatestJson));
            ini.Set("Record", "savelatesttxt", B(LatestTxt));
            ini.Set("Livestream", "showplaystate", B(PlayState));
            ini.Set("Livestream", "enablemarquee", B(Marquee));
            ini.Set("Livestream", "enablefullsonginfo", B(FullSongInfo));
            return ini.ToBytes();
        }

        public bool SameAs(RefluxConfig o) =>
            ApiEnabled == o.ApiEnabled && Host == o.Host && Port == o.Port && ApiKey == o.ApiKey && LatestJson == o.LatestJson &&
            LatestTxt == o.LatestTxt && PlayState == o.PlayState && Marquee == o.Marquee && FullSongInfo == o.FullSongInfo;
    }

    #region API
    [DataContract]
    sealed class CurrentChart
    {
        [DataMember(Name = "title")] public string Title { get; set; }
        [DataMember(Name = "difficulty")] public string Difficulty { get; set; }
    }

    [DataContract]
    sealed class Status
    {
        [DataMember(Name = "refluxVersion")] public string RefluxVersion { get; set; }
        [DataMember(Name = "attachState")] public string AttachState { get; set; }
        [DataMember(Name = "attachStateSince")] public string AttachStateSince { get; set; }
        [DataMember(Name = "message")] public string Message { get; set; }
        [DataMember(Name = "processId")] public int? ProcessId { get; set; }
        [DataMember(Name = "gameVersion")] public string GameVersion { get; set; }
        [DataMember(Name = "offsetsVersion")] public string OffsetsVersion { get; set; }
        [DataMember(Name = "infinitasId")] public string InfinitasId { get; set; }
        [DataMember(Name = "gameState")] public string GameState { get; set; }
        [DataMember(Name = "currentChart")] public CurrentChart CurrentChart { get; set; }
        [DataMember(Name = "sessionPlays")] public int SessionPlays { get; set; }
    }

    [DataContract]
    sealed class CoreSettingsValues
    {
        [DataMember(Name = "pollIntervalMs")] public int PollIntervalMs { get; set; }
        [DataMember(Name = "unlockCheckSeconds")] public int UnlockCheckSeconds { get; set; }
        [DataMember(Name = "trackerTsv")] public string TrackerTsv { get; set; }
        [DataMember(Name = "processPriority")] public string ProcessPriority { get; set; }
        [DataMember(Name = "sessionFile", EmitDefaultValue = false)] public string SessionFile { get; set; }
    }

    [DataContract]
    sealed class CoreSettingsResponse
    {
        [DataMember(Name = "values")] public CoreSettingsValues Values { get; set; }
        [DataMember(Name = "defaults")] public CoreSettingsValues Defaults { get; set; }
    }

    /// <summary>The core's API on this PC (http://localhost:port works for host = localhost and host = +)</summary>
    sealed class CoreApi : IDisposable
    {
        readonly HttpClient http;

        public CoreApi(RefluxConfig config)
        {
            http = new HttpClient { BaseAddress = new Uri($"http://localhost:{config.Port}/"), Timeout = TimeSpan.FromSeconds(3) };
            if (!string.IsNullOrEmpty(config.ApiKey)) { http.DefaultRequestHeaders.Add("X-Api-Key", config.ApiKey); }
        }

        static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null") { return null; }
            var serializer = new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return (T)serializer.ReadObject(ms);
        }

        static string ToJson<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            using var ms = new MemoryStream();
            serializer.WriteObject(ms, value);
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        async Task<string> Get(string path)
        {
            using var res = await http.GetAsync(path).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) { throw new HttpRequestException($"{(int)res.StatusCode}: {body}"); }
            return body;
        }

        public async Task<Status> StatusAsync() => Parse<Status>(await Get("api/status").ConfigureAwait(false));
        public async Task<Dictionary<string, string>> LatestAsync() => Parse<Dictionary<string, string>>(await Get("api/latest").ConfigureAwait(false));
        public async Task<CoreSettingsResponse> SettingsAsync() => Parse<CoreSettingsResponse>(await Get("api/settings").ConfigureAwait(false));

        public async Task PutSettingsAsync(CoreSettingsValues values)
        {
            using var content = new StringContent(ToJson(values), Encoding.UTF8, "application/json");
            using var res = await http.PutAsync("api/settings", content).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) { throw new HttpRequestException($"{(int)res.StatusCode}: {body}"); }
        }

        public void Dispose() => http.Dispose();
    }
    #endregion

    /// <summary>
    /// The RefluxAPIServer service. Service-Install.bat lets interactive users start and stop it; where that wasn't granted, the
    /// monitor runs itself elevated (UAC) for the operation.
    /// </summary>
    static class ServiceControl
    {
        public const string Name = "RefluxAPIServer";

        /// <summary>null when the service isn't registered</summary>
        public static ServiceControllerStatus? Status()
        {
            try
            {
                using var sc = new ServiceController(Name);
                return sc.Status;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>start / stop / restart, in this process (throws on access denied)</summary>
        public static void Do(string action)
        {
            using var sc = new ServiceController(Name);
            var timeout = TimeSpan.FromSeconds(30);
            if ((action == "stop" || action == "restart") && sc.Status != ServiceControllerStatus.Stopped)
            {
                if (sc.Status != ServiceControllerStatus.StopPending) { sc.Stop(); }
                sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
            }
            if (action == "start" || action == "restart")
            {
                sc.Refresh();
                if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending) { sc.Start(); }
                sc.WaitForStatus(ServiceControllerStatus.Running, timeout);
            }
        }

        static bool AccessDenied(Exception e) =>
            e is Win32Exception w && w.NativeErrorCode == 5 || e.InnerException != null && AccessDenied(e.InnerException);

        /// <summary>Run the action, elevated when the user isn't allowed to. Returns an error message, null on success</summary>
        public static string Run(string action)
        {
            try
            {
                Do(action);
                return null;
            }
            catch (Exception e) when (AccessDenied(e))
            {
                return Elevated.Run($"--service {action}");
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                return "サービスの状態が30秒たっても変わりませんでした";
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }
    }

    /// <summary>Running this exe again as administrator for one operation (UAC prompt)</summary>
    static class Elevated
    {
        public static string Run(string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath, args) { Verb = "runas", UseShellExecute = true });
                p.WaitForExit();
                return p.ExitCode == 0 ? null : $"管理者での実行に失敗しました（{p.ExitCode}）";
            }
            catch (Win32Exception e) when (e.NativeErrorCode == 1223)
            {
                return "キャンセルされました";
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }
    }

    /// <summary>Starting the monitor at logon (HKCU Run). Turned on at the first start unless the user turned it off</summary>
    static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "RefluxAPIServerMonitor";
        const string PrefKey = @"Software\RefluxAPIServerMonitor";

        public static bool Enabled
        {
            get
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
                return k?.GetValue(ValueName) is string;
            }
            set
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) { k.SetValue(ValueName, $"\"{System.Windows.Forms.Application.ExecutablePath}\""); }
                    else { k.DeleteValue(ValueName, false); }
                }
                using var pref = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(PrefKey);
                pref.SetValue("AutoStartChosen", 1);
            }
        }

        public static void EnableOnFirstRun()
        {
            using var pref = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PrefKey);
            if (pref?.GetValue("AutoStartChosen") == null) { Enabled = true; }
        }
    }
}
