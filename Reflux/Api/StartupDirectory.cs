using System;
using System.Diagnostics;
using System.IO;

namespace Reflux.Api
{
    /// <summary>
    /// Reflux reads and writes config.ini, offsets.txt, tracker.db, sessions/ etc. relative to the current directory.
    /// Started from a shortcut with another working folder, a bat in another folder or a scheduled task, it would run
    /// with empty settings somewhere else, so the current directory is set to the folder of Reflux.exe first.
    /// </summary>
    static class StartupDirectory
    {
        public static void Apply()
        {
            try
            {
                var dir = ExeDirectory();
                if (dir == null || !Directory.Exists(dir)) { return; }
                if (!string.Equals(Path.GetFullPath(Environment.CurrentDirectory).TrimEnd('\\'), Path.GetFullPath(dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Working folder changed to {dir} (was {Environment.CurrentDirectory})");
                    Environment.CurrentDirectory = dir;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Could not change the working folder: {e.Message}");
            }
        }

        static string ExeDirectory()
        {
            /* Single file publish (netcoreapp3.1) runs from a temporary extraction folder: AppContext.BaseDirectory points
               there, the process's main module is the real Reflux.exe. Under "dotnet Reflux.dll" the main module is dotnet.exe */
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (exe != null && !string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(exe);
            }
            return AppContext.BaseDirectory;
        }
    }
}
