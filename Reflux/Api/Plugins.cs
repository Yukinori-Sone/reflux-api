using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;

namespace Reflux.Plugins
{
    /// <summary>
    /// A plugin: a class with a public parameterless constructor in plugins\*.dll (next to Reflux.exe), loaded when the API is enabled.
    /// Build it against Reflux.dll (netcoreapp3.1) and check <see cref="IPluginHost.ApiVersion"/> before using newer members.
    /// </summary>
    public interface IRefluxPlugin
    {
        string Name { get; }
        void Initialize(IPluginHost host);
    }

    /// <summary>A play the core is recording (result screen, main loop thread)</summary>
    public sealed class PlayRecord
    {
        public string SongId { get; internal set; }
        /// <summary>SPB..DPL</summary>
        public string Difficulty { get; internal set; }
        /// <summary>Notes of the play (a DOUBLE BATTLE play counts the SP chart twice)</summary>
        public int NoteCount { get; internal set; }
        public bool Battle { get; internal set; }
        /// <summary>Assist option as in the play record (e.g. "AUTO SCRATCH", "LEGACY NOTE"), null when none</summary>
        public string Assist { get; internal set; }
        /// <summary>The play record (as GET /api/latest and /api/plays return it): fields added here are kept with the play</summary>
        public IDictionary<string, string> Fields { get; internal set; }
    }

    /// <summary>What the core gives a plugin. Hooks run on the main loop thread, request handlers on the thread pool</summary>
    public interface IPluginHost
    {
        /// <summary><see cref="PluginHost.ApiVersion"/></summary>
        int ApiVersion { get; }
        string RefluxVersion { get; }

        /// <summary>The game's process id, null when not attached</summary>
        int? ProcessId { get; }
        /// <summary>The game version (P2D:J:B:A:yyyymmddnn), null until known</summary>
        string GameVersion { get; }
        /// <summary>Base address of bm2dx.exe in the game, 0 when not attached</summary>
        long ModuleBase { get; }
        /// <summary>Path of bm2dx.exe, null when not attached</summary>
        string GameExePath { get; }
        /// <summary>Reads game memory (read only)</summary>
        byte[] ReadMemory(long address, int length);

        /// <summary>
        /// A section of layout.json owned by the plugin: public int fields (0x-hex in the file), public class fields for nested
        /// objects. Register it in Initialize; it shows in GET /api/layout and can be changed with PUT /api/layout like the core's
        /// </summary>
        void RegisterLayout(string section, Func<object> defaults);
        /// <summary>The section as in use now (layout.json or the defaults)</summary>
        T Layout<T>(string section) where T : class;

        /// <summary>Called after a play is recorded, before it is written to the play log and published</summary>
        void OnPlayRecorded(Action<PlayRecord> handler);
        /// <summary>Called every time the game returns to song select</summary>
        void OnSongSelect(Action handler);

        void MapGet(string path, Action<HttpListenerContext> handler, string description = null);
        void WriteJson(HttpListenerContext ctx, int statusCode, object value, IDictionary<string, string> headers = null);
        void WriteBytes(HttpListenerContext ctx, int statusCode, string contentType, byte[] bytes);
        /// <summary>The core's JSON settings (camelCase, ISO dates)</summary>
        JsonSerializerSettings JsonSettings { get; }

        /// <summary>Increments /api/status pluginRevisions[name]: tells the clients the data changed</summary>
        void BumpRevision(string name);
        /// <summary>Adds / removes a name in /api/status dataWarnings</summary>
        void SetDataWarning(string name, bool unreadable);
        void Log(string message);
        void Error(Exception e, string where);
    }

    /// <summary>Loads the plugins and passes the core's events to them</summary>
    static class PluginHost
    {
        /// <summary>Raised when members are added to IPluginHost</summary>
        public const int ApiVersion = 1;
        const string Dir = "plugins";

        static readonly List<IRefluxPlugin> loaded = new List<IRefluxPlugin>();
        static readonly List<Action<PlayRecord>> playHandlers = new List<Action<PlayRecord>>();
        static readonly List<Action> songSelectHandlers = new List<Action>();
        static readonly Dictionary<string, (Action<HttpListenerContext> handler, string description)> routes =
            new Dictionary<string, (Action<HttpListenerContext>, string)>();
        static readonly object sync = new object();

        /// <summary>Names and versions of the loaded plugins (for /api/status)</summary>
        public static List<string> Names { get { lock (sync) { return loaded.Select(p => $"{p.Name} {p.GetType().Assembly.GetName().Version}").ToList(); } } }

