using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LightMusic
{
    /// <summary>桌面歌词浮窗：无边框、可拖动、可锁定（鼠标穿透）、置顶。</summary>
    public class DesktopLyricsWindow : Window
    {
        private readonly MainWindow main;
        private readonly Border frame = new Border();
        private readonly TextBlock currentText = new TextBlock();
        private readonly TextBlock translationText = new TextBlock();
        private readonly TextBlock nextText = new TextBlock();
        private readonly TextBlock hintText = new TextBlock();
        private readonly StackPanel panel = new StackPanel();
        private bool locked;

        public DesktopLyricsWindow(MainWindow owner)
        {
            main = owner;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = 920;
            SizeToContent = SizeToContent.Height;
            MinHeight = 96;
            FontFamily = Ui.Font;

            frame.CornerRadius = new CornerRadius(16);
            frame.Padding = new Thickness(28, 14, 28, 16);
            frame.Margin = new Thickness(26);
            frame.Background = new SolidColorBrush(Color.FromArgb(150, 10, 12, 16));
            frame.Effect = new DropShadowEffect
            {
                BlurRadius = 26,
                ShadowDepth = 4,
                Opacity = 0.45,
                Color = Colors.Black
            };

            currentText.FontSize = 34;
            currentText.FontWeight = FontWeights.SemiBold;
            currentText.TextAlignment = TextAlignment.Center;
            currentText.TextWrapping = TextWrapping.Wrap;
            currentText.Foreground = Brushes.White;

            translationText.FontSize = 18;
            translationText.TextAlignment = TextAlignment.Center;
            translationText.TextWrapping = TextWrapping.Wrap;
            translationText.Margin = new Thickness(0, 2, 0, 0);
            translationText.Opacity = 0.85;
            translationText.Foreground = Brushes.White;

            nextText.FontSize = 19;
            nextText.TextAlignment = TextAlignment.Center;
            nextText.TextWrapping = TextWrapping.Wrap;
            nextText.Margin = new Thickness(0, 6, 0, 0);
            nextText.Opacity = 0.6;
            nextText.Foreground = Brushes.White;

            hintText.FontSize = 11.5;
            hintText.TextAlignment = TextAlignment.Center;
            hintText.Margin = new Thickness(0, 6, 0, 0);
            hintText.Opacity = 0.45;
            hintText.Foreground = Brushes.White;
            hintText.Text = DefaultHint;

            panel.Children.Add(currentText);
            panel.Children.Add(translationText);
            panel.Children.Add(nextText);
            panel.Children.Add(hintText);
            frame.Child = panel;
            Content = frame;

            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (locked) return;
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            MouseRightButtonUp += delegate { ShowMenu(); };
            MouseEnter += delegate { hintText.Visibility = Visibility.Visible; };
            MouseLeave += delegate { hintText.Visibility = Visibility.Collapsed; };
            hintText.Visibility = Visibility.Collapsed;

            SourceInitialized += delegate { ApplyClickThrough(); };
            Loaded += delegate
            {
                PlaceWindow();
                ApplySettings();
                UpdateNow();
            };
        }

        /// <summary>应用设置中的字号、颜色、透明度与锁定状态。</summary>
        public void ApplySettings()
        {
            AppSettings s = main.Settings;
            currentText.FontSize = s.LyricFontSize;
            translationText.FontSize = Math.Max(12, s.LyricFontSize * 0.55);
            nextText.FontSize = Math.Max(12, s.LyricFontSize * 0.6);
            Color color;
            try
            {
                color = (Color)ColorConverter.ConvertFromString(s.LyricColor);
            }
            catch (Exception)
            {
                color = Colors.White;
            }
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            currentText.Foreground = brush;
            translationText.Foreground = brush;
            nextText.Foreground = brush;
            Opacity = Math.Max(0.2, Math.Min(1, s.LyricOpacity));
            translationText.Visibility = s.LyricShowTranslation && translationText.Text.Length > 0
                ? Visibility.Visible : Visibility.Collapsed;
            bool wantLock = s.LyricLocked;
            if (wantLock != locked)
            {
                locked = wantLock;
                ApplyClickThrough();
            }
            frame.Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
            Width = Math.Min(1200, Math.Max(560, SystemParameters.WorkArea.Width * 0.72));
        }

        /// <summary>刷新歌词显示。</summary>
        public void UpdateNow()
        {
            Song song = main.CurrentSong;
            if (song == null)
            {
                currentText.Text = "未在播放";
                translationText.Text = string.Empty;
                nextText.Text = string.Empty;
                translationText.Visibility = Visibility.Collapsed;
                return;
            }

            LyricsView view = main.Lyrics;
            List<LyricLine> lines = view.Lines;
            int index = view.CurrentIndex;

            if (lines == null || lines.Count == 0)
            {
                currentText.Text = song.Title;
                translationText.Text = string.Empty;
                nextText.Text = "（未找到歌词，放一个同名 .lrc 文件即可）";
                translationText.Visibility = Visibility.Collapsed;
                return;
            }

            if (index < 0)
            {
                currentText.Text = "♪ " + song.Title;
                translationText.Text = string.Empty;
                translationText.Visibility = Visibility.Collapsed;
                nextText.Text = lines[0].Text;
                return;
            }

            currentText.Text = lines[index].Text;
            if (!string.IsNullOrEmpty(lines[index].Translation) && main.Settings.LyricShowTranslation)
            {
                translationText.Text = lines[index].Translation;
                translationText.Visibility = Visibility.Visible;
            }
            else
            {
                translationText.Text = string.Empty;
                translationText.Visibility = Visibility.Collapsed;
            }
            nextText.Text = index + 1 < lines.Count ? lines[index + 1].Text : string.Empty;
        }

        private const string DefaultHint = "拖动可移动 · 右键可锁定（鼠标穿透）· Ctrl+Alt+L 快速锁定 / 解锁";
        private System.Windows.Threading.DispatcherTimer hintTimer;

        /// <summary>短暂显示一条提示（例如“已锁定”）。</summary>
        public void FlashHint(string message)
        {
            hintText.Text = message;
            hintText.Visibility = Visibility.Visible;
            if (hintTimer == null)
            {
                hintTimer = new System.Windows.Threading.DispatcherTimer();
                hintTimer.Interval = TimeSpan.FromMilliseconds(2600);
                hintTimer.Tick += delegate
                {
                    hintTimer.Stop();
                    hintText.Text = DefaultHint;
                    hintText.Visibility = Visibility.Collapsed;
                };
            }
            hintTimer.Stop();
            hintTimer.Start();
        }

        private void PlaceWindow()
        {
            Rect area = SystemParameters.WorkArea;
            double left = main.Settings.LyricX;
            double top = main.Settings.LyricY;
            if (double.IsNaN(left) || double.IsNaN(top)
                || left < AppSettings.Unset + 1 || top < AppSettings.Unset + 1)
            {
                left = area.Left + (area.Width - Width) / 2;
                top = area.Bottom - 200;
            }
            else
            {
                if (left < area.Left - Width + 80) left = area.Left;
                if (left > area.Right - 80) left = area.Right - Width;
                if (top < area.Top - 10) top = area.Top;
                if (top > area.Bottom - 40) top = area.Bottom - 120;
            }
            Left = left;
            Top = top;
        }

        private void ShowMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.Items.Add(Item(locked ? "解锁（可拖动）" : "锁定（鼠标穿透）", delegate
            {
                main.ToggleLyricLock();
            }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("增大字号", delegate { ChangeFont(3); }));
            menu.Items.Add(Item("减小字号", delegate { ChangeFont(-3); }));
            menu.Items.Add(Item(main.Settings.LyricShowTranslation ? "隐藏翻译" : "显示翻译", delegate
            {
                main.Settings.LyricShowTranslation = !main.Settings.LyricShowTranslation;
                main.SaveSettings();
                ApplySettings();
                main.RefreshLyricsView();
            }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("回到主界面", delegate { main.ShowFromTrayPublic(); }));
            menu.Items.Add(Item("关闭桌面歌词", delegate { main.ShowDesktopLyrics(false); }));
            menu.PlacementTarget = this;
            menu.IsOpen = true;
        }

        private void ChangeFont(double delta)
        {
            double size = main.Settings.LyricFontSize + delta;
            if (size < 18) size = 18;
            if (size > 72) size = 72;
            main.Settings.LyricFontSize = size;
            main.SaveSettings();
            ApplySettings();
            main.NotifySettingsChanged();
        }

        private static MenuItem Item(string text, RoutedEventHandler handler)
        {
            MenuItem item = new MenuItem();
            item.Header = text;
            item.Click += handler;
            return item;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private void ApplyClickThrough()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero) return;
                int style = GetWindowLong(handle, GWL_EXSTYLE);
                style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                if (locked) style |= WS_EX_TRANSPARENT;
                else style &= ~WS_EX_TRANSPARENT;
                SetWindowLong(handle, GWL_EXSTYLE, style);
                // 让扩展样式立即生效
                SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            catch (Exception)
            {
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
    }
}
