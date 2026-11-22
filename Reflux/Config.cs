using Microsoft.Extensions.Configuration.Ini;
using System;
using System.Collections.Generic;
using System.Text;

namespace Reflux
{
    static class Config
    {
        public static string Server { get; private set; }
        public static string UpdateServer { get; private set; }
        public static bool UpdateFiles { get; private set; }
        public static string API_key { get; private set; }
        public static bool Output_songlist { get; private set; }
        public static bool Save_remote { get; private set; }
        public static bool Save_local { get; private set; }
        public static bool Save_json { get; private set; }
        public static bool Save_latestJson { get; internal set; }
        public static bool Save_latestTxt { get; internal set; }
        public static bool Stream_Playstate { get; internal set; }
        public static bool Stream_Marquee { get; internal set; }
        public static bool Stream_FullSongInfo { get; internal set; }
        public static string MarqueeIdleText { get; private set; }
        public static bool UseLocaltime { get; private set; }
        public static bool ApiServer_Enabled { get; private set; }
        public static string ApiServer_Host { get; private set; }
        public static int ApiServer_Port { get; private set; }
        public static string ApiServer_Key { get; private set; }
        public static HeaderValues HeaderConfig { get { return header; } }
        static HeaderValues header;
        static IDictionary<string, string> config;

        public static void Parse(string configFile)
        {
            using (var stream = new System.IO.FileStream(configFile, System.IO.FileMode.OpenOrCreate))
            {
                config = IniStreamConfigurationProvider.Read(stream);
            }

            Utils.Debug("Loading config...");

            Server = ReadConfigString("remoterecord:serveraddress");
            API_key = ReadConfigString("remoterecord:apikey");

            UpdateFiles = ReadConfigBoolean("update:updatefiles");
            UpdateServer = ReadConfigString("update:updateserver");

            Save_remote = ReadConfigBoolean("record:saveremote");
            Save_local = ReadConfigBoolean("record:savelocal");
            Save_json = ReadConfigBoolean("record:savejson");
            Save_latestJson = ReadConfigBoolean("record:savelatestjson");
            Save_latestTxt = ReadConfigBoolean("record:savelatesttxt");

            header.songInfo = ReadConfigBoolean("localrecord:songinfo");
            header.chartDetails = ReadConfigBoolean("localrecord:chartdetails");
            header.resultDetails = ReadConfigBoolean("localrecord:resultdetails");
            header.judge = ReadConfigBoolean("localrecord:judge");
            header.settings = ReadConfigBoolean("localrecord:settings");
            UseLocaltime = ReadConfigBoolean("localrecord:uselocaltime");

            Stream_Playstate = ReadConfigBoolean("livestream:showplaystate");
            Stream_Marquee = ReadConfigBoolean("livestream:enablemarquee");
            Stream_FullSongInfo = ReadConfigBoolean("livestream:enablefullsonginfo");
            MarqueeIdleText = ReadConfigString("livestream:marqueeidletext");

            Output_songlist = ReadConfigBoolean("debug:outputdb");

            /* Optional section, missing keys fall back to defaults silently (API disabled) */
            ApiServer_Enabled = ReadOptional("api:enabled", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
            ApiServer_Host = ReadOptional("api:host", "localhost");
            ApiServer_Port = int.TryParse(ReadOptional("api:port", "8790"), out int port) ? port : 8790;
            ApiServer_Key = ReadOptional("api:apikey", "");
            Utils.Debug("Done\n");

        }
        static string ReadConfigString(string key)
        {
            if (config.ContainsKey(key))
            {
                Utils.Debug($"{key}: {config[key]}");
                return config[key];
            }
            Console.WriteLine($"Couldn't read key {key}, setting empty string");
            return "";
        }
        static string ReadOptional(string key, string defaultValue)
        {
            return config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : defaultValue;
        }
        static bool ReadConfigBoolean(string key)
        {
            if (config.ContainsKey(key))
            {
                if (bool.TryParse(config[key], out bool value))
                {
                    Utils.Debug($"{key}: {value}");
                    return value;
                }
            }
            Console.WriteLine($"Couldn't read key {key}, or unable to parse as boolean, setting false");
            return false;
        }
        public static string GetTsvHeader()
        {
            StringBuilder sb = new StringBuilder("title\tdifficulty");
            if (HeaderConfig.songInfo)
            {
                sb.Append("\ttitle2\tbpm\tartist\tgenre");
            }
            if (HeaderConfig.chartDetails)
            {
                sb.Append("\tnotecount\tlevel");
            }
            sb.Append("\tplaytype\tgrade\tlamp\tmisscount");
            if (HeaderConfig.resultDetails)
            {
                sb.Append("\tgaugepercent\texscore");
            }
            if (HeaderConfig.judge)
            {
                sb.Append("\tpgreat\tgreat\tgood\tbad\tpoor\tcombobreak\tfast\tslow");
            }
            if (HeaderConfig.settings)
            {
                sb.Append("\tstyle\tstyle2\tgauge\tassist\trange");
            }
            sb.Append("\tdate");

            return sb.ToString();
        }
    }
    struct HeaderValues
    {
        /* Title, difficulty, grade and lamp will always be included */
        public bool songInfo; /* Include artist, bpm, genre */
        public bool chartDetails; /* Include level, notecount */
        public bool resultDetails; /* Include gauge percent, ex score */
        public bool judge; /* Include all judge data (Pgreat, combobreak, fast, slow etc.) */
        public bool settings; /* Include style, gauge, assist, flip, range etc. */
    }
}
