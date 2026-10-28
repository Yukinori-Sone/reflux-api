using System;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;

namespace Reflux.Api
{
    /// <summary>
    /// Running Reflux as a Windows service ("Reflux.exe --service", registered by Install-Service.bat), and making sure only one
    /// Reflux runs per folder (a service and a startup shortcut together would record every play twice and fight over the port).
    /// </summary>
    static class ServiceHost
    {
        public const string ServiceName = "RefluxAPIServer";

        [DllImport("kernel32.dll")] static extern bool AllocConsole();
        [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        static Mutex instanceMutex;

        /// <summary>True when started by the service control manager: Reflux's own Main then runs on a thread of the service</summary>
        public static bool RunIfService(string[] args, Action main)
        {
            if (args == null || Array.IndexOf(args, "--service") < 0) { return false; }
            ServiceBase.Run(new RefluxService(main));
            return true;
        }

        /// <summary>
        /// False when another Reflux already runs from the same folder. Call after the working folder is set to the exe's folder
        /// </summary>
        public static bool AcquireSingleInstance()
        {
            var folder = Path.GetFullPath(Environment.CurrentDirectory).TrimEnd('\\').ToLowerInvariant();
            var name = @"Global\Reflux_" + Convert.ToBase64String(System.Security.Cryptography.SHA1.Create()
                .ComputeHash(System.Text.Encoding.UTF8.GetBytes(folder))).Replace('/', '_').Replace('+', '-').TrimEnd('=');
            try
            {
                instanceMutex = new Mutex(true, name, out bool created);
                if (created) { return true; }
                /* The other one may have just exited (abandoned mutex): wait a little */
                try { if (instanceMutex.WaitOne(TimeSpan.FromSeconds(3))) { return true; } }
                catch (AbandonedMutexException) { return true; }
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                /* Held by a process of another account (e.g. the service as LocalSystem) */
                return false;
            }
        }

        sealed class RefluxService : ServiceBase
        {
            readonly Action main;

            public RefluxService(Action main)
            {
                this.main = main;
                ServiceName = Api.ServiceHost.ServiceName;
                CanStop = true;
                CanShutdown = true;
            }

            protected override void OnStart(string[] args)
            {
                /* A service has no console. Reflux clears and colours the console and would fail without one, so it gets a
                   hidden console of its own (in session 0 nobody would see it anyway) */
                AllocConsole();
                var window = GetConsoleWindow();
                if (window != IntPtr.Zero) { ShowWindow(window, 0); }
                var thread = new Thread(() =>
                {
                    try { main(); }
                    catch (Exception e) { Utils.Except(e, "Service"); }
                    /* Main only returns on errors: stop the service so the recovery settings restart it */
                    Environment.Exit(1);
                }) { IsBackground = true, Name = "Reflux main" };
                thread.Start();
            }

            protected override void OnStop() => ExitSoon();
            protected override void OnShutdown() => ExitSoon();

            /* Nothing to flush: every file is written right away. Let the stop be reported first, then end the process */
            static void ExitSoon() => new Thread(() => { Thread.Sleep(300); Environment.Exit(0); }) { IsBackground = true }.Start();
        }
    }
}
