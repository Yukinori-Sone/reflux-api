using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Reflux.ServerMonitor
{
    /// <summary>
    /// config.ini ([Api] and the stream / latest files, applied by restarting the service) and the core settings (through the
    /// API, applied at once)
    /// </summary>
    sealed class SettingsForm : Form
    {
        sealed class Choice
        {
            public object Value; public string Text;
            public Choice(object value, string text) { Value = value; Text = text; }
            public override string ToString() => Text;
        }

        readonly TrayContext tray;
        readonly ComboBox cbHost = Combo(220);
        readonly NumericUpDown numPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Width = 90 };
        readonly TextBox txtKey = new TextBox { Width = 250, UseSystemPasswordChar = true };
        readonly CheckBox chkShowKey = new CheckBox { Text = "表示", AutoSize = true };
        readonly Button btnGenerate = new Button { Text = "生成", AutoSize = true };
        readonly Button btnCopy = new Button { Text = "コピー", AutoSize = true };
        readonly CheckBox chkApi = new CheckBox { Text = "API を有効にする（Score Manager から見るのに必要）", AutoSize = true };
        readonly CheckBox chkLatestJson = new CheckBox { Text = "latest.json", AutoSize = true };
        readonly CheckBox chkLatestTxt = new CheckBox { Text = "latest.txt・latest-*.txt", AutoSize = true };
        readonly CheckBox chkPlayState = new CheckBox { Text = "playstate.txt", AutoSize = true };
        readonly CheckBox chkMarquee = new CheckBox { Text = "marquee.txt", AutoSize = true };
        readonly CheckBox chkSongInfo = new CheckBox { Text = "title.txt・artist.txt など（曲情報）", AutoSize = true };
        readonly CheckBox chkShot = new CheckBox { Text = "リザルト画面でスクリーンショットを撮る", AutoSize = true };
        readonly NumericUpDown numShotMax = new NumericUpDown { Minimum = 3, Maximum = 30, DecimalPlaces = 0, Increment = 1, Width = 70 };
        readonly ComboBox cbShotFormat = Combo(220);
        readonly ComboBox cbPoll = Combo(260), cbUnlock = Combo(260), cbTracker = Combo(260), cbPriority = Combo(260), cbSession = Combo(260);
        readonly Label lblCore = new Label { AutoSize = true, ForeColor = SystemColors.GrayText };
        readonly Label lblResult = new Label { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        readonly Button btnSave = new Button { Text = "保存して反映", AutoSize = true };
        readonly Button btnClose = new Button { Text = "閉じる", AutoSize = true };
        RefluxConfig loadedConfig;
        CoreSettingsValues loadedCore;

        static ComboBox Combo(int width) => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };

        public SettingsForm(TrayContext tray)
        {
            this.tray = tray;
            Text = "Reflux API Server の設定";
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Padding = new Padding(10);
            using (var s = typeof(SettingsForm).Assembly.GetManifestResourceStream("icon.ico")) { Icon = new Icon(s); }

            cbHost.Items.AddRange(new object[] { new Choice("localhost", "このPCだけ（localhost）"), new Choice("+", "LAN の別PCからも（+）") });
            cbPoll.Items.AddRange(new object[] { new Choice(500, "0.5秒"), new Choice(1000, "1秒"), new Choice(2000, "2秒（初期値・本家と同じ）"), new Choice(3000, "3秒"), new Choice(5000, "5秒") });
            cbUnlock.Items.AddRange(new object[] { new Choice(0, "毎回（初期値・本家と同じ）"), new Choice(10, "10秒ごと"), new Choice(30, "30秒ごと"), new Choice(60, "60秒ごと"), new Choice(-1, "選曲画面に戻ったときだけ") });
            cbTracker.Items.AddRange(new object[] { new Choice("always", "毎回書き直す（本家と同じ）"), new Choice("onChange", "変化があったときだけ"), new Choice("never", "書き出さない（初期値）") });
            cbPriority.Items.AddRange(new object[] { new Choice("normal", "通常（初期値・本家と同じ）"), new Choice("belowNormal", "通常以下"), new Choice("idle", "低") });
            cbSession.Items.AddRange(new object[] { new Choice("perStart", "Reflux の起動ごと（本家と同じ）"), new Choice("daily", "日ごと"), new Choice("monthly", "月ごと（初期値）"), new Choice("single", "1つのファイル"), new Choice("none", "出力しない") });
            chkShowKey.CheckedChanged += (_, __) => txtKey.UseSystemPasswordChar = !chkShowKey.Checked;
            btnGenerate.Click += (_, __) => GenerateKey();
            btnCopy.Click += (_, __) =>
            {
                if (txtKey.Text.Trim().Length == 0) { MessageBox.Show(this, "APIキーが空です", Text); return; }
                Clipboard.SetText(txtKey.Text.Trim());
                MessageBox.Show(this, "APIキーをクリップボードにコピーしました。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            root.Controls.Add(Group("API（config.ini。保存するとサービスを再起動して反映）", g =>
            {
                g.Controls.Add(chkApi); g.SetColumnSpan(chkApi, 2);
                Row(g, "公開範囲", cbHost);
                Row(g, "ポート", numPort);
                var keyPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                keyPanel.Controls.Add(txtKey); keyPanel.Controls.Add(btnGenerate); keyPanel.Controls.Add(btnCopy); keyPanel.Controls.Add(chkShowKey);
                Row(g, "APIキー", keyPanel);
                var note = new Label
                {
                    AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(520, 0),
                    Text = "ポートや APIキーを変えたら、Score Manager の接続設定も合わせてください。LAN に公開するポートを変えたときは、ファイアウォールでそのポートの受信を許可してください（サービスで動かしている場合、netsh の URL 予約は不要です）。",
                };
                g.Controls.Add(note); g.SetColumnSpan(note, 2);
            }));
            root.Controls.Add(Group("配信用・最新プレーのファイル出力（config.ini。保存するとサービスを再起動して反映）", g =>
            {
                foreach (var c in new Control[] { chkLatestJson, chkLatestTxt, chkPlayState, chkMarquee, chkSongInfo }) { g.Controls.Add(c); g.SetColumnSpan(c, 2); }
            }));
            cbShotFormat.Items.AddRange(new object[] { new Choice(false, "PNG（劣化なし、1枚 2〜4MB）"), new Choice(true, "JPEG（1枚 0.5MB 前後）") });
            root.Controls.Add(Group("リザルト画面のスクリーンショット（このモニターが撮る。保存するとすぐ反映）", g =>
            {
                g.Controls.Add(chkShot); g.SetColumnSpan(chkShot, 2);
                Row(g, "撮り続ける最長時間（秒）", numShotMax);
                Row(g, "形式", cbShotFormat);
                var shotNote = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(520, 0),
                    Text = "リザルト画面が出たら撮り続け、左の成績パネルの演出が止まった1枚を残します（止まらないまま画面を抜けたときは、抜ける直前の1枚）。" +
                           "Reflux のフォルダの screenshots に、プレーの記録と同じ日時・曲・譜面の名前で保存します。Score Manager のプレー履歴から開けます。" };
                g.Controls.Add(shotNote); g.SetColumnSpan(shotNote, 2);
            }));
            root.Controls.Add(Group("コアの動作設定（API で変更。保存するとすぐ反映）", g =>
            {
                Row(g, "監視間隔", cbPoll);
                Row(g, "解禁データの確認", cbUnlock);
                Row(g, "tracker.tsv の書き出し", cbTracker);
                Row(g, "Reflux の優先度", cbPriority);
                Row(g, "セッションTSVの単位", cbSession);
                g.Controls.Add(lblCore); g.SetColumnSpan(lblCore, 2);
            }));
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            buttons.Controls.Add(btnSave);
            buttons.Controls.Add(btnClose);
            root.Controls.Add(buttons);
            root.Controls.Add(lblResult);
            Controls.Add(root);

            btnSave.Click += async (_, __) => await Save();
            btnClose.Click += (_, __) => Close();
            Shown += async (_, __) => await LoadValues();
        }

        /// <summary>New random API key (32 hex characters), copied to the clipboard</summary>
        void GenerateKey()
        {
            if (txtKey.Text.Trim().Length > 0 &&
                MessageBox.Show(this, "今の APIキーを新しいキーに置き換えます。\n保存すると、今のキーを入れている Score Manager からは接続できなくなります。よろしいですか？",
                    Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK)
            {
                return;
            }
            var bytes = new byte[16];
            using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider()) { rng.GetBytes(bytes); }
            var key = string.Concat(bytes.Select(b => b.ToString("x2")));
            txtKey.Text = key;
            try { Clipboard.SetText(key); } catch (Exception e) { MessageBox.Show(this, "クリップボードにコピーできませんでした: " + e.Message, Text); return; }
            MessageBox.Show(this,
                "新しい APIキーを作り、クリップボードにコピーしました。\n\n" +
                "1. 「保存して反映」で Reflux に反映してください（サービスが再起動します）。\n" +
                "2. Reflux INFINITAS Score Manager の「APIキー」欄にも同じキーを貼り付けてください。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static GroupBox Group(string title, Action<TableLayoutPanel> fill)
        {
            var g = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8), MinimumSize = new Size(560, 0) };
            var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fill(t);
            g.Controls.Add(t);
            return g;
        }

        static void Row(TableLayoutPanel t, string name, Control c)
        {
            t.Controls.Add(new Label { Text = name, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 12, 6) });
            t.Controls.Add(c);
        }

        static void Select(ComboBox cb, object value)
        {
            var item = cb.Items.Cast<Choice>().FirstOrDefault(c => Equals(c.Value, value));
            if (item == null) { item = new Choice(value, value?.ToString()); cb.Items.Add(item); }
            cb.SelectedItem = item;
        }

        static object Value(ComboBox cb) => (cb.SelectedItem as Choice)?.Value;

        async Task LoadValues()
        {
            lblResult.Text = "";
            try
            {
                loadedConfig = RefluxConfig.Read();
            }
            catch (Exception e)
            {
                lblResult.Text = "config.ini を読めません: " + e.Message;
                lblResult.ForeColor = Color.Firebrick;
                btnSave.Enabled = false;
                return;
            }
            chkShot.Checked = ScreenshotSettings.Enabled;
            numShotMax.Value = Math.Max(3, Math.Min(30, ScreenshotSettings.MaxBurstMs / 1000));
            Select(cbShotFormat, ScreenshotSettings.Jpeg);
            chkApi.Checked = loadedConfig.ApiEnabled;
            Select(cbHost, loadedConfig.Host);
            numPort.Value = Math.Max(1, Math.Min(65535, loadedConfig.Port));
            txtKey.Text = loadedConfig.ApiKey;
            chkLatestJson.Checked = loadedConfig.LatestJson;
            chkLatestTxt.Checked = loadedConfig.LatestTxt;
            chkPlayState.Checked = loadedConfig.PlayState;
            chkMarquee.Checked = loadedConfig.Marquee;
            chkSongInfo.Checked = loadedConfig.FullSongInfo;

            loadedCore = null;
            foreach (var cb in new[] { cbPoll, cbUnlock, cbTracker, cbPriority, cbSession }) { cb.Enabled = false; }
            lblCore.Text = "コアから読み込み中...";
            try
            {
                using var api = new CoreApi(loadedConfig);
                var res = await api.SettingsAsync();
                loadedCore = res.Values;
                Select(cbPoll, loadedCore.PollIntervalMs);
                Select(cbUnlock, loadedCore.UnlockCheckSeconds);
                Select(cbTracker, loadedCore.TrackerTsv);
                Select(cbPriority, loadedCore.ProcessPriority);
                Select(cbSession, loadedCore.SessionFile ?? "perStart");
                foreach (var cb in new[] { cbPoll, cbUnlock, cbTracker, cbPriority, cbSession }) { cb.Enabled = true; }
                lblCore.Text = "保存先: Reflux のフォルダの coresettings.json";
            }
            catch (Exception e)
            {
                lblCore.Text = "Reflux の API に接続できないため変更できません（" + (e.InnerException ?? e).Message + "）";
            }
        }

        async Task Save()
        {
            btnSave.Enabled = false;
            lblResult.ForeColor = SystemColors.ControlText;
            try
            {
                var messages = new System.Collections.Generic.List<string>();

                bool shotJpeg = Value(cbShotFormat) is bool j && j;
                int shotMax = (int)numShotMax.Value * 1000;
                if (chkShot.Checked != ScreenshotSettings.Enabled || shotMax != ScreenshotSettings.MaxBurstMs || shotJpeg != ScreenshotSettings.Jpeg)
                {
                    ScreenshotSettings.Enabled = chkShot.Checked;
                    ScreenshotSettings.MaxBurstMs = shotMax;
                    ScreenshotSettings.Jpeg = shotJpeg;
                    messages.Add("スクリーンショットの設定を保存しました");
                }

                /* Core settings first: the API still answers on the current port */
                if (loadedCore != null)
                {
                    var core = new CoreSettingsValues
                    {
                        PollIntervalMs = (int)Value(cbPoll),
                        UnlockCheckSeconds = (int)Value(cbUnlock),
                        TrackerTsv = (string)Value(cbTracker),
                        ProcessPriority = (string)Value(cbPriority),
                        SessionFile = (string)Value(cbSession),
                    };
                    if (core.PollIntervalMs != loadedCore.PollIntervalMs || core.UnlockCheckSeconds != loadedCore.UnlockCheckSeconds ||
                        core.TrackerTsv != loadedCore.TrackerTsv || core.ProcessPriority != loadedCore.ProcessPriority || core.SessionFile != loadedCore.SessionFile)
                    {
                        using var api = new CoreApi(loadedConfig);
                        await api.PutSettingsAsync(core);
                        loadedCore = core;
                        messages.Add("コアの動作設定を反映しました");
                    }
                }

                var config = new RefluxConfig
                {
                    ApiEnabled = chkApi.Checked,
                    Host = (string)Value(cbHost) ?? "localhost",
                    Port = (int)numPort.Value,
                    ApiKey = txtKey.Text.Trim(),
                    LatestJson = chkLatestJson.Checked,
                    LatestTxt = chkLatestTxt.Checked,
                    PlayState = chkPlayState.Checked,
                    Marquee = chkMarquee.Checked,
                    FullSongInfo = chkSongInfo.Checked,
                };
                if (!config.SameAs(loadedConfig))
                {
                    var error = WriteConfig(config.Apply());
                    if (error != null) { throw new Exception("config.ini を保存できませんでした: " + error); }
                    loadedConfig = config;
                    if (ServiceControl.Status() == null)
                    {
                        messages.Add("config.ini を保存しました。Reflux を起動し直すと反映されます");
                    }
                    else
                    {
                        lblResult.Text = "サービスを再起動しています...";
                        var restartError = await tray.RestartServiceForSettings();
                        messages.Add(restartError == null ? "config.ini を保存してサービスを再起動しました" : "config.ini は保存しましたが、サービスを再起動できませんでした: " + restartError);
                    }
                }
                lblResult.Text = messages.Count == 0 ? "変更はありません" : string.Join("\n", messages) + $"（{DateTime.Now:HH:mm:ss}）";
                await tray.Poll();
            }
            catch (Exception e)
            {
                lblResult.Text = (e.InnerException ?? e).Message;
                lblResult.ForeColor = Color.Firebrick;
            }
            finally
            {
                btnSave.Enabled = true;
            }
        }

        /// <summary>Write config.ini, as administrator (UAC) when the folder isn't writable for the user</summary>
        static string WriteConfig(byte[] bytes)
        {
            try
            {
                File.WriteAllBytes(RefluxFolder.ConfigPath, bytes);
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                var temp = Path.Combine(Path.GetTempPath(), $"reflux-config-{Guid.NewGuid():N}.ini");
                File.WriteAllBytes(temp, bytes);
                try { return Elevated.Run($"--write-config \"{temp}\""); }
                finally { try { File.Delete(temp); } catch { } }
            }
        }
    }
}
