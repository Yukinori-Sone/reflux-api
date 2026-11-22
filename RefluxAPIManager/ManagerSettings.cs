using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Win32;

namespace RefluxAPIManager
{
    /// <summary>
    /// Reflux API Manager's own settings, RefluxAPIManager.json: next to the exe (which is next to Reflux.exe), or
    /// %LOCALAPPDATA%\RefluxAPIManager when that folder isn't writable for the user. A folder that already has the file keeps
    /// being used. Without the file, the first start takes over the screenshot settings of the former monitor
    /// (HKCU\Software\RefluxAPIServerMonitor) and replaces the logon start of the former tools.
    /// </summary>
    [DataContract]
    sealed class ManagerSettings
    {
        const string FileName = "RefluxAPIManager.json";
        const string OldRegistryKey = @"Software\RefluxAPIServerMonitor";
        public const string DefaultService = "RefluxAPIServer";

        [DataMember(Order = 2)] public string ServiceName { get; set; }
        /// <summary>The core's API on this PC (http://localhost:Port/): the last values read from its config.ini, used when it can't be read</summary>
        [DataMember(Order = 3)] public int Port { get; set; }
        [DataMember(Order = 4)] public string ApiKey { get; set; }
        /// <summary>Set once the user turned starting at logon on or off (the first start turns it on otherwise)</summary>
        [DataMember(Order = 5)] public bool AutoStartChosen { get; set; }
        [DataMember(Order = 10)] public bool ScreenshotEnabled { get; set; }
        /// <summary>Longest burst on the result screen before a frame is picked anyway, milliseconds</summary>
        [DataMember(Order = 11)] public int ScreenshotMaxBurstMs { get; set; }
        [DataMember(Order = 12)] public bool ScreenshotJpeg { get; set; }
        /// <summary>zlib level of the PNG: 6 (standard, 1.8 MB in 1 s) or 9 (3 % smaller, 6 s on the dev PC)</summary>
        [DataMember(Order = 13)] public int ScreenshotPngLevel { get; set; }
        /// <summary>JPEG quality, 50-100</summary>
        [DataMember(Order = 14)] public int ScreenshotJpegQuality { get; set; }
        /// <summary>Result kind -> keep its screenshot. Kinds missing here use the default of ScreenshotSettings.Kinds</summary>
        [DataMember(Order = 15)] public Dictionary<string, bool> ScreenshotKeep { get; set; }
        /// <summary>Tuning aid (file only): keep every frame of the burst in screenshots\burst\ with the differences in capture.log</summary>
        [DataMember(Order = 16, EmitDefaultValue = false)] public bool ScreenshotKeepBurst { get; set; }

        public ManagerSettings() => SetDefaults();

        [OnDeserializing]
        void OnDeserializing(StreamingContext _) => SetDefaults();

        void SetDefaults()
        {
            ServiceName = DefaultService;
            Port = 8790;
            ApiKey = "";
            ScreenshotEnabled = true;
            ScreenshotMaxBurstMs = 15000;
            ScreenshotPngLevel = 6;
            ScreenshotJpegQuality = 92;
            ScreenshotKeep = new Dictionary<string, bool>();
        }

        #region File
        static readonly object sync = new object();
        static ManagerSettings current;
        static string path;

        public static string FilePath => path ??= ChooseFile();

        public static ManagerSettings Current
        {
            get
            {
                lock (sync)
                {
                    if (current == null) { current = Load(); }
                    return current;
                }
            }
        }

        static ManagerSettings Load()
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    var serializer = new DataContractJsonSerializer(typeof(ManagerSettings), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                    using var stream = File.OpenRead(FilePath);
                    var s = (ManagerSettings)serializer.ReadObject(stream);
                    if (string.IsNullOrEmpty(s.ServiceName)) { s.ServiceName = DefaultService; }
                    s.ScreenshotKeep ??= new Dictionary<string, bool>();
                    return s;
                }
                catch (Exception e)
                {
                    LoadError = e.Message;
                    return new ManagerSettings(); /* Kept as it was until saved */
                }
            }
            var first = FirstRun();
            try { first.Save(); } catch { /* Read-only everywhere: works for this run */ }
            return first;
        }

        /// <summary>Why RefluxAPIManager.json couldn't be read (the defaults are used), null when it could</summary>
        public static string LoadError { get; private set; }

        public void Save()
        {
            lock (sync)
            {
                var serializer = new DataContractJsonSerializer(typeof(ManagerSettings), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                using var ms = new MemoryStream();
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                {
                    serializer.WriteObject(writer, this);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllBytes(FilePath + ".tmp", ms.ToArray());
                if (File.Exists(FilePath)) { File.Replace(FilePath + ".tmp", FilePath, null); }
                else { File.Move(FilePath + ".tmp", FilePath); }
                LoadError = null;
            }
        }

        static string ChooseFile()
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var localDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RefluxAPIManager");
            if (File.Exists(Path.Combine(exeDir, FileName))) { return Path.Combine(exeDir, FileName); }
            if (File.Exists(Path.Combine(localDir, FileName))) { return Path.Combine(localDir, FileName); }
            return Path.Combine(IsWritable(exeDir) ? exeDir : localDir, FileName);
        }

        static bool IsWritable(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, $".write-test-{Guid.NewGuid():N}");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion

        #region First start
        static ManagerSettings FirstRun()
        {
            var s = new ManagerSettings();
            try
            {
                var c = RefluxConfig.Read(RefluxFolder.ConfigPath);
                if (c != null) { s.Port = c.Port; s.ApiKey = c.ApiKey; }
            }
            catch { /* Defaults */ }
            TakeOverOldMonitor(s);
            return s;
        }

        /// <summary>Screenshot settings of the former monitor (RefluxAPIServerMonitor.exe), the logon start of it and of Beat Shutter (a test build)</summary>
        static void TakeOverOldMonitor(ManagerSettings s)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(OldRegistryKey))
                {
                    if (k != null)
                    {
                        int? I(string name) => k.GetValue(name) is int v ? v : (int?)null;
                        if (I("ScreenshotEnabled") is int en) { s.ScreenshotEnabled = en != 0; }
                        if (I("ScreenshotMaxBurstMs") is int max) { s.ScreenshotMaxBurstMs = max; }
                        if (I("ScreenshotJpeg") is int jpg) { s.ScreenshotJpeg = jpg != 0; }
                        if (I("ScreenshotPngLevel") is int lv) { s.ScreenshotPngLevel = lv >= 9 ? 9 : 6; }
                        if (I("ScreenshotJpegQuality") is int q) { s.ScreenshotJpegQuality = q; }
                        if (I("ScreenshotKeepBurst") is int burst) { s.ScreenshotKeepBurst = burst != 0; }
                        foreach (var name in k.GetValueNames())
                        {
                            if (name.StartsWith("ScreenshotKeep_") && k.GetValue(name) is int keep) { s.ScreenshotKeep[name.Substring("ScreenshotKeep_".Length)] = keep != 0; }
                        }
                        if (I("AutoStartChosen") != null) { s.AutoStartChosen = true; }
                    }
                }
                /* A former tool started at logon: this one does instead (two would fight over the screenshots) */
                using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                foreach (var old in new[] { "RefluxAPIServerMonitor", "BeatShutter" })
                {
                    if (run?.GetValue(old) == null) { continue; }
                    run.DeleteValue(old, false);
                    s.AutoStartChosen = false; /* AutoStart.EnableOnFirstRun turns this one on */
                }
            }
            catch { /* Nothing to take over */ }
        }
        #endregion
    }
}
