using System;
using System.Collections.Generic;
using System.Drawing;
using System.ServiceProcess;
using System.Windows.Forms;

namespace Reflux.ServerMonitor
{
    /// <summary>State of the service and the core, refreshed at every poll while open</summary>
    sealed class StatusForm : Form
    {
        readonly TableLayoutPanel grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(10) };
        readonly Dictionary<string, Label> values = new Dictionary<string, Label>();
        readonly TextBox log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9f) };
        readonly Label logTitle = new Label { Text = "log.txt の最後（エラーの記録）", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 6, 0, 4) };
        static readonly string[] Rows =
        {
            "サービス", "Reflux", "INFINITAS", "ゲーム", "プレー中の譜面", "INFINITAS ID", "バージョン", "API", "今回の記録", "直近のプレー", "スクリーンショット", "メッセージ", "最終確認",
        };

        public StatusForm()
        {
            Text = "Reflux API Server の状態";
            Width = 760;
            Height = 620;
            StartPosition = FormStartPosition.CenterScreen;
            using (var s = typeof(StatusForm).Assembly.GetManifestResourceStream("icon.ico")) { Icon = new Icon(s); }
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var r in Rows)
            {
                grid.Controls.Add(new Label { Text = r, AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(0, 3, 12, 3) });
                var v = new Label { AutoSize = true, Padding = new Padding(0, 3, 0, 3), MaximumSize = new Size(560, 0) };
                values[r] = v;
                grid.Controls.Add(v);
            }
            Controls.Add(log);
            Controls.Add(logTitle);
            Controls.Add(grid);
            /* Closing just hides it, the tray keeps running */
            FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        }

        static string IdText(string id) =>
            id != null && id.Length == 13 && id[0] == 'C' ? $"C-{id.Substring(1, 4)}-{id.Substring(5, 4)}-{id.Substring(9, 4)}" : id ?? "-";

        void Set(string row, string text, Color? color = null)
        {
            values[row].Text = string.IsNullOrEmpty(text) ? "-" : text;
            values[row].ForeColor = color ?? SystemColors.ControlText;
        }

        public void ShowSnapshot(Snapshot s)
        {
            if (s == null) { return; }
            var green = Color.ForestGreen;
            var red = Color.Firebrick;
            var orange = Color.DarkOrange;
            Set("サービス", Snapshot.ServiceJa(s.Service) + (s.Service == null ? "（Service-Install.bat で登録できます）" : "（RefluxAPIServer）"),
                s.Service == ServiceControllerStatus.Running ? green : s.Service == null ? (Color?)null : red);
            var st = s.Status;
            Set("Reflux", st != null ? "応答あり" : "応答なし: " + s.ApiError, st != null ? green : red);
            Set("INFINITAS", st == null ? null : Snapshot.AttachStateJa(st.AttachState) + (st.ProcessId.HasValue ? $"（PID {st.ProcessId}）" : ""),
                st == null ? (Color?)null : st.AttachState == "ready" ? green : s.State == Snapshot.Level.Error ? red : orange);
            Set("ゲーム", Snapshot.GameStateJa(st?.GameState));
            Set("プレー中の譜面", st?.GameState == "playing" && st.CurrentChart != null ? $"{st.CurrentChart.Title} [{st.CurrentChart.Difficulty}]" : null);
            Set("INFINITAS ID", st?.InfinitasId == null ? null : IdText(st.InfinitasId));
            Set("バージョン", st == null ? null : $"Reflux {st.RefluxVersion} / ゲーム {st.GameVersion ?? "-"} / オフセット {st.OffsetsVersion ?? "-"}");
            var c = s.Config;
            Set("API", c == null ? null : (c.ApiEnabled ? "有効" : "無効") + $"　{(c.Host == "+" ? "LAN に公開" : c.Host == "localhost" ? "このPCだけ" : c.Host)}　ポート {c.Port}　APIキー {(string.IsNullOrEmpty(c.ApiKey) ? "なし" : "あり")}");
            Set("今回の記録", st == null ? null : $"{st.SessionPlays} プレー（Reflux の起動以降）");
            string L(string k) => s.Latest != null && s.Latest.TryGetValue(k, out var v) ? v : null;
            Set("直近のプレー", s.Latest == null ? null : $"{L("title")} [{L("diff")}]  {L("lamp")}  EX {L("exscore")}  {(L("timestamp") is string t && DateTime.TryParse(t, out var dt) ? dt.ToLocalTime().ToString("MM/dd HH:mm") : "")}");
            Set("スクリーンショット", !ScreenshotSettings.Enabled ? "撮らない" : ResultScreenshot.LastSaved == null ? "リザルト画面で撮る（まだ撮っていない）" : "最後: " + System.IO.Path.GetFileName(ResultScreenshot.LastSaved));
            Set("メッセージ", st?.Message, st?.Message != null ? red : (Color?)null);
            Set("最終確認", s.At.ToString("HH:mm:ss"));
            if (Visible)
            {
                var tail = RefluxFolder.LogTail(40);
                if (log.Text != tail) { log.Text = tail; log.SelectionStart = log.Text.Length; log.ScrollToCaret(); }
            }
        }
    }
}
