using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LightMusic
{
    /// <summary>软件内歌词页面（正在播放）。</summary>
    public class LyricsView : UserControl
    {
        private readonly MainWindow main;
        private readonly List<LyricLine> lines = new List<LyricLine>();
        private readonly List<FrameworkElement> lineElements = new List<FrameworkElement>();
        private readonly List<TextBlock> lineTexts = new List<TextBlock>();

        private readonly StackPanel lyricsPanel = new StackPanel();
        private readonly TranslateTransform lyricsTransform = new TranslateTransform();
        private readonly Border topSpacer = new Border();
        private readonly Border bottomSpacer = new Border();
        private readonly Grid viewport = new Grid();
        private readonly Border lyricsCard = new Border();
        private readonly TextBlock statusText = Ui.Text("", 13, "TextMuted");
        private readonly StackPanel statusPanel = new StackPanel();

        private readonly TextBlock songTitle = Ui.Text("未在播放", 22, "Text", FontWeights.SemiBold);
        private readonly TextBlock songArtist = Ui.Text("", 14, "TextDim");
        private readonly TextBlock songAlbum = Ui.Text("", 12, "TextMuted");
        private readonly TextBlock offsetLabel = Ui.Text("0.0 s", 12, "TextMuted");
        private readonly ToggleButton desktopToggle = new ToggleButton();
        private readonly CheckBox translationCheck = new CheckBox();

        private int activeIndex = -1;
        private double manualDelta;
        private DispatcherTimer manualTimer;
        private bool synced;

        public LyricsView(MainWindow owner)
        {
            main = owner;
            Padding = new Thickness(22, 16, 22, 8);

            Grid root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.ColumnDefinitions[0].Width = Ui.Px(330);
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.ColumnDefinitions[1].Width = Ui.Stars(1);

            // 左侧：封面与信息
            Border cover = new Border();
            cover.Width = 250;
            cover.Height = 250;
            cover.CornerRadius = new CornerRadius(22);
            cover.Background = new LinearGradientBrush(
                Parse("#4F6DF5"), Parse("#8B5CF6"), new Point(0, 0), new Point(1, 1));
            cover.HorizontalAlignment = HorizontalAlignment.Left;
            cover.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 26,
                ShadowDepth = 6,
                Opacity = 0.35,
                Color = Colors.Black
            };
            Canvas coverIcon = Icons.Create("music", 96, "OnAccent");
            cover.Child = coverIcon;

            songTitle.Margin = new Thickness(0, 22, 0, 6);
            songTitle.TextWrapping = TextWrapping.Wrap;
            songArtist.Margin = new Thickness(0, 0, 0, 4);
            songAlbum.Margin = new Thickness(0, 0, 0, 16);

            desktopToggle.Style = (Style)Application.Current.Resources["ToggleIconButton"];
            desktopToggle.Content = Icons.Create("monitor", 18, "TextDim");
            desktopToggle.ToolTip = "桌面歌词开关";
            desktopToggle.IsChecked = main.Settings.DesktopLyricsOn;
            desktopToggle.Click += delegate { main.ShowDesktopLyrics(desktopToggle.IsChecked == true); };

            Button offsetMinus = Ui.IconButton("minimize", 16, "歌词提前 0.5 秒", delegate { AddOffset(-0.5); });
            Button offsetPlus = Ui.IconButton("plus", 16, "歌词延后 0.5 秒", delegate { AddOffset(0.5); });

            translationCheck.Style = (Style)Application.Current.Resources["ModernCheckBox"];
            translationCheck.Content = "显示翻译";
            translationCheck.IsChecked = main.Settings.LyricShowTranslation;
            translationCheck.VerticalAlignment = VerticalAlignment.Center;
            translationCheck.Click += delegate
            {
                main.Settings.LyricShowTranslation = translationCheck.IsChecked == true;
                main.SaveSettings();
                ApplyTranslationVisibility();
                if (main.DesktopLyrics != null) main.DesktopLyrics.UpdateNow();
            };

            StackPanel adjust = Ui.Row(4, desktopToggle, offsetMinus, offsetLabel, offsetPlus, translationCheck);
            adjust.Margin = new Thickness(0, 2, 0, 0);

            StackPanel left = Ui.Column(0, cover, songTitle, songArtist, songAlbum, adjust);
            left.HorizontalAlignment = HorizontalAlignment.Left;
            root.Children.Add(left);

            // 右侧：歌词
            lyricsCard.Style = (Style)Application.Current.Resources["CardBox"];
            lyricsCard.Padding = new Thickness(6, 0, 6, 0);
            lyricsCard.Margin = new Thickness(26, 0, 0, 0);

            viewport.ClipToBounds = true;
            lyricsPanel.RenderTransform = lyricsTransform;
            lyricsPanel.VerticalAlignment = VerticalAlignment.Top;
            viewport.Children.Add(lyricsPanel);

            statusPanel.HorizontalAlignment = HorizontalAlignment.Center;
            statusPanel.VerticalAlignment = VerticalAlignment.Center;
            Canvas statusIcon = Icons.Create("lyrics", 34, "TextMuted");
            statusIcon.HorizontalAlignment = HorizontalAlignment.Center;
            statusText.HorizontalAlignment = HorizontalAlignment.Center;
            statusText.TextAlignment = TextAlignment.Center;
            statusText.Margin = new Thickness(0, 10, 0, 0);
            statusPanel.Children.Add(statusIcon);
            statusPanel.Children.Add(statusText);
            statusPanel.IsHitTestVisible = false;
            viewport.Children.Add(statusPanel);

            lyricsCard.Child = viewport;
            Grid.SetColumn(lyricsCard, 1);
            root.Children.Add(lyricsCard);

            viewport.SizeChanged += delegate { UpdateSpacers(); ScrollToActive(false); };
            IsVisibleChanged += delegate
            {
                if (!IsVisible) return;
                Dispatcher.BeginInvoke((Action)delegate
                {
                    UpdateSpacers();
                    ScrollToActive(false);
                }, DispatcherPriority.Loaded);
            };
            viewport.MouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                manualDelta -= e.Delta * 0.6;
                ClampManual();
                ScrollToActive(false);
                RestartManualTimer();
                e.Handled = true;
            };

            Content = root;
            UpdateOffsetLabel();
            Load(null);
        }

        public int CurrentIndex
        {
            get { return activeIndex; }
        }

        public List<LyricLine> Lines
        {
            get { return lines; }
        }

        public bool Synced
        {
            get { return synced; }
        }

        private static Color Parse(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        private void AddOffset(double delta)
        {
            main.Settings.LyricOffset += delta;
            if (main.Settings.LyricOffset > 10) main.Settings.LyricOffset = 10;
            if (main.Settings.LyricOffset < -10) main.Settings.LyricOffset = -10;
            main.SaveSettings();
            UpdateOffsetLabel();
            activeIndex = -2;
        }

        private void UpdateOffsetLabel()
        {
            offsetLabel.Text = (main.Settings.LyricOffset >= 0 ? "+" : "") +
                               main.Settings.LyricOffset.ToString("0.0") + " s";
        }

        /// <summary>根据播放位置找当前歌词行。</summary>
        public int IndexAt(double seconds)
        {
            if (!synced || lines.Count == 0) return -1;
            int lo = 0;
            int hi = lines.Count - 1;
            int result = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (lines[mid].Time <= seconds)
                {
                    result = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return result;
        }

        public void SetActive(int index)
        {
            if (index == activeIndex) return;
            if (activeIndex >= 0 && activeIndex < lineTexts.Count)
            {
                lineTexts[activeIndex].SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
                lineTexts[activeIndex].FontWeight = FontWeights.Normal;
            }
            activeIndex = index;
            if (activeIndex >= 0 && activeIndex < lineTexts.Count)
            {
                lineTexts[activeIndex].SetResourceReference(TextBlock.ForegroundProperty, "Text");
                lineTexts[activeIndex].FontWeight = FontWeights.SemiBold;
                manualDelta = 0;
                ScrollToActive(true);
            }
        }

        /// <summary>载入指定歌曲的歌词。</summary>
        public void Load(Song song)
        {
            lines.Clear();
            lineElements.Clear();
            lineTexts.Clear();
            lyricsPanel.Children.Clear();
            topSpacer.Height = 120;
            bottomSpacer.Height = 120;
            activeIndex = -1;

            songTitle.Text = song == null ? "未在播放" : song.Title;
            songArtist.Text = song == null ? "从音乐库中选择一首歌开始" : song.ArtistText;
            songAlbum.Text = song == null ? "" : song.FileName;

            if (song == null)
            {
                synced = false;
                statusText.Text = "还没有正在播放的歌曲";
                statusPanel.Visibility = Visibility.Visible;
                UpdateSpacers();
                return;
            }

            LyricDocument doc = LrcParser.Load(song.LyricPath);
            if (!doc.Found || doc.Lines.Count == 0)
            {
                synced = false;
                statusText.Text = (string.IsNullOrEmpty(song.LyricPath)
                    ? "这首歌没有找到歌词文件"
                    : doc.Message) + "\n\n把同名 .lrc 歌词放到歌曲同目录即可\n文件名：" + System.IO.Path.GetFileNameWithoutExtension(song.FileName) + ".lrc";
                statusPanel.Visibility = Visibility.Visible;
                UpdateSpacers();
                return;
            }

            statusPanel.Visibility = Visibility.Collapsed;
            synced = doc.Synced;
            lines.AddRange(doc.Lines);

            foreach (LyricLine line in lines)
            {
                StackPanel item = new StackPanel();
                item.Margin = new Thickness(6, 3, 6, 3);
                item.Cursor = Cursors.Hand;
                item.Background = Brushes.Transparent;

                TextBlock text = Ui.Text(line.Text, 16.5, "TextDim");
                text.TextWrapping = TextWrapping.Wrap;
                text.LineHeight = 26;
                item.Children.Add(text);

                if (line.HasTranslation)
                {
                    TextBlock translation = Ui.Text(line.Translation, 13, "TextMuted");
                    translation.TextWrapping = TextWrapping.Wrap;
                    translation.Opacity = 0.75;
                    translation.Margin = new Thickness(0, 2, 0, 0);
                    item.Tag = translation;
                    item.Children.Add(translation);
                }

                LyricLine captured = line;
                item.MouseLeftButtonUp += delegate
                {
                    if (!synced || captured.Time < 0) return;
                    main.SeekTo(captured.Time - main.Settings.LyricOffset);
                };

                lyricsPanel.Children.Add(item);
                lineElements.Add(item);
                lineTexts.Add(text);
            }

            ApplyTranslationVisibility();
            lyricsPanel.Children.Insert(0, topSpacer);
            lyricsPanel.Children.Add(bottomSpacer);
            UpdateSpacers();
            lyricsTransform.Y = 0;
            Dispatcher.BeginInvoke((Action)delegate
            {
                UpdateSpacers();
                ScrollToActive(false);
            }, DispatcherPriority.Loaded);
        }

        private void ApplyTranslationVisibility()
        {
            bool show = main.Settings.LyricShowTranslation;
            for (int i = 0; i < lines.Count; i++)
            {
                TextBlock translation = lineElements[i].Tag as TextBlock;
                if (translation != null) translation.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateSpacers()
        {
            double pad = Math.Max(60, viewport.ActualHeight / 2 - 30);
            topSpacer.Height = pad;
            bottomSpacer.Height = pad;
        }

        private void ClampManual()
        {
            double max = viewport.ActualHeight * 0.8;
            if (manualDelta > max) manualDelta = max;
            if (manualDelta < -max) manualDelta = -max;
        }

        private void RestartManualTimer()
        {
            if (manualTimer == null)
            {
                manualTimer = new DispatcherTimer();
                manualTimer.Interval = TimeSpan.FromSeconds(5);
                manualTimer.Tick += delegate
                {
                    manualTimer.Stop();
                    manualDelta = 0;
                    ScrollToActive(true);
                };
            }
            manualTimer.Stop();
            manualTimer.Start();
        }

        private void ScrollToActive(bool animate)
        {
            if (activeIndex < 0 || activeIndex >= lineElements.Count) return;
            FrameworkElement element = lineElements[activeIndex];
            double top;
            try
            {
                top = element.TransformToAncestor(lyricsPanel).Transform(new Point(0, 0)).Y;
            }
            catch (Exception)
            {
                return;
            }
            double target = viewport.ActualHeight / 2 - (top + element.ActualHeight / 2) + manualDelta;

            if (animate)
            {
                DoubleAnimation animation = new DoubleAnimation(lyricsTransform.Y, target,
                    TimeSpan.FromMilliseconds(360));
                animation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                lyricsTransform.BeginAnimation(TranslateTransform.YProperty, animation);
            }
            else
            {
                lyricsTransform.BeginAnimation(TranslateTransform.YProperty, null);
                lyricsTransform.Y = target;
            }
        }

        public void SyncDesktopToggle()
        {
            desktopToggle.IsChecked = main.Settings.DesktopLyricsOn;
        }
    }
}