        /// <summary>Loads plugins\*.dll; a plugin that fails is skipped with a message</summary>
        public static void Load()
        {
            if (!Directory.Exists(Dir)) { return; }
            var host = new Host();
            foreach (var file in Directory.GetFiles(Dir, "*.dll").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var asm = Assembly.LoadFrom(Path.GetFullPath(file));
                    var types = asm.GetTypes().Where(t => typeof(IRefluxPlugin).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null);
                    foreach (var t in types)
                    {
                        var p = (IRefluxPlugin)Activator.CreateInstance(t);
                        p.Initialize(host);
                        lock (sync) { loaded.Add(p); }
                        Console.WriteLine($"[API] Plugin loaded: {p.Name} ({Path.GetFileName(file)})");
                    }
                }
                catch (Exception e)
                {
                    var detail = e is ReflectionTypeLoadException r ? string.Join("; ", r.LoaderExceptions.Select(x => x?.Message).Distinct()) : e.Message;
                    Console.WriteLine($"[API] Plugin {Path.GetFileName(file)} not loaded: {detail}");
                    Utils.Except(e, "PluginLoad");
                }
            }
        }

        public static void RaisePlayRecorded(PlayData play, Dictionary<string, string> form)
        {
            List<Action<PlayRecord>> handlers;
            lock (sync) { handlers = playHandlers.ToList(); }
            if (handlers.Count == 0) { return; }
            if (!int.TryParse(form.TryGetValue("notecount", out var nc) ? nc : null, out var notes)) { notes = play.Chart.totalNotes; }
            var record = new PlayRecord
            {
                SongId = play.Chart.songid,
                Difficulty = play.Chart.difficulty.ToString(),
                NoteCount = notes,
                Battle = play.Battle,
                Assist = form.TryGetValue("assist", out var a) ? a : null,
                Fields = form,
            };
            foreach (var h in handlers)
            {
                try { h(record); }
                catch (Exception e) { Utils.Except(e, "PluginPlayRecorded"); }
            }
        }

        public static void RaiseSongSelect()
        {
            List<Action> handlers;
            lock (sync) { handlers = songSelectHandlers.ToList(); }
            foreach (var h in handlers)
            {
                try { h(); }
                catch (Exception e) { Utils.Except(e, "PluginSongSelect"); }
            }
        }

        /// <summary>Runs the plugin's handler for a GET path, false when no plugin has it</summary>
        public static bool TryHandleGet(string path, HttpListenerContext ctx)
        {
            Action<HttpListenerContext> handler;
            lock (sync)
            {
                if (!routes.TryGetValue(path, out var r)) { return false; }
                handler = r.handler;
            }
            handler(ctx);
            return true;
        }

        /// <summary>The plugins' endpoints for the GET /api list</summary>
        public static IEnumerable<string> Endpoints()
        {
            lock (sync) { return routes.Select(r => "GET " + (r.Value.description ?? r.Key)).ToList(); }
        }

        sealed class Host : IPluginHost
        {
            public int ApiVersion => PluginHost.ApiVersion;
            public string RefluxVersion => Api.BuildInfo.DisplayVersion;
            public int? ProcessId => Api.ApiState.ProcessId;
            public string GameVersion => Api.ApiState.GameVersion;

            public long ModuleBase
            {
                get
                {
                    var pid = ProcessId;
                    if (pid == null) { return 0; }
                    try { return (long)Process.GetProcessById(pid.Value).MainModule.BaseAddress; }
                    catch (Exception) { return 0; }
                }
            }

            public string GameExePath
            {
                get
                {
                    var pid = ProcessId;
                    if (pid == null) { return null; }
                    try { return Process.GetProcessById(pid.Value).MainModule.FileName; }
                    catch (Exception) { return null; }
                }
            }

            public byte[] ReadMemory(long address, int length) => Utils.ReadRaw(address, length);

            public void RegisterLayout(string section, Func<object> defaults) => Api.Layout.RegisterSection(section, defaults);
            public T Layout<T>(string section) where T : class => Api.Layout.Section(section) as T;

            public void OnPlayRecorded(Action<PlayRecord> handler) { lock (sync) { playHandlers.Add(handler); } }
            public void OnSongSelect(Action handler) { lock (sync) { songSelectHandlers.Add(handler); } }

            public void MapGet(string path, Action<HttpListenerContext> handler, string description = null)
            {
                lock (sync) { routes[path.TrimEnd('/').ToLowerInvariant()] = (handler, description); }
            }

            public void WriteJson(HttpListenerContext ctx, int statusCode, object value, IDictionary<string, string> headers = null) =>
                Api.ApiServer.Write(ctx, statusCode, value as string ?? JsonConvert.SerializeObject(value, Api.ApiState.JsonSettings),
                    headers == null ? null : new Dictionary<string, string>(headers));

            public void WriteBytes(HttpListenerContext ctx, int statusCode, string contentType, byte[] bytes) =>
                Api.ApiServer.WriteBytes(ctx, statusCode, contentType, bytes, null);

            public JsonSerializerSettings JsonSettings => Api.ApiState.JsonSettings;

            public void BumpRevision(string name) => Api.ApiState.BumpPluginRevision(name);
            public void SetDataWarning(string name, bool unreadable) => Api.ApiState.SetDataWarning(name, unreadable);
            public void Log(string message) => Console.WriteLine("[API] " + message);
            public void Error(Exception e, string where) => Utils.Except(e, where);
        }
    }
}
