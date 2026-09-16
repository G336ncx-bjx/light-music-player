using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace LightMusic
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                LogCrash(e.ExceptionObject as Exception);
            };

            if (args.Length > 0 && args[0] == "--selftest")
            {
                AttachConsole();
                Environment.Exit(SelfTest.Run());
                return;
            }
            if (args.Length > 0 && args[0] == "--shot")
            {
                AttachConsole();
                int code = 1;
                try
                {
                    code = ShotMode.Run(args);
                }
                catch (Exception ex)
                {
                    LogCrash(ex);
                    Console.WriteLine("shot failed: " + ex.Message);
                }
                Environment.Exit(code);
                return;
            }
            if (args.Length > 0 && args[0] == "--smoke")
            {
                AttachConsole();
                Environment.Exit(SmokeRun(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--lockcheck")
            {
                AttachConsole();
                Environment.Exit(LockCheck());
                return;
            }
            if (args.Length > 0 && args[0] == "--streamtest")
            {
                AttachConsole();
                Environment.Exit(StreamTest(args.Length > 1 ? args[1] : null));
                return;
            }
            if (args.Length > 0 && args[0] == "--cloudtest")
            {
                AttachConsole();
                Environment.Exit(CloudTest(args.Length > 1 ? args[1] : null));
                return;
            }
            if (args.Length > 0 && args[0] == "--uploadtest")
            {
                AttachConsole();
                Environment.Exit(UploadTest(args.Length > 1 ? args[1] : null,
                    args.Length > 2 ? args[2] : null));
                return;
            }
            if (args.Length > 0 && args[0] == "--clouddelete")
            {
                AttachConsole();
                Environment.Exit(CloudDelete(args.Length > 1 ? args[1] : null,
                    args.Length > 2 ? args[2] : null));
                return;
            }
            if (args.Length > 0 && args[0] == "--uploadall")
            {
                AttachConsole();
                Environment.Exit(UploadAll(args.Length > 1 ? args[1] : null,
                    args.Length > 2 ? args[2] : null));
                return;
            }

            bool createdNew;
            Mutex mutex = new Mutex(true, "LightMusicPlayer_SingleInstance", out createdNew);
            if (!createdNew)
            {
                FocusRunningInstance();
                return;
            }

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                LogCrash(e.Exception);
            };
            Theme.EnsureStyles();

            MainWindow window = new MainWindow();
            app.MainWindow = window;
            app.Run(window);

            GC.KeepAlive(mutex);
        }

        private static void LogCrash(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LightMusic-error.log");
                System.IO.File.AppendAllText(path,
                    DateTime.Now.ToString("s") + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine,
                    System.Text.Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_EXSTYLE_LOCAL = -20;
        private const int WS_EX_TRANSPARENT_LOCAL = 0x00000020;
        private const int WS_EX_TOOLWINDOW_LOCAL = 0x00000080;
        private const int WS_EX_NOACTIVATE_LOCAL = 0x08000000;

        /// <summary>
        /// 验证桌面歌词「锁定」是否真的鼠标穿透：
        /// 用 WindowFromPoint 检查窗口中心那一像素究竟命中了哪个窗口。
        /// </summary>
        private static int LockCheck()
        {
            StringBuilder report = new StringBuilder();
            try
            {
                MainWindow.Headless = true;
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Theme.EnsureStyles();

                MainWindow main = new MainWindow();
                bool original = main.Settings.LyricLocked;
                main.SetLyricLocked(false, false);
                main.ShowDesktopLyrics(true);
                DesktopLyricsWindow lyrics = main.DesktopLyrics;
                if (lyrics == null)
                {
                    Report(report, "lockcheck failed: desktop lyrics window not created");
                    return 1;
                }
                Pump(0.8);

                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(lyrics).Handle;
                if (handle == IntPtr.Zero)
                {
                    Report(report, "lockcheck failed: no window handle");
                    return 1;
                }

                bool hitUnlocked = HitTest(handle);
                int styleUnlocked = GetWindowLong(handle, GWL_EXSTYLE_LOCAL);

                main.SetLyricLocked(true, false);
                Pump(0.5);
                bool hitLocked = HitTest(handle);
                int styleLocked = GetWindowLong(handle, GWL_EXSTYLE_LOCAL);

                // 锁定时应出现一个始终可点的「解锁」按钮窗口
                bool unlockClickable = false;
                bool unlockVisible = false;
                LyricsUnlockWindow unlock = lyrics.UnlockButton;
                if (unlock != null)
                {
                    IntPtr unlockHandle = new System.Windows.Interop.WindowInteropHelper(unlock).Handle;
                    int unlockStyle = GetWindowLong(unlockHandle, GWL_EXSTYLE_LOCAL);
                    unlockClickable = (unlockStyle & WS_EX_TRANSPARENT_LOCAL) == 0 && HitTest(unlockHandle);
                    unlockVisible = unlock.IsVisible;
                }

                // 鼠标靠近才显示 / 离开后自动隐藏
                POINT origin;
                GetCursorPos(out origin);
                RECT lyricRect;
                GetWindowRect(handle, out lyricRect);
                SetCursorPos((lyricRect.Left + lyricRect.Right) / 2, (lyricRect.Top + lyricRect.Bottom) / 2);
                Pump(0.8);
                bool shownWhenNear = lyrics.UnlockButton != null && lyrics.UnlockButton.IsVisible;
                SetCursorPos(4, 4);
                Pump(2.2);
                bool hiddenWhenFar = lyrics.UnlockButton == null || !lyrics.UnlockButton.IsVisible;
                SetCursorPos(origin.X, origin.Y);
                Pump(0.3);

                main.SetLyricLocked(original, false);
                main.ShowDesktopLyrics(false);

                bool transparent = (styleLocked & WS_EX_TRANSPARENT_LOCAL) != 0;
                bool transparentBefore = (styleUnlocked & WS_EX_TRANSPARENT_LOCAL) != 0;
                bool noActivate = (styleLocked & WS_EX_NOACTIVATE_LOCAL) != 0;
                bool toolWindow = (styleLocked & WS_EX_TOOLWINDOW_LOCAL) != 0;

                Report(report, "解锁时窗口中心命中桌面歌词 = " + hitUnlocked + "（期望 True）");
                Report(report, "锁定后窗口中心命中桌面歌词 = " + hitLocked + "（期望 False）");
                Report(report, "解锁时 WS_EX_TRANSPARENT    = " + transparentBefore + "（期望 False）");
                Report(report, "锁定后 WS_EX_TRANSPARENT    = " + transparent + "（期望 True）");
                Report(report, "锁定后 WS_EX_NOACTIVATE     = " + noActivate + "（期望 True，不抢焦点）");
                Report(report, "锁定后 WS_EX_TOOLWINDOW     = " + toolWindow + "（期望 True，不占 Alt+Tab）");
                Report(report, "锁定后解锁按钮可见可点     = " + (unlockVisible && unlockClickable) + "（期望 True）");
                Report(report, "鼠标靠近时显示解锁按钮     = " + shownWhenNear + "（期望 True）");
                Report(report, "鼠标离开后自动隐藏         = " + hiddenWhenFar + "（期望 True）");

                bool ok = hitUnlocked && !hitLocked && !transparentBefore && transparent && noActivate
                    && toolWindow && unlockVisible && unlockClickable && shownWhenNear && hiddenWhenFar;
                Report(report, ok ? "LOCKCHECK OK" : "LOCKCHECK FAILED");
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                Report(report, "lockcheck failed: " + ex.Message);
                return 1;
            }
        }

        private static void Report(StringBuilder report, string line)
        {
            Console.WriteLine(line);
            report.AppendLine(line);
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lightmusic-lockcheck.log"),
                    report.ToString(), System.Text.Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        private static bool HitTest(IntPtr handle)
        {
            RECT rect;
            if (!GetWindowRect(handle, out rect)) return false;
            POINT point = new POINT();
            point.X = (rect.Left + rect.Right) / 2;
            point.Y = (rect.Top + rect.Bottom) / 2;
            return WindowFromPoint(point) == handle;
        }

        private static void Pump(double seconds)
        {
            System.Windows.Threading.DispatcherFrame frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(seconds);
            timer.Tick += delegate
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        /// <summary>真实启动一次界面（显示窗口若干秒后自动退出），用于冒烟测试。</summary>
        private static int StreamTest(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Console.WriteLine("usage: LightMusic.exe --streamtest <url>");
                return 1;
            }

            StringBuilder report = new StringBuilder();
            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                System.Windows.Media.MediaPlayer player = new System.Windows.Media.MediaPlayer();
                bool opened = false;
                bool failed = false;
                string error = null;
                player.MediaOpened += delegate { opened = true; };
                player.MediaFailed += delegate(object sender, System.Windows.Media.ExceptionEventArgs e)
                {
                    failed = true;
                    error = e != null && e.ErrorException != null ? e.ErrorException.Message : "unknown";
                };
                player.Volume = 0;
                player.Open(new Uri(url));
                Pump(6);

                double duration = player.NaturalDuration.HasTimeSpan
                    ? player.NaturalDuration.TimeSpan.TotalSeconds : 0;
                Report(report, "stream opened=" + opened + " failed=" + failed + " duration=" + duration.ToString("0.0") + "s"
                    + (error == null ? "" : " error=" + error));

                if (opened)
                {
                    player.Play();
                    Pump(3);
                    Report(report, "position after 3s = " + player.Position.TotalSeconds.ToString("0.00") + "s");
                    try
                    {
                        player.Position = TimeSpan.FromSeconds(60);
                        Pump(2);
                        Report(report, "seek to 60s -> " + player.Position.TotalSeconds.ToString("0.00") + "s");
                    }
                    catch (Exception ex)
                    {
                        Report(report, "seek failed: " + ex.Message);
                    }
                }
                player.Close();
                return opened ? 0 : 1;
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                Report(report, "streamtest failed: " + ex.Message);
                return 1;
            }
        }

        /// <summary>真实启动一次界面（显示窗口若干秒后自动退出），用于冒烟测试。</summary>
        /// <summary>上传自检：把本地文件传到云盘分享目录，并列出结果确认。</summary>
        /// <summary>用 API 令牌删除云盘上的文件（同时验证删除接口）。</summary>
        /// <summary>
        /// 把本地文件夹补齐到云端：云端没有的上传；同名且大小相同的跳过；
        /// 同名但大小不同的覆盖。用于「传一次，然后本地就可以删了」的场景。
        /// </summary>
        private static int UploadAll(string endpoint, string localFolder)
        {
            StringBuilder report = new StringBuilder();
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(localFolder))
            {
                Console.WriteLine("usage: LightMusic.exe --uploadall <endpoint> <local-folder>");
                return 1;
            }

            List<string> files = new List<string>();
            foreach (string file in System.IO.Directory.GetFiles(localFolder, "*.*",
                System.IO.SearchOption.AllDirectories))
            {
                string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".lrc" || Array.IndexOf(LibraryScanner.Extensions, ext) >= 0) files.Add(file);
            }
            report.AppendLine("本地待处理文件: " + files.Count);

            Dictionary<string, long> cloud = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (CloudEntry entry in CloudClient.ListAllFiles(endpoint, 3))
            {
                if (!entry.IsDirectory) cloud[entry.Name] = entry.Size;
            }
            report.AppendLine("云端已有文件: " + cloud.Count);

            int uploaded = 0;
            int skipped = 0;
            int replaced = 0;
            int failed = 0;
            foreach (string file in files)
            {
                string name = System.IO.Path.GetFileName(file);
                long size = new System.IO.FileInfo(file).Length;
                bool exists = cloud.ContainsKey(name);
                if (exists && cloud[name] == size)
                {
                    skipped++;
                    continue;
                }
                try
                {
                    bool wasReplaced;
                    CloudClient.Upload(endpoint, file, "/", null, out wasReplaced);
                    uploaded++;
                    if (exists) replaced++;
                    report.AppendLine("  ↑ " + name + " (" + size + " 字节)" + (exists ? " [覆盖]" : ""));
                }
                catch (Exception ex)
                {
                    failed++;
                    report.AppendLine("  ✗ " + name + " 上传失败: " + ex.Message);
                }
            }

            report.AppendLine("上传 " + uploaded + " 个（其中覆盖 " + replaced + "），跳过 " + skipped
                + " 个，失败 " + failed + " 个");
            Console.WriteLine(report.ToString());
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "lightmusic-lockcheck.log"), report.ToString(), System.Text.Encoding.UTF8);
            }
            catch (Exception)
            {
            }
            return failed == 0 ? 0 : 1;
        }

        /// <summary>用 API 令牌删除云盘上的文件（同时验证删除接口）。</summary>
        private static int CloudDelete(string endpoint, string cloudPath)
        {
            StringBuilder report = new StringBuilder();
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(cloudPath))
            {
                Console.WriteLine("usage: LightMusic.exe --clouddelete <token> <path-in-library>");
                return 1;
            }
            try
            {
                report.AppendLine("is api token = " + CloudClient.IsApiToken(endpoint));
                CloudRepoInfo info = CloudClient.GetRepoInfo(endpoint);
                report.AppendLine("repo = " + info.Name + " files=" + info.FileCount);

                string parent = "/";
                int slash = cloudPath.LastIndexOf('/');
                if (slash > 0) parent = cloudPath.Substring(0, slash);
                string name = cloudPath.Substring(slash + 1);
                List<string> names = new List<string>();
                names.Add(name);
                CloudClient.DeleteFiles(endpoint, parent, names);
                report.AppendLine("deleted: " + cloudPath);

                List<CloudEntry> entries = CloudClient.List(endpoint, string.Empty);
                bool stillThere = false;
                foreach (CloudEntry entry in entries)
                {
                    if (entry.Name == name) stillThere = true;
                }
                report.AppendLine("还在列表里 = " + stillThere + "（共 " + entries.Count + " 个文件）");
                report.AppendLine(stillThere ? "DELETETEST FAILED" : "DELETETEST OK");
                Console.WriteLine(report.ToString());
                try
                {
                    System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                        "lightmusic-lockcheck.log"), report.ToString(), System.Text.Encoding.UTF8);
                }
                catch (Exception)
                {
                }
                return stillThere ? 1 : 0;
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                Console.WriteLine("delete failed: " + ex.Message);
                return 1;
            }
        }

        private static int UploadTest(string url, string localFile)
        {
            StringBuilder report = new StringBuilder();
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(localFile))
            {
                Console.WriteLine("usage: LightMusic.exe --uploadtest <share-url> <local-file>");
                return 1;
            }
            try
            {
                Report(report, "upload url = " + CloudClient.GetUploadUrl(url, "/"));
                long last = 0;
                bool replaced;
                CloudClient.Upload(url, localFile, "/", delegate(long done, long total) { last = done; }, out replaced);
                Report(report, "uploaded " + System.IO.Path.GetFileName(localFile)
                    + " (" + last + " bytes, replaced=" + replaced + ")");

                List<CloudEntry> entries = CloudClient.List(url, string.Empty);
                bool found = false;
                foreach (CloudEntry entry in entries)
                {
                    if (entry.Name == System.IO.Path.GetFileName(localFile)) found = true;
                }
                Report(report, "出现在文件列表 = " + found + "（共 " + entries.Count + " 个文件）");
                Report(report, found ? "UPLOADTEST OK" : "UPLOADTEST FAILED");
                return found ? 0 : 1;
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                Report(report, "uploadtest failed: " + ex.Message);
                return 1;
            }
        }

        private static int CloudTest(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Console.WriteLine("usage: LightMusic.exe --cloudtest <share-url>");
                return 1;
            }

            StringBuilder report = new StringBuilder();
            int failures = 0;
            try
            {
                Report(report, "token = " + CloudClient.ParseToken(url));
                Report(report, "share = " + CloudClient.ShareUrl(url));

                ScanResult scan = CloudLibrary.Scan(url, null, null);
                Report(report, "songs = " + scan.Songs.Count);
                if (scan.Songs.Count == 0)
                {
                    Report(report, "CLOUDTEST FAILED: 没有扫描到歌曲");
                    return 1;
                }

                int withLyrics = 0;
                foreach (Song song in scan.Songs)
                {
                    if (song.HasLyrics) withLyrics++;
                }
                Report(report, "with lyrics = " + withLyrics + "/" + scan.Songs.Count);

                // 逐首拉 512KB 文件头，确认每个音频都能解析出时长（校验整库完整性）
                int parsed = 0;
                List<string> broken = new List<string>();
                foreach (Song song in scan.Songs)
                {
                    try
                    {
                        int probe = Math.Min(512 * 1024, song.Size > 0 ? (int)song.Size : 512 * 1024);
                        byte[] probeBytes = CloudClient.DownloadHead(url, song.CloudPath, probe);
                        double d = DurationReader.ReadBytes(probeBytes, song.Size,
                            System.IO.Path.GetExtension(song.FileName));
                        if (d > 10 && d < 36000) parsed++;
                        else broken.Add(song.FileName);
                    }
                    catch (Exception ex)
                    {
                        broken.Add(song.FileName + " (" + ex.Message + ")");
                    }
                }
                Report(report, "duration parsed = " + parsed + "/" + scan.Songs.Count
                    + (broken.Count == 0 ? "" : "，异常：" + string.Join("; ", broken.ToArray())));
                if (parsed != scan.Songs.Count) failures++;

                Song first = scan.Songs[0];
                Report(report, "first = " + first.Title + " - " + first.Artist + " (" + first.FileName + ", "
                    + (first.Size / 1024 / 1024.0).ToString("0.0") + " MB)");

                byte[] head = CloudClient.DownloadHead(url, first.CloudPath, 512 * 1024);
                double duration = DurationReader.ReadBytes(head, first.Size,
                    System.IO.Path.GetExtension(first.FileName));
                Report(report, "head bytes = " + head.Length + ", duration = " + duration.ToString("0.0") + "s");
                if (duration < 10 || duration > 3600) failures++;

                string target = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lightmusic-cloud-test.mp3");
                long lastDone = 0;
                CloudClient.DownloadTo(url, first.CloudPath, target, delegate(long done, long total)
                {
                    lastDone = done;
                });
                long length = new System.IO.FileInfo(target).Length;
                Report(report, "downloaded = " + (length / 1024 / 1024.0).ToString("0.0") + " MB, size match = "
                    + (length == first.Size) + " (progress last = " + lastDone + ")");
                if (length != first.Size) failures++;

                double localDuration = DurationReader.Read(target);
                Report(report, "local parse = " + localDuration.ToString("0.0") + "s, diff = "
                    + Math.Abs(localDuration - duration).ToString("0.0") + "s");
                if (Math.Abs(localDuration - duration) > 1.5) failures++;
                System.IO.File.Delete(target);

                Song lyricSong = null;
                foreach (Song song in scan.Songs)
                {
                    if (song.HasLyrics) { lyricSong = song; break; }
                }
                if (lyricSong != null)
                {
                    string lrcPath = lyricSong.LyricPath.Substring(
                        (CloudLibrary.PseudoScheme + CloudClient.ParseToken(url)).Length);
                    string text = CloudClient.GetText(url, lrcPath);
                    LyricDocument doc = LrcParser.Parse(text);
                    Report(report, "lyric lines = " + doc.Lines.Count + ", synced = " + doc.Synced
                        + ", first = " + (doc.Lines.Count > 0 ? doc.Lines[0].Text : ""));
                    if (doc.Lines.Count == 0) failures++;
                }

                Report(report, failures == 0 ? "CLOUDTEST OK" : "CLOUDTEST FAILED: " + failures);
                return failures == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                Report(report, "cloudtest failed: " + ex.Message);
                return 1;
            }
        }

        /// <summary>真实启动一次界面（显示窗口若干秒后自动退出），用于冒烟测试。</summary>
        private static int SmokeRun(string[] args)
        {
            try
            {
                StringBuilder trace = new StringBuilder();
                Trace(trace, "start");
                bool play = args.Length > 1 && args[1] == "play";
                MainWindow.Headless = true;
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    LogCrash(e.Exception);
                    Trace(trace, "dispatcher exception: " + e.Exception.Message);
                };
                Theme.EnsureStyles();
                Trace(trace, "styles");
                MainWindow window = new MainWindow();
                // 冒烟测试里强制关闭“关闭到托盘”，否则窗口关闭后进程会留在托盘
                window.Settings.CloseToTray = false;
                window.Settings.MinimizeToTray = false;
                app.MainWindow = window;
                window.Show();
                Trace(trace, "shown");

                if (play)
                {
                    System.Windows.Threading.DispatcherTimer starter = new System.Windows.Threading.DispatcherTimer();
                    starter.Interval = TimeSpan.FromSeconds(4);
                    starter.Tick += delegate
                    {
                        starter.Stop();
                        Trace(trace, "play -> " + window.SmokePlayFirst());
                    };
                    starter.Start();
                }

                int seconds = play ? 26 : 5;
                System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(seconds);
                timer.Tick += delegate
                {
                    timer.Stop();
                    Trace(trace, "state -> " + window.SmokeState());
                    Trace(trace, "ui -> " + window.SmokeStatusText());
                    Trace(trace, "tick -> close");
                    window.Close();
                };
                timer.Start();
                Trace(trace, "timer started");
                app.Run();
                Trace(trace, "app.Run returned");
                Console.WriteLine("smoke ok");
                return 0;
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                Console.WriteLine("smoke failed: " + ex.Message);
                return 1;
            }
        }

        private static void Trace(StringBuilder trace, string step)
        {
            try
            {
                trace.AppendLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + step);
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lightmusic-smoke.log");
                using (System.IO.FileStream fs = new System.IO.FileStream(path, System.IO.FileMode.Create,
                    System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite))
                using (System.IO.StreamWriter writer = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8))
                {
                    writer.Write(trace.ToString());
                }
            }
            catch (Exception)
            {
            }
        }

        private static void FocusRunningInstance()
        {
            try
            {
                Process current = Process.GetCurrentProcess();
                Process[] all = Process.GetProcessesByName(current.ProcessName);
                foreach (Process process in all)
                {
                    if (process.Id == current.Id) continue;
                    IntPtr handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero) continue;
                    ShowWindow(handle, SW_RESTORE);
                    SetForegroundWindow(handle);
                    break;
                }
            }
            catch (Exception)
            {
            }
        }

        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        private const int ATTACH_PARENT_PROCESS = -1;

        /// <summary>让窗口程序也能把自检输出打印到调用它的控制台。</summary>
        private static void AttachConsole()
        {
            try
            {
                if (AttachConsole(ATTACH_PARENT_PROCESS))
                {
                    System.IO.StreamWriter writer = new System.IO.StreamWriter(Console.OpenStandardOutput());
                    writer.AutoFlush = true;
                    Console.SetOut(writer);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}

