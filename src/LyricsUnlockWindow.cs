using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Skylark
{
    /// <summary>
    /// 锁定桌面歌词时出现的「解锁」小按钮。
    /// 它是独立窗口，因此锁定状态下歌词本体鼠标穿透、而这个按钮始终可以点。
    /// </summary>
    public class LyricsUnlockWindow : Window
    {
        private readonly MainWindow main;
        private readonly Border frame = new Border();

        public LyricsUnlockWindow(MainWindow owner)
        {
            main = owner;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = Ui.Font;

            frame.CornerRadius = new CornerRadius(10);
            frame.Padding = new Thickness(11, 6, 13, 6);
            frame.Background = new SolidColorBrush(Color.FromArgb(190, 16, 19, 26));
            frame.Cursor = Cursors.Hand;
            frame.ToolTip = "点这里解锁桌面歌词（解锁后可拖动、调整字号，Ctrl+Alt+L 也可以）";

            Canvas icon = Icons.Create("unlock", 15, "OnAccent");
            icon.VerticalAlignment = VerticalAlignment.Center;
            TextBlock label = Ui.Text("解锁", 12.5, "OnAccent");
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(7, 0, 0, 0);
            frame.Child = Ui.Row(0, icon, label);

            frame.MouseLeftButtonUp += delegate { main.ToggleLyricLock(); };
            frame.MouseEnter += delegate
            {
                frame.Background = new SolidColorBrush(Color.FromArgb(240, 36, 42, 56));
            };
            frame.MouseLeave += delegate
            {
                frame.Background = new SolidColorBrush(Color.FromArgb(190, 16, 19, 26));
            };

            Content = frame;
            SourceInitialized += delegate { ApplyStyles(); };
        }

        /// <summary>贴在桌面歌词窗口的右上角。</summary>
        public void PlaceNear(DesktopLyricsWindow lyrics)
        {
            if (lyrics == null) return;
            double width = ActualWidth > 1 ? ActualWidth : 80;
            double left = lyrics.Left + lyrics.ActualWidth - width - 34;
            double top = lyrics.Top + 6;

            Rect area = SystemParameters.WorkArea;
            if (left < area.Left) left = area.Left + 8;
            if (left + width > area.Right) left = area.Right - width - 8;
            if (top < area.Top) top = area.Top + 8;
            if (top + 40 > area.Bottom) top = area.Bottom - 48;

            Left = left;
            Top = top;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private void ApplyStyles()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                int style = GetWindowLong(handle, GWL_EXSTYLE);
                style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                style &= ~0x00000020; // 确保不是鼠标穿透
                SetWindowLong(handle, GWL_EXSTYLE, style);
            }
            catch (Exception)
            {
            }
        }
    }
}
