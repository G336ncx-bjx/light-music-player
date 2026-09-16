using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LightMusic
{
    /// <summary>离屏渲染主界面为 PNG，用于开发期的视觉校对。</summary>
    public static class ShotMode
    {
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

        public static int Run(string[] args)
        {
            string output = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "lightmusic.png");
            string view = args.Length > 2 ? args[2] : "library";
            string theme = args.Length > 3 ? args[3] : "dark";
            double width = args.Length > 4 ? double.Parse(args[4]) : 1180;
            double height = args.Length > 5 ? double.Parse(args[5]) : 740;

            MainWindow.Headless = true;
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Theme.Current = theme;

            string lyricPath = Path.Combine(Path.GetTempPath(), "lightmusic-demo.lrc");
            File.WriteAllText(lyricPath,
                "[ti:晴天]\n[ar:周杰伦]\n[00:00.00]晴天 - 周杰伦\n[00:12.40]故事的小黄花\n" +
                "[00:17.20]从出生那年就飘着\n[00:22.60]童年的荡秋千\n[00:27.80]随记忆一直晃到现在\n" +
                "[00:33.00]Re So So Si Do Si La\n[00:38.20]吹着前奏望着天空\n[00:43.60]我想起花瓣试着掉落\n",
                new System.Text.UTF8Encoding(true));

            MainWindow window = new MainWindow();
            Theme.Apply(theme);
            window.LoadDemoForShot(view, lyricPath);
            Pump(0.25);

            FrameworkElement root;
            if (view == "desktop")
            {
                DesktopLyricsWindow lyric = new DesktopLyricsWindow(window);
                lyric.ApplySettings();
                lyric.UpdateNow();
                lyric.SimulateHoverForShot();
                root = lyric.Content as FrameworkElement;
                width = 980;
                height = 200;
            }
            else if (view == "desktop-locked")
            {
                window.Settings.LyricLocked = true;
                DesktopLyricsWindow lyric = new DesktopLyricsWindow(window);
                lyric.ApplySettings();
                lyric.UpdateNow();
                lyric.SimulateHoverForShot();
                root = lyric.Content as FrameworkElement;
                width = 980;
                height = 200;
            }
            else
            {
                root = window.Content as FrameworkElement;
            }
            if (root == null)
            {
                Console.WriteLine("no content");
                return 1;
            }
            root.Width = width;
            root.Height = height;
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            Pump(0.25);
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();

            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                (int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            if (view == "desktop" || view == "desktop-locked")
            {
                // 桌面歌词是半透明浮窗，先铺一层桌面背景方便观察效果
                DrawingVisual background = new DrawingVisual();
                using (DrawingContext dc = background.RenderOpen())
                {
                    LinearGradientBrush brush = new LinearGradientBrush(
                        (Color)ColorConverter.ConvertFromString("#1B3A5C"),
                        (Color)ColorConverter.ConvertFromString("#3E2A4F"),
                        new Point(0, 0), new Point(1, 1));
                    dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
                }
                bitmap.Render(background);
            }
            bitmap.Render(root);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = new FileStream(output, FileMode.Create))
            {
                encoder.Save(stream);
            }
            Console.WriteLine("shot: " + output);
            return 0;
        }
    }
}
