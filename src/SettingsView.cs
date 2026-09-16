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
        private readonly TextBlock hiddenText = Ui.Text("", 12, "TextMuted");
        private readonly Slider fontSizeSlider = new Slider();
        private readonly Slider opacitySlider = new Slider();
        private readonly TextBlock fontSizeLabel = Ui.Text("", 12, "TextMuted");
        private readonly TextBlock opacityLabel = Ui.Text("", 12, "TextMuted");
        private readonly CheckBox resumeCheck = new CheckBox();
        private readonly CheckBox autoPlayCheck = new CheckBox();
        private readonly CheckBox mediaKeysCheck = new CheckBox();
        private readonly CheckBox translationCheck = new CheckBox();
        private readonly CheckBox lockCheck = new CheckBox();
        private readonly CheckBox closeTrayCheck = new CheckBox();
        private readonly CheckBox minimizeTrayCheck = new CheckBox();
        private readonly TextBox cloudUrlBox = new TextBox();
        private readonly TextBlock cloudStatus = Ui.Text("", 11.5, "TextMuted");
        private readonly TextBlock cacheInfo = Ui.Text("", 11.5, "TextMuted");
        private readonly CheckBox cloudCacheCheck = new CheckBox();
        private readonly StackPanel colorRow = new StackPanel();
        private readonly Dictionary<string, Border> colorSwatches = new Dictionary<string, Border>();
        private RadioButton darkTheme;
        private RadioButton lightTheme;
        private RadioButton systemTheme;
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
            page.HorizontalAlignment = HorizontalAlignment.Stretch;

            TextBlock title = Ui.Text("设置", 21, "Text", FontWeights.SemiBold);
            title.Margin = new Thickness(2, 0, 0, 14);
            page.Children.Add(title);

            // 布局：云端音乐库整行，中间两列，关于整行
            Grid columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions[1].Width = Ui.Px(18);
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            for (int i = 0; i < 4; i++) columns.RowDefinitions.Add(new RowDefinition());
            for (int i = 0; i < 4; i++) columns.RowDefinitions[i].Height = GridLength.Auto;

            Put(columns, BuildCloudCard(), 0, 0, 3);
            Put(columns, BuildPlayCard(), 0, 1, 1);
            Put(columns, BuildLyricCard(), 2, 1, 1);
            Put(columns, BuildAppearanceCard(), 0, 2, 1);
            Put(columns, BuildWindowCard(), 2, 2, 1);
            Put(columns, BuildAboutCard(), 0, 3, 3);
            page.Children.Add(columns);

            scroll.Content = page;
            Content = scroll;
            Refresh();
            main.SettingsChanged += delegate { Refresh(); };
        }

        private static void Put(Grid grid, UIElement element, int column, int row, int columnSpan)
        {
            Grid.SetColumn(element, column);
            Grid.SetRow(element, row);
            Grid.SetColumnSpan(element, columnSpan);
            grid.Children.Add(element);
        }

        /// <summary>标签在上、控件在下的行，适合分段按钮这类较宽的控件。</summary>
        private UIElement StackedRow(string label, string hint, UIElement control)
        {
            TextBlock name = Ui.Text(label, 13, "TextDim");
            control.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 0));
            StackPanel panel = Ui.Column(0, name);
            if (!string.IsNullOrEmpty(hint))
            {
                TextBlock h = Ui.Text(hint, 11.5, "TextMuted");
                h.Margin = new Thickness(0, 4, 0, 0);
                h.TextWrapping = TextWrapping.Wrap;
                panel.Children.Add(h);
            }
            panel.Children.Add(control);
            return panel;
        }

        private Border Card(string title, params UIElement[] rows)
        {
            Border card = new Border();
            card.Style = (Style)Application.Current.Resources["CardBox"];
            card.Margin = new Thickness(0, 0, 0, 14);
            card.Padding = new Thickness(20, 16, 20, 22);

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

        private UIElement BuildCloudCard()
        {
            cloudUrlBox.Style = (Style)Application.Current.Resources["InputBox"];
            cloudUrlBox.FontSize = 12.5;
            cloudUrlBox.TextChanged += delegate { main.SetCloudUrl(cloudUrlBox.Text); };

            Button test = Ui.Button("测试连接", "OutlineButton", delegate
            {
                cloudStatus.Text = "正在测试…";
                main.TestCloudConnection(cloudUrlBox.Text, delegate(bool ok, string message)
                {
                    cloudStatus.Text = message;
                });
            });
            Button browse = Ui.Button("打开云盘", "OutlineButton", delegate
            {
                try
                {
                    System.Diagnostics.Process.Start(CloudClient.ShareUrl(cloudUrlBox.Text));
                }
                catch (Exception)
                {
                }
            });
            Button refresh = Ui.Button("刷新列表", "OutlineButton", delegate { main.Rescan(); });

            Grid urlRow = new Grid();
            urlRow.ColumnDefinitions.Add(new ColumnDefinition());
            urlRow.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            urlRow.ColumnDefinitions.Add(new ColumnDefinition());
            urlRow.ColumnDefinitions[1].Width = GridLength.Auto;
            urlRow.Children.Add(cloudUrlBox);
            StackPanel buttons = Ui.Row(8, test, browse, refresh);
            buttons.Margin = new Thickness(10, 0, 0, 0);
            buttons.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(buttons, 1);
            urlRow.Children.Add(buttons);

            cloudStatus.Margin = new Thickness(0, 6, 0, 0);
            cloudStatus.TextWrapping = TextWrapping.Wrap;

            cloudCacheCheck.Style = (Style)Application.Current.Resources["ModernCheckBox"];
            cloudCacheCheck.Content = "播放时缓存到本地（缓存后可离线重听、即点即播）";
            cloudCacheCheck.Click += delegate
            {
                main.Settings.CloudCacheEnabled = cloudCacheCheck.IsChecked == true;
                main.SaveSettings();
                if (!main.Settings.CloudCacheEnabled)
                {
                    main.ClearCloudCache();
                    cacheInfo.Text = "缓存占用：0 MB";
                }
            };
            Button clearCache = Ui.Button("清理缓存", "OutlineButton", delegate
            {
                main.ClearCloudCache();
                cacheInfo.Text = CacheText();
            });
            StackPanel cacheRow = Ui.Row(12, cloudCacheCheck, cacheInfo, clearCache);

            TextBlock hint = Ui.Text(
                "把云盘的分享链接（形如 https://cloud.tsinghua.edu.cn/d/xxxxxxxxxxxx/）粘贴到上面，"
                + "保存后点「刷新列表」即可看到云端的歌：播放时会自动下载到本地缓存，"
                + "下次播放同一首就是本地播放；也会自动预取队列里的下一首。",
                11.5, "TextMuted");
            hint.TextWrapping = TextWrapping.Wrap;
            hint.LineHeight = 20;

            Button restore = Ui.Button("恢复全部", "OutlineButton", delegate
            {
                main.Settings.Hidden.Clear();
                main.SaveSettings();
                main.Rescan();
                main.ShowToast("已恢复被移除的歌曲");
            });
            StackPanel hiddenRow = Ui.Row(12, hiddenText, restore);

            return Card("云端音乐（清华云盘 / Seafile 分享链接）",
                Row("分享链接", "", urlRow),
                cloudStatus,
                cacheRow,
                hiddenRow,
                hint);
        }

        private static string CacheText()
        {
            long bytes = CloudCache.TotalSize();
            double mb = bytes / 1024.0 / 1024.0;
            return "缓存占用：" + (mb >= 1024 ? (mb / 1024).ToString("0.0") + " GB" : mb.ToString("0.0") + " MB")
                 + "（" + CloudCache.Count() + " 个文件）";
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

            return Card("播放", checks, StackedRow("默认播放模式", "", modes));
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
                StackedRow("歌词字号", "", fontRow),
                StackedRow("不透明度", "", opacityRow),
                StackedRow("歌词颜色", "", colorRow),
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
            systemTheme = new RadioButton();
            systemTheme.Content = "跟随系统";
            darkTheme = new RadioButton();
            darkTheme.Content = "深色";
            lightTheme = new RadioButton();
            lightTheme.Content = "浅色";
            StackPanel themes = Segmented(systemTheme, darkTheme, lightTheme);
            systemTheme.Click += delegate { SetTheme("system"); };
            darkTheme.Click += delegate { SetTheme("dark"); };
            lightTheme.Click += delegate { SetTheme("light"); };

            TextBlock hint = Ui.Text("跟随系统：自动匹配 Windows 的浅色 / 深色设置。", 11.5, "TextMuted");
            hint.TextWrapping = TextWrapping.Wrap;

            return Card("外观", StackedRow("主题", "", themes), hint);
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
            main.SetThemeMode(theme);
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
            hiddenText.Text = "已隐藏 " + s.Hidden.Count + " 首歌曲";
            resumeCheck.IsChecked = s.ResumeLast;
            autoPlayCheck.IsChecked = s.AutoPlayOnStart;
            mediaKeysCheck.IsChecked = s.MediaKeys;
            translationCheck.IsChecked = s.LyricShowTranslation;
            lockCheck.IsChecked = s.LyricLocked;
            closeTrayCheck.IsChecked = s.CloseToTray;
            minimizeTrayCheck.IsChecked = s.MinimizeToTray;
            if (cloudUrlBox.Text != (s.CloudUrl == null ? string.Empty : s.CloudUrl))
                cloudUrlBox.Text = s.CloudUrl == null ? string.Empty : s.CloudUrl;
            cloudCacheCheck.IsChecked = s.CloudCacheEnabled;
            cacheInfo.Text = CacheText();
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
            systemTheme.IsChecked = Theme.Mode == "system";
            darkTheme.IsChecked = Theme.Mode == "dark";
            lightTheme.IsChecked = Theme.Mode == "light";
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
