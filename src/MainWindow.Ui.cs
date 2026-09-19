using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Skylark
{
    public partial class MainWindow
    {
        #region 界面构建

        private void BuildUi()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);

            Grid shell = new Grid();
            shell.ColumnDefinitions.Add(new ColumnDefinition());
            shell.ColumnDefinitions[0].Width = Ui.Px(224);
            shell.ColumnDefinitions.Add(new ColumnDefinition());
            shell.ColumnDefinitions[1].Width = Ui.Stars(1);

            shell.Children.Add(BuildSidebar());

            contentHost = new Grid();
            contentHost.Margin = new Thickness(0);
            libraryView = new LibraryView(this);
            queueView = new QueueView(this);
            lyricsView = new LyricsView(this);
            settingsView = new SettingsView(this);
            contentHost.Children.Add(libraryView);
            contentHost.Children.Add(queueView);
            contentHost.Children.Add(lyricsView);
            contentHost.Children.Add(settingsView);
            Grid.SetColumn(contentHost, 1);
            shell.Children.Add(contentHost);

            root.Children.Add(shell);

            Grid outer = new Grid();
            outer.RowDefinitions.Add(new RowDefinition());
            outer.RowDefinitions[0].Height = GridLength.Auto;
            outer.RowDefinitions.Add(new RowDefinition());
            outer.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            outer.RowDefinitions.Add(new RowDefinition());
            outer.RowDefinitions[2].Height = GridLength.Auto;

            UIElement topBar = BuildTopBar();
            outer.Children.Add(topBar);
            Grid.SetRow(root, 1);
            outer.Children.Add(root);
            UIElement playerBar = BuildPlayerBar();
            Grid.SetRow(playerBar, 2);
            outer.Children.Add(playerBar);

            Grid host = new Grid();
            Ui.Bind(host, Panel.BackgroundProperty, "Window");
            host.Children.Add(outer);
            modalHost = new Grid();
            modalHost.Visibility = Visibility.Collapsed;
            host.Children.Add(modalHost);
            toast = BuildToast();
            host.Children.Add(toast);
            Content = host;

            ApplyFilter();
            ShowView(string.IsNullOrEmpty(settings.LastView) ? "library" : settings.LastView);
            UpdateModeButton();
            UpdateVolumeSlider();
            UpdateQueueState();
        }

        private UIElement BuildTopBar()
        {
            Border bar = new Border();
            bar.Height = 58;
            Ui.Bind(bar, Border.BackgroundProperty, "Panel");
            bar.BorderThickness = new Thickness(0, 0, 0, 1);
            Ui.Bind(bar, Border.BorderBrushProperty, "Border");

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions[0].Width = GridLength.Auto;
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions[2].Width = GridLength.Auto;

            // logo + 名称
            Border logo = new Border();
            logo.Width = 32;
            logo.Height = 32;
            logo.CornerRadius = new CornerRadius(10);
            logo.Background = new LinearGradientBrush(
                ParseColor("#6366F1"), ParseColor("#A855F7"), new Point(0, 0), new Point(1, 1));
            Canvas logoIcon = Icons.Create("music", 18, "OnAccent");
            logo.Child = logoIcon;

            TextBlock appName = Ui.Text(AppName, 15, "Text", FontWeights.SemiBold);
            appName.VerticalAlignment = VerticalAlignment.Center;
            TextBlock slogan = Ui.Text("Skylark", 10.5, "TextMuted");
            slogan.VerticalAlignment = VerticalAlignment.Center;
            StackPanel nameColumn = Ui.Column(1, appName, slogan);
            nameColumn.VerticalAlignment = VerticalAlignment.Center;

            StackPanel brand = Ui.Row(10, logo, nameColumn);
            brand.Margin = new Thickness(18, 0, 12, 0);
            grid.Children.Add(brand);

            // 搜索框
            Border searchBorder = new Border();
            searchBorder.Width = 400;
            searchBorder.Height = 36;
            searchBorder.CornerRadius = new CornerRadius(10);
            searchBorder.Padding = new Thickness(11, 0, 11, 0);
            searchBorder.HorizontalAlignment = HorizontalAlignment.Center;
            searchBorder.VerticalAlignment = VerticalAlignment.Center;
            Ui.Bind(searchBorder, Border.BackgroundProperty, "Card");
            Ui.Bind(searchBorder, Border.BorderBrushProperty, "Border");
            searchBorder.BorderThickness = new Thickness(1);

            searchBox = new TextBox();
            searchBox.Style = (Style)Application.Current.Resources["SearchBox"];
            searchBox.VerticalAlignment = VerticalAlignment.Center;
            searchBox.Width = 330;
            searchBox.TextChanged += delegate { SetSearch(searchBox.Text); UpdateSearchPlaceholder(); };

            searchPlaceholder = Ui.Text("搜索歌曲、歌手（按 F 快速定位）", 13, "TextMuted");
            searchPlaceholder.VerticalAlignment = VerticalAlignment.Center;
            searchPlaceholder.IsHitTestVisible = false;

            Grid searchInner = new Grid();
            searchInner.ColumnDefinitions.Add(new ColumnDefinition());
            searchInner.ColumnDefinitions[0].Width = GridLength.Auto;
            searchInner.ColumnDefinitions.Add(new ColumnDefinition());
            searchInner.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            Canvas searchIcon = Icons.Create("search", 15, "TextMuted");
            searchIcon.VerticalAlignment = VerticalAlignment.Center;
            searchInner.Children.Add(searchIcon);
            Grid.SetColumn(searchBox, 1);
            searchInner.Children.Add(searchBox);
            Grid.SetColumn(searchPlaceholder, 1);
            searchPlaceholder.Margin = new Thickness(1, 0, 0, 0);
            searchInner.Children.Add(searchPlaceholder);
            searchBorder.Child = searchInner;
            Grid.SetColumn(searchBorder, 1);
            grid.Children.Add(searchBorder);

            // 右侧按钮
            Button themeButton = Ui.RoundButton("theme", 18, "切换深色 / 浅色主题", delegate { ToggleTheme(); });
            Button dirButton = Ui.RoundButton("folder-open", 18, "打开音乐文件夹", delegate { OpenMusicDir(); });
            Button rescanButton = Ui.RoundButton("refresh", 18, "重新扫描音乐库", delegate { Rescan(); });

            lyricsToggle = new ToggleButton();
            lyricsToggle.Style = (Style)Application.Current.Resources["ToggleIconButton"];
            lyricsToggle.Content = Icons.Create("lyrics", 18, "TextDim");
            lyricsToggle.ToolTip = "桌面歌词开关";
            lyricsToggle.Click += delegate { ShowDesktopLyrics(lyricsToggle.IsChecked == true); };

            lockToggle = new ToggleButton();
            lockToggle.Style = (Style)Application.Current.Resources["ToggleIconButton"];
            lockToggle.Content = Icons.Create("lock", 18, "TextDim");
            lockToggle.ToolTip = "锁定桌面歌词（鼠标穿透，不影响操作电脑）Ctrl+Alt+L";
            lockToggle.IsChecked = settings.LyricLocked;
            lockToggle.Click += delegate { SetLyricLocked(lockToggle.IsChecked == true, true); };

            Button settingsButton = Ui.RoundButton("settings", 18, "设置", delegate { ShowView("settings"); });

            StackPanel actions = Ui.Row(2, themeButton, dirButton, rescanButton, lyricsToggle, lockToggle, settingsButton);
            actions.Margin = new Thickness(12, 0, 14, 0);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            bar.Child = grid;
            return bar;
        }

        private UIElement BuildSidebar()
        {
            Border side = new Border();
            Ui.Bind(side, Border.BackgroundProperty, "Panel");
            side.Padding = new Thickness(12, 10, 12, 12);
            side.BorderThickness = new Thickness(0, 0, 1, 0);
            Ui.Bind(side, Border.BorderBrushProperty, "Border");

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions[0].Height = GridLength.Auto;
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions[2].Height = GridLength.Auto;

            StackPanel nav = new StackPanel();
            nav.Children.Add(BuildNavItem("library", "library", "音乐库", out libraryCountText));
            nav.Children.Add(BuildNavItem("queue", "queue", "播放队列", out queueCountText));
            TextBlock unused1;
            TextBlock unused2;
            nav.Children.Add(BuildNavItem("lyrics", "lyrics", "歌词", out unused1));
            nav.Children.Add(BuildNavItem("settings", "settings", "设置", out unused2));
            grid.Children.Add(nav);

            // 歌单区（云盘上的一个文件夹就是一个歌单）
            ScrollViewer playlistScroll = new ScrollViewer();
            playlistScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            playlistScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            playlistScroll.Content = playlistNav;
            playlistScroll.Margin = new Thickness(0, 2, 0, 6);
            Grid.SetRow(playlistScroll, 1);
            grid.Children.Add(playlistScroll);

            Border stats = new Border();
            stats.CornerRadius = new CornerRadius(12);
            stats.Padding = new Thickness(12);
            Ui.Bind(stats, Border.BackgroundProperty, "Card");
            stats.Margin = new Thickness(0, 0, 0, 0);
            stats.VerticalAlignment = VerticalAlignment.Bottom;

            statusText = Ui.Text("未扫描", 11.5, "TextMuted");
            statusText.TextWrapping = TextWrapping.Wrap;
            dirLabel = Ui.Text("", 11, "TextMuted");
            dirLabel.TextWrapping = TextWrapping.Wrap;
            dirLabel.Margin = new Thickness(0, 6, 0, 0);

            StackPanel statPanel = Ui.Column(2, statusText, dirLabel);
            stats.Child = statPanel;
            Grid.SetRow(stats, 2);
            grid.Children.Add(stats);

            side.Child = grid;
            return side;
        }

        private UIElement BuildNavItem(string view, string icon, string label, out TextBlock countText)
        {
            ToggleButton toggle = new ToggleButton();
            toggle.Style = (Style)Application.Current.Resources["NavToggle"];
            toggle.Tag = view;

            Canvas iconCanvas = Icons.Create(icon, 18, "TextDim");
            iconCanvas.VerticalAlignment = VerticalAlignment.Center;
            TextBlock text = Ui.Text(label, 13.5, "TextDim");
            text.VerticalAlignment = VerticalAlignment.Center;

            Grid content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.ColumnDefinitions[0].Width = GridLength.Auto;
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.ColumnDefinitions[2].Width = GridLength.Auto;
            content.Children.Add(iconCanvas);
            Grid.SetColumn(text, 1);
            text.Margin = new Thickness(10, 0, 0, 0);
            content.Children.Add(text);

            countText = Ui.Text("", 11.5, "TextMuted");
            countText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(countText, 2);
            content.Children.Add(countText);

            toggle.Content = content;
            toggle.Tag = view;
            toggle.Click += delegate { ShowView((string)toggle.Tag); };
            navToggles[view] = toggle;
            return toggle;
        }

        private UIElement BuildPlayerBar()
        {
            Border bar = new Border();
            bar.Height = 94;
            Ui.Bind(bar, Border.BackgroundProperty, "Panel");
            bar.BorderThickness = new Thickness(0, 1, 0, 0);
            Ui.Bind(bar, Border.BorderBrushProperty, "Border");

            Grid grid = new Grid();
            grid.Margin = new Thickness(18, 10, 18, 12);
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions[0].Width = Ui.Px(250);
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions[1].Width = Ui.Stars(1);
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions[2].Width = Ui.Px(250);

            // 左：封面 + 歌曲信息
            Border cover = new Border();
            cover.Width = 54;
            cover.Height = 54;
            cover.CornerRadius = new CornerRadius(12);
            cover.Background = new LinearGradientBrush(
                ParseColor("#4F6DF5"), ParseColor("#8B5CF6"), new Point(0, 0), new Point(1, 1));
            Canvas coverIcon = Icons.Create("music", 26, "OnAccent");
            cover.Child = coverIcon;

            titleText = Ui.Text("未在播放", 13.5, "Text", FontWeights.SemiBold);
            artistText = Ui.Text("双击列表中的歌曲开始播放", 11.5, "TextMuted");
            artistText.Margin = new Thickness(0, 3, 0, 0);
            StackPanel info = Ui.Column(0, titleText, artistText);
            info.VerticalAlignment = VerticalAlignment.Center;
            info.Margin = new Thickness(12, 0, 0, 0);
            info.Cursor = Cursors.Hand;
            info.MouseLeftButtonUp += delegate { ShowView("lyrics"); };

            StackPanel left = Ui.Row(0, cover, info);
            left.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(left);

            // 中：进度 + 控制
            positionText = Ui.Text("0:00", 11.5, "TextMuted");
            positionText.VerticalAlignment = VerticalAlignment.Center;
            positionText.HorizontalAlignment = HorizontalAlignment.Right;
            positionText.Width = 44;
            durationText = Ui.Text("0:00", 11.5, "TextMuted");
            durationText.VerticalAlignment = VerticalAlignment.Center;
            durationText.HorizontalAlignment = HorizontalAlignment.Left;
            durationText.Width = 44;

            progressSlider = new Slider();
            progressSlider.Style = (Style)Application.Current.Resources["FlatSlider"];
            progressSlider.Minimum = 0;
            progressSlider.Maximum = 1;
            progressSlider.SmallChange = 5;
            progressSlider.LargeChange = 15;
            progressSlider.VerticalAlignment = VerticalAlignment.Center;
            progressSlider.PreviewMouseLeftButtonDown += delegate { draggingProgress = true; };
            progressSlider.PreviewMouseLeftButtonUp += delegate
            {
                draggingProgress = false;
                SeekTo(progressSlider.Value);
            };
            progressSlider.ValueChanged += delegate
            {
                if (draggingProgress)
                {
                    if (durationText != null && Duration > 0)
                        durationText.Text = "/ " + TextUtil.FormatTime(progressSlider.Value);
                }
            };

            Grid progressRow = new Grid();
            progressRow.ColumnDefinitions.Add(new ColumnDefinition());
            progressRow.ColumnDefinitions[0].Width = GridLength.Auto;
            progressRow.ColumnDefinitions.Add(new ColumnDefinition());
            progressRow.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            progressRow.ColumnDefinitions.Add(new ColumnDefinition());
            progressRow.ColumnDefinitions[2].Width = GridLength.Auto;
            progressRow.Children.Add(positionText);
            Grid.SetColumn(progressSlider, 1);
            progressSlider.Margin = new Thickness(10, 0, 10, 0);
            progressRow.Children.Add(progressSlider);
            Grid.SetColumn(durationText, 2);
            progressRow.Children.Add(durationText);

            modeButton = Ui.RoundButton("repeat", 18, "播放模式", delegate { CycleMode(); });

            Button prevButton = Ui.RoundButton("prev", 20, "上一首（Ctrl+←）", delegate { Previous(); });
            Button nextButton = Ui.RoundButton("next", 20, "下一首（Ctrl+→）", delegate { Next(); });

            playButton = new Button();
            playButton.Width = 46;
            playButton.Height = 46;
            playButton.Style = (Style)Application.Current.Resources["GhostButton"];
            playButton.Padding = new Thickness(0);
            playButton.Template = CreateCircleButtonTemplate();
            playButton.Content = Icons.Create("play", 20, "OnAccent");
            playButton.ToolTip = "播放 / 暂停（空格）";
            playButton.Click += delegate { TogglePlay(); };

            queueButton = Ui.RoundButton("queue", 18, "播放队列", delegate { ShowView("queue"); });

            StackPanel transport = Ui.Row(8, modeButton, prevButton, playButton, nextButton, queueButton);
            transport.HorizontalAlignment = HorizontalAlignment.Center;
            transport.Margin = new Thickness(0, 8, 0, 0);

            StackPanel center = Ui.Column(0, progressRow, transport);
            center.VerticalAlignment = VerticalAlignment.Center;
            center.Margin = new Thickness(18, 0, 18, 0);
            Grid.SetColumn(center, 1);
            grid.Children.Add(center);

            // 右：音量
            Button muteButton = Ui.RoundButton("volume", 18, "静音", delegate
            {
                engine.IsMuted = !engine.IsMuted;
                UpdateVolumeSlider();
                SettingsChangedSafe();
            });

            volumeSlider = new Slider();
            volumeSlider.Style = (Style)Application.Current.Resources["FlatSlider"];
            volumeSlider.Minimum = 0;
            volumeSlider.Maximum = 100;
            volumeSlider.Width = 110;
            volumeSlider.ValueChanged += delegate
            {
                engine.Volume = volumeSlider.Value / 100.0;
                if (engine.Volume > 0 && engine.IsMuted)
                {
                    engine.IsMuted = false;
                    UpdateMuteIcon(muteButton);
                }
                SaveSettingsDebounced();
            };
            volumeSlider.VerticalAlignment = VerticalAlignment.Center;

            StackPanel right = Ui.Row(2, muteButton, volumeSlider);
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            // 云端缓冲进度条：贴在播放条底部，出现时不改变布局
            Grid barHost = new Grid();
            barHost.Children.Add(grid);
            bufferingBar = new ProgressBar();
            bufferingBar.Minimum = 0;
            bufferingBar.Maximum = 100;
            bufferingBar.Height = 3;
            bufferingBar.Visibility = Visibility.Collapsed;
            bufferingBar.VerticalAlignment = VerticalAlignment.Bottom;
            bufferingBar.HorizontalAlignment = HorizontalAlignment.Stretch;
            bufferingBar.BorderThickness = new Thickness(0);
            Ui.Bind(bufferingBar, ProgressBar.BackgroundProperty, "Panel");
            Ui.Bind(bufferingBar, ProgressBar.ForegroundProperty, "Accent");
            barHost.Children.Add(bufferingBar);

            bar.Child = barHost;
            return bar;
        }

        private ControlTemplate CreateCircleButtonTemplate()
        {
            string xaml =
                "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"Button\">" +
                "<Border x:Name=\"bd\" CornerRadius=\"23\" Background=\"{DynamicResource Accent}\">" +
                "<ContentPresenter HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/>" +
                "</Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property=\"IsMouseOver\" Value=\"True\">" +
                "<Setter TargetName=\"bd\" Property=\"Background\" Value=\"{DynamicResource AccentHover}\"/>" +
                "</Trigger>" +
                "<Trigger Property=\"IsPressed\" Value=\"True\">" +
                "<Setter TargetName=\"bd\" Property=\"Background\" Value=\"{DynamicResource AccentPressed}\"/>" +
                "</Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate>";
            System.IO.MemoryStream stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(xaml));
            return (ControlTemplate)System.Windows.Markup.XamlReader.Load(stream);
        }

        private Border BuildToast()
        {
            Border border = new Border();
            border.Visibility = Visibility.Collapsed;
            border.Opacity = 0;
            border.HorizontalAlignment = HorizontalAlignment.Center;
            border.VerticalAlignment = VerticalAlignment.Top;
            border.Margin = new Thickness(0, 74, 0, 0);
            border.CornerRadius = new CornerRadius(12);
            border.Padding = new Thickness(18, 10, 18, 10);
            Ui.Bind(border, Border.BackgroundProperty, "MenuBg");
            border.BorderThickness = new Thickness(1);
            Ui.Bind(border, Border.BorderBrushProperty, "Border");
            border.IsHitTestVisible = false;
            border.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 2,
                Opacity = 0.35,
                Color = Colors.Black
            };
            toastText = Ui.Text("", 13, "Text");
            border.Child = toastText;
            return border;
        }

        #endregion
    }
}
