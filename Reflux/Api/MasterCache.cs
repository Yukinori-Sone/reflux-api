using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Reflux.Api
{
    /// <summary>
    /// masters-cache.json: the last master tables read from the game (exe tables, and the song / chart / series / pack tables),
    /// so GET /api/masters still answers while INFINITAS isn't running. Most of them only change with an INFINITAS update.
    /// A table is written only when its content changed (not on every read), with the time and game version it was read at.
    /// </summary>
    static class MasterCache
    {
        const string FileName = "masters-cache.json";
        static readonly object sync = new object();
        static Dictionary<string, Masters.Master> tables;
        /// <summary>JSON of the cached tables as returned (cached = true), made once per table: the song table is about 1 MB</summary>
        static readonly Dictionary<string, string> json = new Dictionary<string, string>();

        static Dictionary<string, Masters.Master> Tables()
        {
            if (tables != null) { return tables; }
            tables = new Dictionary<string, Masters.Master>();
            try
            {
                if (File.Exists(FileName))
                {
                    var list = JsonConvert.DeserializeObject<List<Masters.Master>>(File.ReadAllText(FileName), ApiState.JsonSettings);
                    foreach (var m in list ?? new List<Masters.Master>()) { if (m?.Key != null) { tables[m.Key] = m; } }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[API] {FileName} can't be read, starting without it: {e.Message}");
            }
            return tables;
        }

        /// <summary>The table content without the time it was read, to tell whether it changed</summary>
        static string Content(Masters.Master m)
        {
            var (readAt, cached) = (m.ReadAt, m.Cached);
            m.ReadAt = null; m.Cached = false;
            try { return JsonConvert.SerializeObject(m, ApiState.JsonSettings); }
            finally { m.ReadAt = readAt; m.Cached = cached; }
        }

        /// <summary>Keep freshly read tables (only those read without a warning); writes the file when one changed</summary>
        public static void Save(IEnumerable<Masters.Master> fresh)
        {
            lock (sync)
            {
                var t = Tables();
                bool changed = false;
                foreach (var m in fresh.Where(m => m.Warning == null && m.Rows.Count > 0))
                {
                    if (t.TryGetValue(m.Key, out var old) && Content(old) == Content(m)) { continue; }
                    t[m.Key] = m;
                    json.Remove(m.Key);
                    changed = true;
                }
                if (!changed) { return; }
                try
                {
                    File.WriteAllText(FileName + ".tmp", JsonConvert.SerializeObject(t.Values.OrderBy(m => m.Key).ToList(), ApiState.JsonSettings));
                    File.Move(FileName + ".tmp", FileName, true);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[API] {FileName} can't be written: {e.Message}");
                }
            }
        }

        /// <summary>JSON of the last table read from the game, marked as cached (cached = true), or null</summary>
        public static string GetJson(string key)
        {
            lock (sync)
            {
                if (json.TryGetValue(key, out var j)) { return j; }
                if (!Tables().TryGetValue(key, out var m)) { return null; }
                m.Cached = true;
                try { j = JsonConvert.SerializeObject(m, ApiState.JsonSettings); }
                finally { m.Cached = false; }
                json[key] = j;
                return j;
            }
        }
    }
}
