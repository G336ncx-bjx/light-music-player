using System;
using System.Text;
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
                Environment.Exit(SmokeRun());
                return;
            }
            if (args.Length > 0 && args[0] == "--lockcheck")
            {
                AttachConsole();
                Environment.Exit(LockCheck());
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

                bool ok = hitUnlocked && !hitLocked && !transparentBefore && transparent && noActivate && toolWindow;
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
        private static int SmokeRun()
        {
            try
            {
                MainWindow.Headless = true;
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Theme.EnsureStyles();
                MainWindow window = new MainWindow();
                app.MainWindow = window;
                window.Show();

                int seconds = 5;
                System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(seconds);
                timer.Tick += delegate
                {
                    timer.Stop();
                    window.Close();
                };
                timer.Start();
                app.Run();
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

