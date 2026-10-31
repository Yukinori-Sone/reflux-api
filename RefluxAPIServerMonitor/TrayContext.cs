using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Reflux.ServerMonitor
{
    /// <summary>What the monitor knows at one poll</summary>
    sealed class Snapshot
    {
        public ServiceControllerStatus? Service;
        public RefluxConfig Config;
        public Status Status;
        public string ApiError;
        public Dictionary<string, string> Latest;
        public DateTime At = DateTime.Now;

        public enum Level { Ok, Waiting, Error, Off }

        public Level State =>
            Status == null ? (Service == null ? Level.Off : Level.Error)
            : Status.AttachState == "ready" ? Level.Ok
            : Status.AttachState == "offsetMismatch" || Status.AttachState == "offsetSearch" ? Level.Error
            : Level.Waiting;

        public static string AttachStateJa(string s) => s switch
        {
            "waitingForProcess" => "INFINITAS 待ち",
            "checkingVersion" => "バージョン確認中",
            "offsetMismatch" => "オフセット不一致",
            "offsetSearch" => "対話式オフセット探索中",
            "loading" => "曲データ読込中",
            "ready" => "監視中",
            null => "-",
            _ => s,
        };

        public static string GameStateJa(string s) => s switch
        {
            "songSelect" => "選曲中",
            "playing" => "プレー中",
            "resultScreen" => "リザルト",
            null => "-",
            _ => s,
        };

        public static string ServiceJa(ServiceControllerStatus? s) => s switch
        {
            null => "未登録",
            ServiceControllerStatus.Running => "実行中",
            ServiceControllerStatus.Stopped => "停止",
            ServiceControllerStatus.StartPending => "開始中",
            ServiceControllerStatus.StopPending => "停止中",
            _ => s.ToString(),
        };

        /// <summary>One line for the tray tooltip (at most 63 characters)</summary>
        public string Summary
        {
            get
            {
                string text;
                if (Status != null)
                {
                    text = AttachStateJa(Status.AttachState) + (Status.AttachState == "ready" ? "（" + GameStateJa(Status.GameState) + "）" : "");
                }
                else if (Service == null) { text = "サービス未登録・Reflux 停止"; }
                else if (Service != ServiceControllerStatus.Running) { text = "サービス" + ServiceJa(Service); }
                else { text = "API が応答しません"; }
                text = "Reflux: " + text;
                return text.Length > 63 ? text.Substring(0, 63) : text;
            }
        }
    }

    /// <summary>Tray icon, polling and the menu</summary>
    sealed class TrayContext : ApplicationContext
    {
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Timer timer = new Timer { Interval = 1000 }; /* 1 s, 0.3 s while playing and on the result screen: the result screen screenshot starts from this poll */
        readonly Dictionary<Snapshot.Level, Icon> icons = new Dictionary<Snapshot.Level, Icon>();
        readonly ToolStripMenuItem miStart = new ToolStripMenuItem("サービスを開始");
        readonly ToolStripMenuItem miStop = new ToolStripMenuItem("サービスを停止");
        readonly ToolStripMenuItem miRestart = new ToolStripMenuItem("サービスを再起動");
        readonly ToolStripMenuItem miAutoStart = new ToolStripMenuItem("ログオン時にこのモニターを起動");
        StatusForm statusForm;
        SettingsForm settingsForm;
        bool polling;
        Snapshot.Level? lastLevel;

        public Snapshot Last { get; private set; }

        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);

        public TrayContext()
        {
            using (var s = typeof(TrayContext).Assembly.GetManifestResourceStream("icon.ico"))
            using (var baseIcon = new Icon(s, SystemInformation.SmallIconSize))
            {
                icons[Snapshot.Level.Ok] = WithDot(baseIcon, Color.FromArgb(0x2E, 0xB8, 0x4B));
                icons[Snapshot.Level.Waiting] = WithDot(baseIcon, Color.FromArgb(0xF0, 0xB0, 0x10));
                icons[Snapshot.Level.Error] = WithDot(baseIcon, Color.FromArgb(0xE0, 0x30, 0x30));
                icons[Snapshot.Level.Off] = WithDot(baseIcon, Color.Gray);
            }

            var menu = new ContextMenuStrip();
            var miStatus = new ToolStripMenuItem("状態を表示", null, (_, __) => ShowStatus()) { Font = new Font(SystemFonts.MenuFont, FontStyle.Bold) };
            menu.Items.Add(miStatus);
            menu.Items.Add(new ToolStripMenuItem("設定...", null, (_, __) => ShowSettings()));
            menu.Items.Add(new ToolStripSeparator());
            miStart.Click += async (_, __) => await ControlService("start");
            miStop.Click += async (_, __) => await ControlService("stop");
            miRestart.Click += async (_, __) => await ControlService("restart");
            menu.Items.AddRange(new ToolStripItem[] { miStart, miStop, miRestart, new ToolStripSeparator() });
            menu.Items.Add(new ToolStripMenuItem("Reflux のフォルダを開く", null, (_, __) => Open(RefluxFolder.Dir)));
            menu.Items.Add(new ToolStripMenuItem("スクリーンショットのフォルダを開く", null, (_, __) =>
            {
                Directory.CreateDirectory(ResultScreenshot.Folder);
                Open(ResultScreenshot.Folder);
            }));
            menu.Items.Add(new ToolStripMenuItem("log.txt を開く", null, (_, __) =>
            {
                if (File.Exists(RefluxFolder.LogPath)) { Open(RefluxFolder.LogPath); }
                else { MessageBox.Show("log.txt はまだありません（エラーが起きると作られます）", "Reflux API Server Monitor"); }
            }));
            menu.Items.Add(new ToolStripSeparator());
            miAutoStart.Click += (_, __) => { AutoStart.Enabled = !AutoStart.Enabled; miAutoStart.Checked = AutoStart.Enabled; };
            menu.Items.Add(miAutoStart);
            menu.Items.Add(new ToolStripMenuItem("モニターを終了", null, (_, __) => ExitThread()));
            menu.Opening += (_, __) =>
            {
                var s = Last?.Service;
                miStart.Enabled = s == ServiceControllerStatus.Stopped;
                miStop.Enabled = miRestart.Enabled = s == ServiceControllerStatus.Running;
                miAutoStart.Checked = AutoStart.Enabled;
            };

            tray.ContextMenuStrip = menu;
            tray.Icon = icons[Snapshot.Level.Off];
            tray.Text = "Reflux: 確認中";
            tray.Visible = true;
            tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) { ShowStatus(); } };
            timer.Tick += async (_, __) => await Poll();
            timer.Start();
            _ = Poll();
        }

        /// <summary>The Reflux icon with a coloured status dot in the lower right corner</summary>
        Icon WithDot(Icon baseIcon, Color color)
        {
            var size = SystemInformation.SmallIconSize;
            using var bmp = new Bitmap(size.Width, size.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawIcon(baseIcon, new Rectangle(Point.Empty, size));
                int d = (int)Math.Round(size.Width * 0.55);
                var r = new Rectangle(size.Width - d, size.Height - d, d - 1, d - 1);
                using var brush = new SolidBrush(color);
                g.FillEllipse(brush, r);
                using var pen = new Pen(Color.White, Math.Max(1f, size.Width / 16f));
                g.DrawEllipse(pen, r);
            }
            var handle = bmp.GetHicon();
            var icon = (Icon)Icon.FromHandle(handle).Clone();
            DestroyIcon(handle);
            return icon;
        }

        public async Task Poll()
        {
            if (polling) { return; }
            polling = true;
            try
            {
                var snap = await Task.Run(async () =>
                {
                    var s = new Snapshot { Service = ServiceControl.Status() };
                    try { s.Config = RefluxConfig.Read(); } catch { s.Config = new RefluxConfig(); }
                    if (s.Config.ApiEnabled)
                    {
                        using var api = new CoreApi(s.Config);
                        try
                        {
                            s.Status = await api.StatusAsync().ConfigureAwait(false);
                            s.Latest = await api.LatestAsync().ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            s.ApiError = (e.InnerException ?? e).Message;
                        }
                    }
                    else
                    {
                        s.ApiError = "config.ini の [Api] enabled が false です";
                    }
                    return s;
                });
                var previousGameState = Last?.Status?.GameState;
                Last = snap;
                ResultScreenshot.OnPoll(previousGameState, snap);
            timer.Interval = snap.Status?.GameState == "playing" || snap.Status?.GameState == "resultScreen" ? 300 : 1000; /* Catch the result screen soon after the core does */
                var level = snap.State;
                tray.Icon = icons[level];
                tray.Text = snap.Summary;
                if (lastLevel.HasValue && lastLevel != level && level == Snapshot.Level.Error)
                {
                    tray.ShowBalloonTip(5000, "Reflux API Server", snap.Summary + (snap.Status?.Message != null ? "\n" + snap.Status.Message : ""), ToolTipIcon.Warning);
                }
                lastLevel = level;
                statusForm?.ShowSnapshot(snap);
            }
            finally
            {
                polling = false;
            }
        }

        async Task ControlService(string action)
        {
            tray.Text = "Reflux: サービスを操作中...";
            var error = await Task.Run(() => ServiceControl.Run(action));
            if (error != null) { MessageBox.Show(error, "Reflux API Server Monitor", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            await Poll();
        }

        public void OpenStatus() => ShowStatus();
        public void OpenSettings() => ShowSettings();

        void ShowStatus()
        {
            if (statusForm == null || statusForm.IsDisposed)
            {
                statusForm = new StatusForm();
                statusForm.ShowSnapshot(Last);
            }
            statusForm.Show();
            statusForm.Activate();
        }

        void ShowSettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed)
            {
                settingsForm = new SettingsForm(this);
            }
            settingsForm.Show();
            settingsForm.Activate();
        }

        public Task<string> RestartServiceForSettings() => Task.Run(() => ServiceControl.Run("restart"));

        static void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception e) { MessageBox.Show(e.Message, "Reflux API Server Monitor"); }
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            tray.Visible = false;
            tray.Dispose();
            statusForm?.Close();
            settingsForm?.Close();
            base.ExitThreadCore();
        }
    }
}
