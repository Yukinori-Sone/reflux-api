using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RefluxAPIManager
{
    /// <summary>Screenshot settings (RefluxAPIManager.json, see ManagerSettings). The setters change the values in memory; ManagerSettings.Save writes them</summary>
    static class ScreenshotSettings
    {
        static ManagerSettings S => ManagerSettings.Current;

        public static bool Enabled { get => S.ScreenshotEnabled; set => S.ScreenshotEnabled = value; }
        /// <summary>Longest burst on the result screen before a frame is picked anyway, milliseconds</summary>
        public static int MaxBurstMs { get => S.ScreenshotMaxBurstMs; set => S.ScreenshotMaxBurstMs = value; }
        public static bool Jpeg { get => S.ScreenshotJpeg; set => S.ScreenshotJpeg = value; }
        /// <summary>zlib level of the PNG: 6 (standard, 1.8 MB in 1 s) or 9 (3 % smaller, 6 s on the dev PC)</summary>
        public static int PngLevel { get => S.ScreenshotPngLevel >= 9 ? 9 : 6; set => S.ScreenshotPngLevel = value; }
        /// <summary>JPEG quality, 50-100</summary>
        public static int JpegQuality { get => Math.Max(50, Math.Min(100, S.ScreenshotJpegQuality)); set => S.ScreenshotJpegQuality = value; }
        /// <summary>Tuning aid (file only): keep every frame of the burst in screenshots\burst\ with the differences in capture.log</summary>
        public static bool KeepBurst => S.ScreenshotKeepBurst;

        /// <summary>
        /// Kinds of result (the core's resultKind, plus "unrecorded" when no play record came) with their label and whether the
        /// screenshot is kept by default (user's choice)
        /// </summary>
        public static readonly (string kind, string label, bool keep)[] Kinds =
        {
            ("first", "完走・クリア（初回）", true),
            ("pb", "完走・クリア（自己ベスト更新あり）", true),
            ("nopb", "完走・クリア（自己ベスト更新なし）", false),
            ("failed", "完走・FAILED", true),
            ("dead", "途中落ち（HARD / EX HARD / 段位）", true),
            ("quit", "自分で閉じた", false),
            ("idle", "放置で落ちた（50ノーツ以上叩かなかった）", false),
            ("unrecorded", "プレーの記録が取れなかったもの", false),
        };

        /// <summary>Keep the screenshot of this kind of result. Unknown kinds (a core newer than this monitor) are kept</summary>
        public static bool Keep(string kind)
        {
            var k = Kinds.FirstOrDefault(x => x.kind == kind);
            return k.kind == null || (S.ScreenshotKeep.TryGetValue(kind, out var keep) ? keep : k.keep);
        }

        public static void SetKeep(string kind, bool keep) => S.ScreenshotKeep[kind] = keep;
    }

    /// <summary>
    /// Takes the screenshot of the result screen and saves it as screenshots\&lt;play time UTC&gt;_&lt;song id&gt;_&lt;difficulty&gt;.png,
    /// the key the core and the Score Manager use to find the screenshot of a play. The core can't take it itself when it runs as a
    /// service (no desktop in session 0).
    ///
    /// What the game shows after a play (seen on 2026101400 in fullscreen): the core already reports the result screen while the
    /// game shows a black loading screen for 2-3 s, then the result screen draws itself with an animation (clear type, FAILED /
    /// CLEAR over the middle) for 1.5-2 s and stays still apart from the background. Leaving it brings a blue INFINITAS screen
    /// that the core still reports as the result screen for a moment. How long each part takes varies, so instead of waiting a
    /// fixed time the monitor takes a frame every 0.15 s, compares it with the frame of 0.3 s before and keeps:
    ///  - the first frame after the score panel on the left and the middle both stopped changing for 0.3 s (thresholds tuned on
    ///    frames 0.3 s apart); the background keeps moving all the time, so only those parts are compared
    ///  - when the player left before that: the last frame where the panel was still, else the result frame that changed least
    /// A frame counts as the result screen when enough of the panel area is the gray of its boxes (0.14-0.29 on result screens,
    /// also with bright CLEAR backgrounds; 0.09 or less on the loading screen and the blue screen after it). The first frame that
    /// isn't, after a result frame, means the player has left.
    /// </summary>
    static class ResultScreenshot
    {
        /// <summary>screenshots\ in the Reflux folder (the core serves them from there)</summary>
        public static string Folder => Path.Combine(RefluxFolder.Dir, "screenshots");
        public static string LastSaved { get; private set; }
        static string lastPlayTimestamp;
        static volatile string gameState;
        static bool busy;

        const int FrameIntervalMs = 150;
        /// <summary>How far back the frame compared with is (the thresholds were tuned on frames this far apart)</summary>
        const int CompareMs = 300;
        /// <summary>How long panel and middle have to stay still before a frame is picked</summary>
        const int StillForMs = 300;
        /// <summary>Score panel (clear type .. judges) on a 1920x1080 result screen, scaled for other sizes</summary>
        static readonly Rectangle Panel = new Rectangle(0, 380, 560, 630);
        /// <summary>Middle of the screen where FAILED / CLEAR is shown during the animation</summary>
        static readonly Rectangle Middle = new Rectangle(600, 420, 720, 200);
        /// <summary>Share of gray pixels in the panel area from which a frame is the result screen</summary>
        const double ResultGrayShare = 0.12;
        /// <summary>Mean difference (0-255) between two frames below which a part counts as not changing (still result: under 1)</summary>
        const double StillBelow = 3.0;
        /// <summary>Panel difference above which the game has left the result screen, once the panel was still</summary>
        const double LeftAbove = 30;

        /// <summary>File name (without extension) for a play: 20261015T063424Z_28054_DPA</summary>
        public static string KeyOf(Dictionary<string, string> play)
        {
            if (play == null || !play.TryGetValue("timestamp", out var ts) || !play.TryGetValue("songid", out var song) || !play.TryGetValue("diff", out var diff)) { return null; }
            if (!DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)) { return null; }
            return $"{t:yyyyMMdd'T'HHmmss'Z'}_{song}_{diff}";
        }

        /// <summary>Called at every poll: starts a capture when the game state turns into the result screen</summary>
        public static void OnPoll(string previousGameState, Snapshot snap)
        {
            var now = snap.Status?.GameState;
            gameState = now;
            if (now != previousGameState) { Log($"game state {previousGameState ?? "-"} -> {now ?? "-"}" + (snap.ApiError != null ? " (" + snap.ApiError + ")" : "")); }
            if (!ScreenshotSettings.Enabled || busy || snap.Status == null) { return; }
            if (now != "resultScreen" || previousGameState == "resultScreen" || previousGameState == null) { return; }
            busy = true;
            Log("result screen: capturing");
            var since = DateTime.UtcNow.AddSeconds(-20);
            Task.Run(async () =>
            {
                try { await Capture(since).ConfigureAwait(false); }
                catch (Exception e) { Log("error: " + e.Message); }
                finally { busy = false; }
            });
        }

        static async Task Capture(DateTime since)
        {
            using var bmp = CaptureResult(out var how);
            if (bmp == null) { Log("nothing captured: " + how); return; }
            Log($"picked {bmp.Width}x{bmp.Height}: {how}, waiting for the play record");

            /* Reflux records the play a moment after the result screen appears: wait for it (up to 15 seconds) */
            string key = null, kind = null;
            using (var api = new CoreApi())
            {
                for (int i = 0; i < 15 && key == null; i++)
                {
                    try
                    {
                        var latest = await api.LatestAsync().ConfigureAwait(false);
                        if (latest != null && latest.TryGetValue("timestamp", out var ts) && ts != lastPlayTimestamp &&
                            DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) && t >= since)
                        {
                            key = KeyOf(latest);
                            latest.TryGetValue("resultKind", out kind); /* Missing from older cores: kept */
                            lastPlayTimestamp = ts;
                            break;
                        }
                    }
                    catch { /* Core busy or restarting: try again */ }
                    await Task.Delay(1000).ConfigureAwait(false);
                }
            }
            if (key == null)
            {
                key = $"{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}_unrecorded"; /* Quick retry, or a play Reflux couldn't read */
                kind = "unrecorded";
            }
            if (kind != null && !ScreenshotSettings.Keep(kind))
            {
                Log($"{key}: not saved ({kind}, off in the settings)");
                return;
            }

            Directory.CreateDirectory(Folder);
            var jpeg = ScreenshotSettings.Jpeg;
            var path = Path.Combine(Folder, key + (jpeg ? ".jpg" : ".png"));
            var sw = Stopwatch.StartNew();
            var quality = ScreenshotSettings.JpegQuality;
            var level = ScreenshotSettings.PngLevel;
            var tmp = path + ".tmp"; /* The core lists only finished files */
            var thread = Thread.CurrentThread;
            var priority = thread.Priority;
            thread.Priority = ThreadPriority.BelowNormal; /* Don't compete with the game on the next song select */
            try
            {
                if (jpeg) { SaveJpeg(bmp, tmp, quality); }
                else { PngWriter.Save(bmp, tmp, level); }
            }
            finally { thread.Priority = priority; }
            if (File.Exists(path)) { File.Delete(path); }
            File.Move(tmp, path);
            LastSaved = path;
            Log($"{Path.GetFileName(path)} {bmp.Width}x{bmp.Height} {(jpeg ? "quality " + quality : "level " + level)} {new FileInfo(path).Length / 1024}KB in {sw.ElapsedMilliseconds}ms, brightness {Brightness(bmp):0.0}");
        }

        /// <summary>The screen INFINITAS is on (the primary screen when its window isn't found)</summary>
        static Screen GameScreen()
        {
            var game = Process.GetProcessesByName("bm2dx").FirstOrDefault();
            return game != null && game.MainWindowHandle != IntPtr.Zero ? Screen.FromHandle(game.MainWindowHandle) : Screen.PrimaryScreen;
        }

        /// <summary>
        /// Burst through Desktop Duplication until the panel settles, the result screen is left or the time is up. A plain screen
        /// copy (GDI) after a fixed wait when duplication can't be used (it gets only black in exclusive fullscreen).
        /// </summary>
        static Bitmap CaptureResult(out string how)
        {
            var screen = GameScreen();
            DesktopDuplication dd;
            try { dd = new DesktopDuplication(screen.DeviceName); }
            catch (Exception e)
            {
                Log("desktop duplication failed, using GDI after 5 s: " + e.Message);
                Thread.Sleep(5000);
                how = screen.DeviceName + " GDI";
                return CaptureGdi(screen);
            }

            var keep = ScreenshotSettings.KeepBurst;
            string burstDir = null;
            if (keep)
            {
                burstDir = Path.Combine(Folder, "burst", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(burstDir);
            }
            var start = DateTime.UtcNow;
            var max = TimeSpan.FromMilliseconds(Math.Max(3000, ScreenshotSettings.MaxBurstMs));
            Bitmap picked = null, lastStill = null, calmest = null, last = null;
            string pickedHow = null, lastStillHow = null, calmestHow = null, end = "time up";
            double calmestDiff = double.MaxValue;
            var history = new List<(int ms, byte[] panel, byte[] middle)>(); /* Result frames of the last CompareMs, for the comparison */
            bool panelWasStill = false;
            int stillSince = -1, n = 0;
            using (dd)
            {
                while (DateTime.UtcNow - start < max)
                {
                    if (n > 0 && gameState != "resultScreen") { end = "the core left the result screen (" + (gameState ?? "-") + ")"; break; }
                    var t0 = DateTime.UtcNow;
                    var bmp = dd.Next(out var reason);
                    var ms = (int)(DateTime.UtcNow - start).TotalMilliseconds;
                    if (bmp == null) { if (keep) { Log($"  +{ms}ms {reason}"); } continue; } /* Nothing presented (a still screen): try again */
                    var panel = Sample(bmp, Panel);
                    var middle = Sample(bmp, Middle);
                    var gray = GrayShare(bmp, Panel);
                    bool isResult = gray >= ResultGrayShare;
                    int refAt = history.FindLastIndex(h => h.ms <= ms - CompareMs + FrameIntervalMs / 3);
                    double dPanel = refAt < 0 ? -1 : Diff(history[refAt].panel, panel);
                    double dMiddle = refAt < 0 ? -1 : Diff(history[refAt].middle, middle);
                    if (keep)
                    {
                        SaveJpeg(bmp, Path.Combine(burstDir, $"{n:D2}_{ms:D5}.jpg"), 85);
                        Log($"  frame {n:D2} +{ms}ms gray {gray:0.00} panel {dPanel:0.00} middle {dMiddle:0.00}");
                    }
                    n++;
                    if (!isResult && last != null)
                    {
                        end = $"left the result screen at frame {n - 1}";
                        bmp.Dispose();
                        break;
                    }
                    if (!isResult)
                    {
                        history.Clear(); /* Loading screen: the first result frame has nothing to compare with */
                        bmp.Dispose();
                    }
                    else if (panelWasStill && dPanel > LeftAbove)
                    {
                        end = $"left the result screen at frame {n - 1}";
                        bmp.Dispose();
                        break;
                    }
                    else
                    {
                        bool panelStill = dPanel >= 0 && dPanel < StillBelow;
                        if (panelStill)
                        {
                            panelWasStill = true;
                            lastStill?.Dispose();
                            lastStill = (Bitmap)bmp.Clone();
                            lastStillHow = $"frame {n - 1} at +{ms}ms";
                        }
                        if (dPanel >= 0 && dPanel <= calmestDiff)
                        {
                            calmestDiff = dPanel;
                            calmest?.Dispose();
                            calmest = (Bitmap)bmp.Clone();
                            calmestHow = $"frame {n - 1} at +{ms}ms, panel change {dPanel:0.00}";
                        }
                        /* Still since the frame compared with: the old rule (two still frames 0.3 s apart) is still for 0.3 s here */
                        if (panelStill && dMiddle >= 0 && dMiddle < StillBelow) { if (stillSince < 0) { stillSince = history[refAt].ms; } }
                        else { stillSince = -1; }
                        if (picked == null && stillSince >= 0 && ms - stillSince >= CompareMs + StillForMs - FrameIntervalMs / 3)
                        {
                            picked = (Bitmap)bmp.Clone();
                            pickedHow = $"frame {n - 1} at +{ms}ms, still for {ms - stillSince}ms";
                            if (!keep) { bmp.Dispose(); break; } /* The burst goes on only to keep frames for tuning */
                        }
                        history.Add((ms, panel, middle));
                        history.RemoveAll(h => h.ms < ms - CompareMs - FrameIntervalMs);
                        last?.Dispose();
                        last = bmp;
                    }
                    var wait = FrameIntervalMs - (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                    if (wait > 0) { Thread.Sleep(wait); }
                }
            }
            if (keep) { Log($"  burst ended: {end}, {n} frames in {burstDir}"); }
            Bitmap result;
            if (picked == null && gameState == "playing")
            {
                /* Quick retry: the core saw the result screen only for a moment and the frames show the next play */
                foreach (var b in new[] { lastStill, calmest, last }) { b?.Dispose(); }
                how = "quick retry (back to playing), nothing to keep";
                return null;
            }
            if (picked != null) { result = picked; how = "DDA " + pickedHow; }
            else if (lastStill != null) { result = lastStill; how = $"DDA not settled ({end}), last still panel: {lastStillHow}"; }
            else if (calmest != null) { result = calmest; how = $"DDA not settled ({end}), least changing: {calmestHow}"; }
            else { result = last; how = $"DDA not settled ({end}), the only result frame"; }
            foreach (var b in new[] { picked, lastStill, calmest, last }) { if (b != null && b != result) { b.Dispose(); } }
            how = screen.DeviceName + " " + how;
            if (result == null) { how += " (only the loading screen)"; }
            return result;
        }

        static Bitmap CaptureGdi(Screen screen)
        {
            var b = screen.Bounds;
            var bmp = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp)) { g.CopyFromScreen(b.Location, Point.Empty, b.Size, CopyPixelOperation.SourceCopy); }
            return bmp;
        }

        /// <summary>Gray values of a grid of pixels in a region given on 1920x1080 (scaled to the frame size)</summary>
        static byte[] Sample(Bitmap bmp, Rectangle r1080)
        {
            double sx = bmp.Width / 1920.0, sy = bmp.Height / 1080.0;
            var r = Rectangle.Intersect(new Rectangle((int)(r1080.X * sx), (int)(r1080.Y * sy), (int)(r1080.Width * sx), (int)(r1080.Height * sy)), new Rectangle(0, 0, bmp.Width, bmp.Height));
            int step = Math.Max(1, (int)(4 * sx));
            var values = new List<byte>();
            var bd = bmp.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                unsafe
                {
                    for (int y = 0; y < r.Height; y += step)
                    {
                        byte* row = (byte*)bd.Scan0 + (long)y * bd.Stride;
                        for (int x = 0; x < r.Width; x += step)
                        {
                            byte* p = row + x * 3;
                            values.Add((byte)((p[0] + p[1] * 2 + p[2]) / 4));
                        }
                    }
                }
            }
            finally { bmp.UnlockBits(bd); }
            return values.ToArray();
        }

        /// <summary>Share of near-gray, not dark pixels in a region given on 1920x1080 (the boxes of the result screen's panel)</summary>
        static double GrayShare(Bitmap bmp, Rectangle r1080)
        {
            double sx = bmp.Width / 1920.0, sy = bmp.Height / 1080.0;
            var r = Rectangle.Intersect(new Rectangle((int)(r1080.X * sx), (int)(r1080.Y * sy), (int)(r1080.Width * sx), (int)(r1080.Height * sy)), new Rectangle(0, 0, bmp.Width, bmp.Height));
            int step = Math.Max(1, (int)(4 * sx));
            int gray = 0, n = 0;
            var bd = bmp.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                unsafe
                {
                    for (int y = 0; y < r.Height; y += step)
                    {
                        byte* row = (byte*)bd.Scan0 + (long)y * bd.Stride;
                        for (int x = 0; x < r.Width; x += step)
                        {
                            byte* p = row + x * 3;
                            int max = Math.Max(p[0], Math.Max(p[1], p[2])), min = Math.Min(p[0], Math.Min(p[1], p[2]));
                            if (max - min < 24 && max > 60) { gray++; }
                            n++;
                        }
                    }
                }
            }
            finally { bmp.UnlockBits(bd); }
            return n == 0 ? 0 : (double)gray / n;
        }

        /// <summary>Mean absolute difference (0-255)</summary>
        static double Diff(byte[] a, byte[] b)
        {
            if (a.Length != b.Length || a.Length == 0) { return 255; }
            long sum = 0;
            for (int i = 0; i < a.Length; i++) { sum += Math.Abs(a[i] - b[i]); }
            return (double)sum / a.Length;
        }

        static void SaveJpeg(Bitmap bmp, string path, long quality)
        {
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var p = new EncoderParameters(1);
            p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
            bmp.Save(path, codec, p);
        }

        /// <summary>Average brightness (0-255) of a grid of pixels: tells a black capture from a real one in the log</summary>
        static double Brightness(Bitmap bmp)
        {
            double sum = 0; int n = 0;
            for (int y = 0; y < bmp.Height; y += Math.Max(1, bmp.Height / 40))
            {
                for (int x = 0; x < bmp.Width; x += Math.Max(1, bmp.Width / 40))
                {
                    var c = bmp.GetPixel(x, y);
                    sum += (c.R + c.G + c.B) / 3.0; n++;
                }
            }
            return n == 0 ? 0 : sum / n;
        }

        static readonly object logSync = new object();

        static void Log(string line)
        {
            try
            {
                lock (logSync)
                {
                    var folder = Folder;
                    Directory.CreateDirectory(folder);
                    File.AppendAllText(Path.Combine(folder, "capture.log"), $"{DateTime.Now:yyyy/MM/dd HH:mm:ss.fff} {line}\r\n");
                }
            }
            catch { }
        }
    }
}
