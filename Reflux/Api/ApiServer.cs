using System;
using System.Net;
using System.Text;
using System.Threading;

namespace Reflux.Api
{
    /// <summary>
    /// Minimal HTTP/JSON API for remote clients. Runs on its own background thread and only serves
    /// data already collected by the main loop (see <see cref="ApiState"/>), so requests never touch game memory.
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
                try
                {
                    Handle(ctx);
                }
                catch (Exception e)
                {
                    Utils.Except(e, "ApiRequest");
                    try { Write(ctx, 500, Error("internal_error", e.Message)); } catch { }
                }
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
                        Write(ctx, 200, "{\"endpoints\":[\"GET /api/status\",\"GET /api/songs\",\"GET /api/charts\",\"GET /api/scores\",\"GET /api/latest\",\"GET /api/session\",\"POST /api/attach/retry\"]}");
                        return;
                    case "/api/status": Write(ctx, 200, ApiState.StatusJson()); return;
                    case "/api/songs": Write(ctx, 200, ApiState.SongsJson()); return;
                    case "/api/charts": Write(ctx, 200, ApiState.ChartsJson()); return;
                    case "/api/scores": Write(ctx, 200, ApiState.ScoresJson()); return;
                    case "/api/latest": Write(ctx, 200, ApiState.LatestJson()); return;
                    case "/api/session": Write(ctx, 200, ApiState.SessionJson()); return;
                }
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

        static string Error(string code, string message)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(new { error = code, message }, ApiState.JsonSettings);
        }

        static void Write(HttpListenerContext ctx, int statusCode, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            var res = ctx.Response;
            res.StatusCode = statusCode;
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.OutputStream.Close();
        }
    }
}
