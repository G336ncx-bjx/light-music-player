using System;
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
