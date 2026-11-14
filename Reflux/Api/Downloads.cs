using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Reflux.Api
{
    /// <summary>Song data the game has downloaded (Resource\dlcache), one song</summary>
    class DownloadDto
    {
        public string SongId { get; set; }
        /// <summary>Bytes of sound\{id}.ifs (keysounds and the chart; every song has one once downloaded)</summary>
        public long Sound { get; set; }
        /// <summary>Bytes of movie\{id}.bin (background movie, only songs that have one), null when absent</summary>
        public long? Movie { get; set; }
        /// <summary>Bytes of graphic\{id}.ifs (background pictures of some songs), null when absent</summary>
        public long? Graphic { get; set; }
        /// <summary>UTC, last write time of the sound file: when the game downloaded it (the first play of the song)</summary>
        public DateTime DownloadedAt { get; set; }
    }

    /// <summary>
    /// The game downloads a song's data the first time it is played and keeps it in
    /// &lt;install folder&gt;\Resource\dlcache\{sound,movie,graphic}\&lt;song id&gt;.(ifs|bin) (seen on 2026111100; the files are encrypted).
    /// The install folder is found from bm2dx.exe (&lt;install folder&gt;\game\app\bm2dx.exe). Only the directory entries are listed
    /// (names, sizes, times; no file is opened), about 3000 files: called from the main loop thread when the game returns to song select.
    /// </summary>
    static class Downloads
    {
        static readonly Regex SongFile = new Regex(@"^(\d{5})\.(ifs|bin)$", RegexOptions.IgnoreCase);
        static int pathPid = -1;
        static string cacheDir;
        static bool warned;

        /// <summary>dlcache folder of the running game, null when it isn't found</summary>
        static string CacheDir(int pid)
        {
            if (pid == pathPid) { return cacheDir; }
            pathPid = pid;
            cacheDir = null;
            var exe = Process.GetProcessById(pid).MainModule.FileName;
            var app = Path.GetDirectoryName(exe);                       /* game\app */
            var root = Path.GetDirectoryName(Path.GetDirectoryName(app)); /* install folder */
            if (root != null)
            {
                var dir = Path.Combine(root, "Resource", "dlcache");
                if (Directory.Exists(dir)) { cacheDir = dir; }
            }
            return cacheDir;
        }

        /// <summary>Song id → (bytes, last write UTC) of one kind</summary>
        static Dictionary<string, (long size, DateTime at)> List(string dir)
        {
            var map = new Dictionary<string, (long, DateTime)>();
            if (!Directory.Exists(dir)) { return map; }
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles())
            {
                var m = SongFile.Match(f.Name);
                if (m.Success) { map[m.Groups[1].Value] = (f.Length, f.LastWriteTimeUtc); }
            }
            return map;
        }

        /// <summary>List the dlcache folder and publish it. Never throws</summary>
        public static void Refresh()
        {
            if (!Config.ApiServer_Enabled) { return; }
            try
            {
                var pid = ApiState.ProcessId;
                if (pid == null) { return; }
                var dir = CacheDir(pid.Value);
                if (dir == null)
                {
                    if (!warned)
                    {
                        Console.WriteLine("[API] Downloaded song data not available: Resource\\dlcache not found next to the game");
                        warned = true;
                    }
                    ApiState.SetDataWarning("downloads", true);
                    return;
                }
                warned = false;
                var sound = List(Path.Combine(dir, "sound"));
                var movie = List(Path.Combine(dir, "movie"));
                var graphic = List(Path.Combine(dir, "graphic"));
                var list = sound.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new DownloadDto
                {
                    SongId = kv.Key,
                    Sound = kv.Value.size,
                    Movie = movie.TryGetValue(kv.Key, out var mv) ? mv.size : (long?)null,
                    Graphic = graphic.TryGetValue(kv.Key, out var g) ? g.size : (long?)null,
                    DownloadedAt = kv.Value.at,
                }).ToList();
                ApiState.SetDataWarning("downloads", false);
                ApiState.PublishDownloads(list);
            }
            catch (Exception e)
            {
                Utils.Except(e, "Downloads");
            }
        }
    }
}
