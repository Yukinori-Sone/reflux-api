using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Reflux.Api
{
    /// <summary>
    /// Result screen screenshots taken by RefluxAPIServerMonitor.exe (the core can't take them when it runs as a service) in
    /// screenshots\, named after the play: &lt;play time UTC yyyyMMddTHHmmssZ&gt;_&lt;song id&gt;_&lt;difficulty&gt;.png|jpg.
    /// GET /api/screenshots lists them, GET /api/screenshot?key=... returns one.
    /// </summary>
    static class Screenshots
    {
        const string Folder = "screenshots";
        static readonly Regex KeyPattern = new Regex(@"^\d{8}T\d{6}Z_(\d{5}_(SP|DP)[BNHAL]|unrecorded)$");
        static readonly string[] Extensions = { ".png", ".jpg" };

        public static string ListJson()
        {
            var list = new List<object>();
            if (Directory.Exists(Folder))
            {
                foreach (var f in Directory.GetFiles(Folder).Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f))
                {
                    var key = Path.GetFileNameWithoutExtension(f);
                    if (!KeyPattern.IsMatch(key)) { continue; }
                    var info = new FileInfo(f);
                    list.Add(new { key, file = info.Name, size = info.Length, savedAt = info.LastWriteTimeUtc });
                }
            }
            return JsonConvert.SerializeObject(list, ApiState.JsonSettings);
        }

        public static void Get(HttpListenerContext ctx)
        {
            var key = ctx.Request.QueryString["key"] ?? "";
            if (!KeyPattern.IsMatch(key))
            {
                ApiServer.Write(ctx, 400, JsonConvert.SerializeObject(new { error = "bad_key", message = "key must look like 20261015T063424Z_28054_DPA" })); return;
            }
            foreach (var ext in Extensions)
            {
                var path = Path.Combine(Folder, key + ext);
                if (!File.Exists(path)) { continue; }
                byte[] bytes;
                using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    bytes = ms.ToArray();
                }
                ApiServer.WriteBytes(ctx, 200, ext == ".png" ? "image/png" : "image/jpeg", bytes, null);
                return;
            }
            ApiServer.Write(ctx, 404, JsonConvert.SerializeObject(new { error = "not_found", message = "no screenshot for " + key }));
        }
    }
}
