using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Reflux.ServerMonitor
{
    static class Program
    {
        /// <summary>
        /// Normal start: tray monitor (one per user session). --status / --settings also open that window at start.
        /// Elevated helpers started by the monitor itself: --service start|stop|restart, --write-config &lt;file&gt;
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--service")
            {
                try { ServiceControl.Do(args[1]); return 0; }
                catch (Exception e) { MessageBox.Show(e.Message, "Reflux API Server Monitor"); return 1; }
            }
            if (args.Length >= 2 && args[0] == "--write-config")
            {
                try { File.Copy(args[1], RefluxFolder.ConfigPath, true); return 0; }
                catch (Exception e) { MessageBox.Show(e.Message, "Reflux API Server Monitor"); return 1; }
            }

            using var mutex = new Mutex(true, @"Local\RefluxAPIServerMonitor", out bool first);
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
    }
}
