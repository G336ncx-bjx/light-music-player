using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace LightMusic
{
    /// <summary>设置页面。</summary>
    public class SettingsView : UserControl
    {
        private readonly MainWindow main;
        private readonly TextBlock dirText = Ui.Text("", 12.5, "TextDim");
        private readonly TextBlock hiddenText = Ui.Text("", 12, "TextMuted");
        private readonly Slider fontSizeSlider = new Slider();
        private readonly Slider opacitySlider = new Slider();
        private readonly TextBlock fontSizeLabel = Ui.Text("", 12, "TextMuted");
        private readonly TextBlock opacityLabel = Ui.Text("", 12, "TextMuted");
        private readonly CheckBox recursiveCheck = new CheckBox();
        private readonly CheckBox resumeCheck = new CheckBox();
        private readonly CheckBox autoPlayCheck = new CheckBox();
        private readonly CheckBox mediaKeysCheck = new CheckBox();
        private readonly CheckBox translationCheck = new CheckBox();
        private readonly CheckBox lockCheck = new CheckBox();
        private readonly CheckBox closeTrayCheck = new CheckBox();
        private readonly CheckBox minimizeTrayCheck = new CheckBox();
        private readonly StackPanel colorRow = new StackPanel();
        private readonly Dictionary<string, Border> colorSwatches = new Dictionary<string, Border>();
        private RadioButton darkTheme;
        private RadioButton lightTheme;
        private RadioButton modeSequential;
        private RadioButton modeListLoop;
        private RadioButton modeSingleLoop;
        private RadioButton modeShuffle;

        public SettingsView(MainWindow owner)
        {
            main = owner;
            Padding = new Thickness(22, 16, 22, 16);

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

            StackPanel page = new StackPanel();
            page.MaxWidth = 860;
            page.HorizontalAlignment = HorizontalAlignment.Left;

            TextBlock title = Ui.Text("设置", 21, "Text", FontWeights.SemiBold);
            title.Margin = new Thickness(2, 0, 0, 14);
            page.Children.Add(title);

            page.Children.Add(BuildLibraryCard());
            page.Children.Add(BuildPlayCard());
            page.Children.Add(BuildLyricCard());
            page.Children.Add(BuildAppearanceCard());
            page.Children.Add(BuildWindowCard());
            page.Children.Add(BuildAboutCard());

            scroll.Content = page;
            Content = scroll;
            Refresh();
            main.SettingsChanged += delegate { Refresh(); };
        }

        private Border Card(string title, params UIElement[] rows)
        {
            Border card = new Border();
            card.Style = (Style)Application.Current.Resources["CardBox"];
            card.Margin = new Thickness(0, 0, 0, 14);
            card.Padding = new Thickness(20, 16, 20, 18);

            StackPanel panel = new StackPanel();
            TextBlock header = Ui.Text(title, 15, "Text", FontWeights.SemiBold);
            header.Margin = new Thickness(0, 0, 0, 12);
            panel.Children.Add(header);
            foreach (UIElement row in rows)
            {
                row.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));
                panel.Children.Add(row);
            }
            card.Child = panel;
            return card;
        }

        private Grid Row(string label, string hint, UIElement control)
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions[0].Width = Ui.Px(176);
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions[1].Width = Ui.Stars(1);

            StackPanel labels = new StackPanel();
            labels.VerticalAlignment = VerticalAlignment.Center;
            TextBlock name = Ui.Text(label, 13, "TextDim");
            labels.Children.Add(name);
            if (!string.IsNullOrEmpty(hint))
            {
                TextBlock h = Ui.Text(hint, 11.5, "TextMuted");
                h.Margin = new Thickness(0, 3, 12, 0);
                h.TextWrapping = TextWrapping.Wrap;
                labels.Children.Add(h);
            }
            grid.Children.Add(labels);
            Grid.SetColumn(control, 1);
            FrameworkElement element = control as FrameworkElement;
            if (element != null) element.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(control);
            return grid;
        }

        private CheckBox Check(string text, bool value, Action<bool> changed)
        {
            CheckBox box = new CheckBox();
            box.Style = (Style)Application.Current.Resources["ModernCheckBox"];
            box.Content = text;
            box.IsChecked = value;
            box.Click += delegate { changed(box.IsChecked == true); };
            return box;
        }

        private StackPanel Segmented(params RadioButton[] buttons)
        {
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Style = (Style)Application.Current.Resources["Segment"];
                buttons[i].Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0);
                row.Children.Add(buttons[i]);
            }
            return row;
        }

        private UIElement BuildLibraryCard()
        {
            TextBlock path = dirText;
            path.TextWrapping = TextWrapping.Wrap;
            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            Button change = Ui.Button("更改目录", "OutlineButton", delegate { main.ChooseMusicDir(); });
            Button open = Ui.Button("打开目录", "OutlineButton", delegate { main.OpenMusicFolder(); });
            Button rescan = Ui.Button("重新扫描", "OutlineButton", delegate { main.Rescan(); });
            actions.Children.Add(change);
            open.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(open);
            rescan.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(rescan);

            StackPanel pathRow = Ui.Column(8, path, actions);

            recursiveCheck.Style = (Style)Application.Current.Resources["ModernCheckBox"];
            recursiveCheck.Content = "包含子文件夹";
            recursiveCheck.Margin = new Thickness(0, 4, 0, 0);
            recursiveCheck.Click += delegate
            {
                main.Settings.Recursive = recursiveCheck.IsChecked == true;
                main.SaveSettings();
                main.Rescan();
            };

            Button restore = Ui.Button("恢复全部", "OutlineButton", delegate
            {
                main.Settings.Hidden.Clear();
                main.SaveSettings();
                main.Rescan();
                main.ShowToast("已恢复被移除的歌曲");
            });
            StackPanel hiddenRow = Ui.Row(10, hiddenText, restore);

            return Card("音乐库",
                Row("音乐目录", "歌词 .lrc 与歌曲放在同一目录", pathRow),
                recursiveCheck,
                hiddenRow);
        }

        private UIElement BuildPlayCard()
        {
            resumeCheck.Style = (Style)Application.Current.Resources["ModernCheckBox"];
            resumeCheck.Content = "启动时恢复上次播放的歌曲和进度";
            resumeCheck.Click += delegate
            {
                main.Settings.ResumeLast = resumeCheck.IsChecked == true;
                main.SaveSettings();
            };

            autoPlayCheck.Style = (Style)Application.Current.Resources["ModernCheckBox"];
            autoPlayCheck.Content = "启动后自动开始播放";
            autoPlayCheck.Margin = new Thickness(0, 8, 0, 0);
            autoPlayCheck.Click += delegate
            {
                main.Settings.AutoPlayOnStart = autoPlayCheck.IsChecked == true;
                main.SaveSettings();
            };

            mediaKeysCheck.Style = (Style)Application.Current.Resources["ModernCheckBox"];
            mediaKeysCheck.Content = "响应键盘多媒体按键（播放 / 上一首 / 下一首）";
            mediaKeysCheck.Margin = new Thickness(0, 8, 0, 0);
            mediaKeysCheck.Click += delegate
            {
                main.Settings.MediaKeys = mediaKeysCheck.IsChecked == true;
                main.SaveSettings();
                main.RefreshMediaKeys();
            };

            StackPanel checks = Ui.Column(0, resumeCheck, autoPlayCheck, mediaKeysCheck);

            modeSequential = new RadioButton();
            modeSequential.Content = "顺序播放";
            modeListLoop = new RadioButton();
            modeListLoop.Content = "列表循环";
            modeSingleLoop = new RadioButton();
            modeSingleLoop.Content = "单曲循环";
            modeShuffle = new RadioButton();
            modeShuffle.Content = "随机播放";
            StackPanel modes = Segmented(modeSequential, modeListLoop, modeSingleLoop, modeShuffle);
            modeSequential.Click += delegate { main.SetMode(PlayMode.Sequential); SyncMode(); };
            modeListLoop.Click += delegate { main.SetMode(PlayMode.ListLoop); SyncMode(); };
            modeSingleLoop.Click += delegate { main.SetMode(PlayMode.SingleLoop); SyncMode(); };
            modeShuffle.Click += delegate { main.SetMode(PlayMode.Shuffle); SyncMode(); };

            return Card("播放", checks, Row("默认播放模式", "", modes));
        }

        private UIElement BuildLyricCard()
        {
            fontSizeSlider.Style = (Style)Application.Current.Resources["FlatSlider"];
            fontSizeSlider.Minimum = 18;
            fontSizeSlider.Maximum = 72;
            fontSizeSlider.Width = 260;
            fontSizeSlider.ValueChanged += delegate
            {
                main.Settings.LyricFontSize = fontSizeSlider.Value;
                fontSizeLabel.Text = ((int)fontSizeSlider.Value) + " px";
                ApplyDesktopLyrics();
                main.SaveSettingsDebounced();
            };

            opacitySlider.Style = (Style)Application.Current.Resources["FlatSlider"];
            opacitySlider.Minimum = 20;
            opacitySlider.Maximum = 100;
            opacitySlider.Width = 260;
            opacitySlider.ValueChanged += delegate
            {
                main.Settings.LyricOpacity = opacitySlider.Value / 100.0;
                opacityLabel.Text = ((int)opacitySlider.Value) + "%";
                ApplyDesktopLyrics();
                main.SaveSettingsDebounced();
            };

            fontSizeLabel.VerticalAlignment = VerticalAlignment.Center;
            opacityLabel.VerticalAlignment = VerticalAlignment.Center;
            StackPanel fontRow = Ui.Row(10, fontSizeSlider, fontSizeLabel);
            StackPanel opacityRow = Ui.Row(10, opacitySlider, opacityLabel);

            colorRow.Orientation = Orientation.Horizontal;
            string[] colors = new string[] { "#FFFFFF", "#FFE066", "#7CE7FF", "#FF9CC8", "#A8F0A0", "#C9B6FF" };
            foreach (string color in colors) colorRow.Children.Add(ColorSwatch(color));

            translationCheck.Style = (Style)Application.Current.Resources["ModernCheckBox"];
            translationCheck.Content = "显示翻译（同时间的第二行歌词）";
            translationCheck.Click += delegate
            {
                main.Settings.LyricShowTranslation = translationCheck.IsChecked == true;
                main.SaveSettings();
                main.RefreshLyricsView();
            };

            lockCheck.Style = (Style)Application.Current.Resources["ModernCheckBox"];
            lockCheck.Content = "锁定桌面歌词（鼠标穿透，不会挡住点击）";
            lockCheck.Margin = new Thickness(0, 8, 0, 0);
            lockCheck.Click += delegate
            {
                main.SetLyricLocked(lockCheck.IsChecked == true, false);
            };

            StackPanel checks = Ui.Column(0, translationCheck, lockCheck);

            TextBlock hint = Ui.Text("提示：桌面歌词上单击拖动可移动位置，右键菜单可锁定、调节字号或关闭。", 11.5, "TextMuted");
            hint.TextWrapping = TextWrapping.Wrap;

            return Card("桌面歌词",
                Row("歌词字号", "", fontRow),
                Row("不透明度", "", opacityRow),
                Row("歌词颜色", "", colorRow),
                checks,
                hint);
        }

        private Border ColorSwatch(string color)
        {
            Border border = new Border();
            border.Width = 30;
            border.Height = 30;
            border.CornerRadius = new CornerRadius(9);
            border.Margin = new Thickness(0, 0, 8, 0);
            border.Cursor = System.Windows.Input.Cursors.Hand;
            border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            border.BorderThickness = new Thickness(2);
            border.BorderBrush = Brushes.Transparent;
            border.Tag = color;
            border.MouseLeftButtonUp += delegate
            {
                main.Settings.LyricColor = color;
                main.SaveSettings();
                ApplyDesktopLyrics();
                UpdateSwatches();
            };
            colorSwatches[color] = border;
            return border;
        }

        private UIElement BuildAppearanceCard()
        {
            darkTheme = new RadioButton();
            darkTheme.Content = "深色";
            lightTheme = new RadioButton();
            lightTheme.Content = "浅色";
            StackPanel themes = Segmented(darkTheme, lightTheme);
            darkTheme.Click += delegate { SetTheme("dark"); };
            lightTheme.Click += delegate { SetTheme("light"); };
            return Card("外观", Row("主题", "深色更适合夜间听歌", themes));
        }

        private UIElement BuildWindowCard()
        {
            closeTrayCheck.Style = (Style)Application.Current.Resources["ModernCheckBox"];
            closeTrayCheck.Content = "点击关闭按钮时最小化到托盘";
            closeTrayCheck.Click += delegate
            {
                main.Settings.CloseToTray = closeTrayCheck.IsChecked == true;
                main.SaveSettings();
            };

            minimizeTrayCheck.Style = (Style)Application.Current.Resources["ModernCheckBox"];
            minimizeTrayCheck.Content = "最小化时隐藏到托盘";
            minimizeTrayCheck.Margin = new Thickness(0, 8, 0, 0);
            minimizeTrayCheck.Click += delegate
            {
                main.Settings.MinimizeToTray = minimizeTrayCheck.IsChecked == true;
                main.SaveSettings();
            };

            return Card("窗口与托盘", closeTrayCheck, minimizeTrayCheck);
        }

        private UIElement BuildAboutCard()
        {
            TextBlock version = Ui.Text(MainWindow.AppName + "  v" + MainWindow.AppVersion + "  ·  轻量级本地音乐播放器", 13, "Text");
            TextBlock tech = Ui.Text("纯 Windows 原生实现，无需安装任何运行库；音乐与歌词全部来自本地文件夹。", 12, "TextMuted");
            tech.Margin = new Thickness(0, 6, 0, 0);
            tech.TextWrapping = TextWrapping.Wrap;

            TextBlock shortcutTitle = Ui.Text("快捷键", 13, "TextDim");
            shortcutTitle.Margin = new Thickness(0, 14, 0, 6);
            TextBlock shortcuts = Ui.Text(
                "空格 播放 / 暂停      ← → 快退快进 5 秒      Ctrl + ← → 上一首 / 下一首\n" +
                "↑ ↓ 音量      F 搜索      L 歌词页      Q 播放队列      D 桌面歌词      M 静音\n" +
                "全局快捷键：Ctrl+Alt+L 锁定 / 解锁桌面歌词、Ctrl+Alt+D 显示 / 隐藏桌面歌词\n" +
                "全局多媒体键：播放 / 暂停、上一首、下一首",
                12, "TextMuted");
            shortcuts.TextWrapping = TextWrapping.Wrap;
            shortcuts.LineHeight = 22;

            TextBlock lyricTitle = Ui.Text("桌面歌词", 13, "TextDim");
            lyricTitle.Margin = new Thickness(0, 14, 0, 6);
            TextBlock lyricHint = Ui.Text(
                "锁定后歌词会变成鼠标穿透：点击、拖动、框选都不会被它挡住，完全不影响使用电脑。\n" +
                "解锁方式：主界面顶栏的锁形按钮、设置里的开关、或全局快捷键 Ctrl+Alt+L。\n" +
                "拖动可移动位置，右键菜单可调整字号、回到主界面或关闭；位置与样式会自动记忆。",
                12, "TextMuted");
            lyricHint.TextWrapping = TextWrapping.Wrap;
            lyricHint.LineHeight = 22;

            return Card("关于", version, tech, shortcutTitle, shortcuts, lyricTitle, lyricHint);
        }

        private void SetTheme(string theme)
        {
            if (Theme.Current == theme) return;
            main.ToggleTheme();
            UpdateThemeButtons();
        }

        private void ApplyDesktopLyrics()
        {
            if (main.DesktopLyrics != null) main.DesktopLyrics.ApplySettings();
        }

        /// <summary>把界面控件同步成当前设置。</summary>
        public void Refresh()
        {
            AppSettings s = main.Settings;
            dirText.Text = string.IsNullOrEmpty(s.MusicDir) ? "（未设置）" : s.MusicDir;
            hiddenText.Text = "已从音乐库移除 " + s.Hidden.Count + " 首歌曲";
            recursiveCheck.IsChecked = s.Recursive;
            resumeCheck.IsChecked = s.ResumeLast;
            autoPlayCheck.IsChecked = s.AutoPlayOnStart;
            mediaKeysCheck.IsChecked = s.MediaKeys;
            translationCheck.IsChecked = s.LyricShowTranslation;
            lockCheck.IsChecked = s.LyricLocked;
            closeTrayCheck.IsChecked = s.CloseToTray;
            minimizeTrayCheck.IsChecked = s.MinimizeToTray;
            fontSizeSlider.Value = s.LyricFontSize;
            fontSizeLabel.Text = ((int)s.LyricFontSize) + " px";
            opacitySlider.Value = s.LyricOpacity * 100;
            opacityLabel.Text = ((int)(s.LyricOpacity * 100)) + "%";
            UpdateSwatches();
            UpdateThemeButtons();
            SyncMode();
        }

        private void SyncMode()
        {
            PlayMode mode = main.Settings.Mode;
            modeSequential.IsChecked = mode == PlayMode.Sequential;
            modeListLoop.IsChecked = mode == PlayMode.ListLoop;
            modeSingleLoop.IsChecked = mode == PlayMode.SingleLoop;
            modeShuffle.IsChecked = mode == PlayMode.Shuffle;
        }

        private void UpdateThemeButtons()
        {
            darkTheme.IsChecked = Theme.Current == "dark";
            lightTheme.IsChecked = Theme.Current == "light";
        }

        private void UpdateSwatches()
        {
            foreach (KeyValuePair<string, Border> pair in colorSwatches)
            {
                bool selected = string.Equals(pair.Key, main.Settings.LyricColor, StringComparison.OrdinalIgnoreCase);
                pair.Value.BorderBrush = selected
                    ? (Brush)Application.Current.Resources["Accent"]
                    : Brushes.Transparent;
            }
        }
    }
}
