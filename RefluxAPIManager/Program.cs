using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace RefluxAPIManager
{
    static class Program
    {
        public const string Title = "Reflux API Manager";

        /// <summary>
        /// Normal start: tray monitor (one per user session). --status / --settings also open that window at start.
        /// Elevated helpers started by Reflux API Manager itself: --service start|stop|restart, --write-config &lt;temp file&gt; &lt;config.ini&gt;
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += LoadEmbeddedAssembly;
            if (args.Length >= 2 && args[0] == "--service")
            {
                try { ServiceControl.Do(args[1]); return 0; }
                catch (Exception e) { MessageBox.Show(e.Message, Title); return 1; }
            }
            if (args.Length >= 3 && args[0] == "--write-config")
            {
                try
                {
                    if (!string.Equals(Path.GetFileName(args[2]), "config.ini", StringComparison.OrdinalIgnoreCase)) { throw new ArgumentException("config.ini 以外には書きません: " + args[2]); }
                    File.Copy(args[1], args[2], true);
                    return 0;
                }
                catch (Exception e) { MessageBox.Show(e.Message, Title); return 1; }
            }

            using var mutex = new Mutex(true, @"Local\RefluxAPIManager", out bool first);
            if (!first) { return 0; } /* Already in the tray */
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AutoStart.EnableOnFirstRun();
            var context = new TrayContext();
            if (Array.IndexOf(args, "--status") >= 0) { context.OpenStatus(); }
            if (Array.IndexOf(args, "--settings") >= 0) { context.OpenSettings(); }
            Application.Run(context);
            return 0;
        }

        /// <summary>Libraries built into the exe (resource lib.&lt;name&gt;.dll, see the csproj)</summary>
        static Assembly LoadEmbeddedAssembly(object sender, ResolveEventArgs e)
        {
            var name = "lib." + new AssemblyName(e.Name).Name + ".dll";
            using var s = typeof(Program).Assembly.GetManifestResourceStream(name);
            if (s == null) { return null; }
            var bytes = new byte[s.Length];
            int read = 0;
            while (read < bytes.Length) { read += s.Read(bytes, read, bytes.Length - read); }
            return Assembly.Load(bytes);
        }
    }
}
