using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Reflux.Api
{
    /// <summary>
    /// Minimal HTTP/JSON API for remote clients. Runs on its own background thread and only serves
    /// data already collected by the main loop (see <see cref="ApiState"/>), so requests never touch game memory.
    /// Exception: the maintenance endpoints in <see cref="DebugApi"/> (offsets, raw memory read).
    /// </summary>
    static class ApiServer
    {
        static HttpListener listener;

        public static void Start()
        {
            var prefix = $"http://{Config.ApiServer_Host}:{Config.ApiServer_Port}/";
            listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            try
            {
                listener.Start();
            }
            catch (HttpListenerException e)
            {
                Console.WriteLine($"[API] Failed to start API server on {prefix}: {e.Message}");
                if (Config.ApiServer_Host != "localhost" && Config.ApiServer_Host != "127.0.0.1")
                {
                    Console.WriteLine($"[API] Listening on non-localhost needs a URL reservation once (as administrator):");
                    Console.WriteLine($"[API]   netsh http add urlacl url={prefix} user=%USERNAME%");
                }
                Utils.Except(e, "ApiServerStart");
                listener = null;
                return;
            }
            var thread = new Thread(Loop) { IsBackground = true, Name = "ApiServer" };
            thread.Start();
            Console.WriteLine($"[API] Listening on {prefix}");
        }

        static void Loop()
        {
            while (listener != null && listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = listener.GetContext();
                }
                catch (Exception)
                {
                    break;
                }
                /* Each request on the thread pool: one client that reads a large response slowly (the masters are about 2.5 MB)
                   must not hold up the others, e.g. the monitor's status polling */
                ThreadPool.QueueUserWorkItem(_ => Run(ctx));
            }
        }

        /// <summary>Requests that change something (PUT / POST: offsets, layout, settings, re-attach) still run one at a time</summary>
        static readonly object changeSync = new object();

        static void Run(HttpListenerContext ctx)
        {
            try
            {
                if (ctx.Request.HttpMethod == "GET" || ctx.Request.HttpMethod == "HEAD") { Handle(ctx); }
                else { lock (changeSync) { Handle(ctx); } }
            }
            catch (Exception e)
            {
                Utils.Except(e, "ApiRequest");
                try { Write(ctx, 500, Error("internal_error", e.Message)); } catch { }
            }
        }

        static void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            if (!string.IsNullOrEmpty(Config.ApiServer_Key) && req.Headers["X-Api-Key"] != Config.ApiServer_Key)
            {
                Write(ctx, 401, Error("unauthorized", "Missing or wrong X-Api-Key header"));
                return;
            }

            var path = req.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            var method = req.HttpMethod.ToUpperInvariant();

            if (method == "GET")
            {
                switch (path)
                {
                    case "":
                    case "/api":
                        Write(ctx, 200, "{\"endpoints\":[\"GET /api/status\",\"GET /api/songs\",\"GET /api/charts\",\"GET /api/scores\",\"GET /api/latest\",\"GET /api/session\",\"GET /api/clearrates\",\"GET /api/rivals\",\"GET /api/rivals/scores\",\"GET /api/downloads\",\"GET /api/plays?since=&limit=&infinitasId=\",\"GET /api/masters?tables=\",\"GET /api/screenshots\",\"GET /api/screenshot?key=\",\"POST /api/attach/retry\"," +
                            "\"GET /api/offsets\",\"PUT /api/offsets\",\"GET /api/memory/info\",\"GET /api/memory?address=&length=&format=hex|bin&encoding=sjis\"," +
                            "\"GET /api/memory/search?start=&length=&hex=|text=&encoding=|int32=|int64=&max=\"," +
                            "\"GET /api/settings\",\"PUT /api/settings\",\"GET /api/layout\",\"PUT /api/layout\",\"GET|POST /api/layout/preview?start=&count=\"]}");
                        return;
                    case "/api/layout": Write(ctx, 200, Layout.GetJson()); return;
                    case "/api/settings": Write(ctx, 200, CoreSettings.GetJson()); return;
                    case "/api/plays": Plays(ctx); return;
                    case "/api/masters":
                        {
                            var (code, json) = Masters.GetJson(ctx.Request.QueryString["tables"]);
                            Write(ctx, code, json);
                            return;
                        }
                    case "/api/screenshots": Write(ctx, 200, Screenshots.ListJson()); return;
                    case "/api/screenshot": Screenshots.Get(ctx); return;
                    case "/api/layout/preview": Layout.Preview(ctx, null); return;
                    case "/api/status": Write(ctx, 200, ApiState.StatusJson()); return;
                    case "/api/songs": Write(ctx, 200, ApiState.SongsJson(), ApiState.DataHeaders("songs")); return;
                    case "/api/charts": Write(ctx, 200, ApiState.ChartsJson(), ApiState.DataHeaders("songs")); return;
                    case "/api/scores": Write(ctx, 200, ApiState.ScoresJson(), ApiState.DataHeaders("scores")); return;
                    case "/api/latest": Write(ctx, 200, ApiState.LatestJson()); return;
                    case "/api/session": Write(ctx, 200, ApiState.SessionJson()); return;
                    case "/api/clearrates": Write(ctx, 200, ApiState.ClearRatesJson(), ApiState.DataHeaders("clearRates")); return;
                    case "/api/rivals": Write(ctx, 200, ApiState.RivalsJson(), ApiState.DataHeaders("rivals")); return;
                    case "/api/rivals/scores": Write(ctx, 200, ApiState.RivalScoresJson(), ApiState.DataHeaders("rivals")); return;
                    case "/api/downloads": Write(ctx, 200, ApiState.DownloadsJson(), ApiState.DataHeaders("downloads")); return;
                    case "/api/offsets": Write(ctx, 200, DebugApi.GetOffsetsJson()); return;
                    case "/api/memory/info":
                        {
                            var (code, json) = DebugApi.GetInfoJson();
                            Write(ctx, code, json);
                            return;
                        }
                    case "/api/memory": DebugApi.Dump(ctx); return;
                    case "/api/memory/search": DebugApi.Search(ctx); return;
                }
            }
            else if ((method == "PUT" || method == "POST") && path == "/api/offsets")
            {
                string body;
                using (var reader = new StreamReader(req.InputStream, Encoding.UTF8)) { body = reader.ReadToEnd(); }
                var (code, json) = DebugApi.PutOffsets(body);
                Write(ctx, code, json);
                return;
            }
            else if ((method == "PUT" || method == "POST") && path == "/api/settings")
            {
                var (code, json) = CoreSettings.Put(ReadBody(req));
                Write(ctx, code, json);
                return;
            }
            else if ((method == "PUT" || method == "POST") && path == "/api/layout")
            {
                var (code, json) = Layout.Put(ReadBody(req));
                Write(ctx, code, json);
                return;
            }
            else if (method == "POST" && path == "/api/layout/preview")
            {
                Layout.Preview(ctx, ReadBody(req));
                return;
            }
            else if (method == "POST")
            {
                switch (path)
                {
                    case "/api/attach/retry":
                        ApiState.RequestRetry();
                        Console.WriteLine("[API] Re-attach requested by client");
                        Write(ctx, 202, "{\"accepted\":true}");
                        return;
                }
            }
            Write(ctx, 404, Error("not_found", $"{method} {path}"));
        }

        /// <summary>GET /api/plays?since=2026-10-15T00:00:00Z&amp;limit=1000&amp;infinitasId=C... : play history, oldest first</summary>
        static void Plays(HttpListenerContext ctx)
        {
            var q = ctx.Request.QueryString;
            DateTime? since = null;
            if (!string.IsNullOrEmpty(q["since"]))
            {
                if (!DateTime.TryParse(q["since"], System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t))
                {
                    Write(ctx, 400, Error("bad_since", "since must be an ISO 8601 date-time")); return;
                }
                since = t;
            }
            int limit = int.TryParse(q["limit"], out var l) ? Math.Clamp(l, 1, 100000) : 10000;
            Write(ctx, 200, Newtonsoft.Json.JsonConvert.SerializeObject(PlayLog.Read(since, limit, string.IsNullOrEmpty(q["infinitasId"]) ? null : q["infinitasId"])));
        }

        static string ReadBody(HttpListenerRequest req)
        {
            using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        static string Error(string code, string message)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(new { error = code, message }, ApiState.JsonSettings);
        }

        public static void Write(HttpListenerContext ctx, int statusCode, string json, Dictionary<string, string> headers = null)
        {
            WriteBytes(ctx, statusCode, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json), headers);
        }

        public static void WriteBytes(HttpListenerContext ctx, int statusCode, string contentType, byte[] bytes, Dictionary<string, string> headers)
        {
            var res = ctx.Response;
            res.StatusCode = statusCode;
            res.ContentType = contentType;
            if (headers != null)
            {
                foreach (var h in headers) { res.AddHeader(h.Key, h.Value); }
            }
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.OutputStream.Close();
        }
    }
}
