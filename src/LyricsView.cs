using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LightMusic
{
    /// <summary>软件内歌词页（正在播放）：左侧唱片卡片 + 右侧逐行歌词。</summary>
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
        private readonly StackPanel statusPanel = new StackPanel();
        private readonly TextBlock statusText = Ui.Text("", 13, "TextMuted");
        private readonly Button backButton = new Button();
        private readonly Border rightCard = new Border();

        private readonly TextBlock songTitle = Ui.Text("未在播放", 21, "Text", FontWeights.SemiBold);
        private readonly TextBlock songArtist = Ui.Text("", 13, "TextDim");
        private readonly TextBlock songFile = Ui.Text("", 11.5, "TextMuted");
        private readonly TextBlock offsetLabel = Ui.Text("+0.0 s", 12, "TextMuted");
        private readonly ToggleButton desktopToggle = new ToggleButton();
        private readonly ToggleButton lockToggle = new ToggleButton();
        private readonly CheckBox translationCheck = new CheckBox();

        private int activeIndex = -1;
        private double manualDelta;
        private DispatcherTimer manualTimer;
        private bool synced;

        public LyricsView(MainWindow owner)
        {
            main = owner;
            Padding = new Thickness(24, 16, 24, 16);

            Grid root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.ColumnDefinitions[0].Width = Ui.Px(330);
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.ColumnDefinitions[1].Width = Ui.Stars(1);

            root.Children.Add(BuildInfoCard());

            rightCard.Style = (Style)Application.Current.Resources["CardBox"];
            rightCard.Padding = new Thickness(10, 0, 10, 0);
            rightCard.Margin = new Thickness(18, 0, 0, 0);
            rightCard.Child = BuildLyricsArea();
            Grid.SetColumn(rightCard, 1);
            root.Children.Add(rightCard);

            Content = root;
            UpdateOffsetLabel();
            SyncToggles();
            Load(null);

            main.SettingsChanged += delegate { SyncToggles(); UpdateOffsetLabel(); };
        }

        #region 左侧信息卡片

        private UIElement BuildInfoCard()
        {
            Border card = new Border();
            card.Style = (Style)Application.Current.Resources["CardBox"];
            card.Padding = new Thickness(22, 24, 22, 20);

            Border cover = new Border();
            cover.Width = 236;
            cover.Height = 236;
            cover.CornerRadius = new CornerRadius(22);
            cover.HorizontalAlignment = HorizontalAlignment.Center;
            cover.Background = new LinearGradientBrush(
                Parse("#5B6CFF"), Parse("#A855F7"), new Point(0, 0), new Point(1, 1));
            cover.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 30,
                ShadowDepth = 8,
                Opacity = 0.45,
                Color = Colors.Black
            };
            Grid coverContent = new Grid();
            Ellipse ring = new Ellipse();
            ring.Width = 150;
            ring.Height = 150;
            ring.Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
            ring.StrokeThickness = 1.5;
            coverContent.Children.Add(ring);
            Canvas note = Icons.Create("music", 84, "OnAccent");
            note.HorizontalAlignment = HorizontalAlignment.Center;
            note.VerticalAlignment = VerticalAlignment.Center;
            coverContent.Children.Add(note);
            cover.Child = coverContent;

            songTitle.Margin = new Thickness(0, 20, 0, 6);
            songTitle.TextWrapping = TextWrapping.Wrap;
            songTitle.TextAlignment = TextAlignment.Center;
            songArtist.TextAlignment = TextAlignment.Center;
            songArtist.Margin = new Thickness(0, 0, 0, 4);
            songArtist.TextWrapping = TextWrapping.Wrap;
            songFile.TextAlignment = TextAlignment.Center;
            songFile.TextWrapping = TextWrapping.Wrap;
            songFile.Margin = new Thickness(0, 0, 0, 18);

            StackPanel info = new StackPanel();
            info.VerticalAlignment = VerticalAlignment.Center;
            info.Children.Add(cover);
            info.Children.Add(songTitle);
            info.Children.Add(songArtist);
            info.Children.Add(songFile);

            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions[1].Height = GridLength.Auto;
            layout.Children.Add(info);
            UIElement controls = BuildControls();
            Grid.SetRow(controls, 1);
            layout.Children.Add(controls);
            card.Child = layout;
            return card;
        }

        private static Border FillCard(Border card, params UIElement[] children)
        {
            StackPanel panel = new StackPanel();
            foreach (UIElement child in children) panel.Children.Add(child);
            card.Child = panel;
            return card;
        }

        private UIElement BuildControls()
        {
            desktopToggle.Style = (Style)Application.Current.Resources["ToggleIconButton"];
            desktopToggle.Content = Icons.Create("monitor", 18, "TextDim");
            desktopToggle.ToolTip = "桌面歌词开关（D）";
            desktopToggle.Click += delegate { main.ShowDesktopLyrics(desktopToggle.IsChecked == true); };

            lockToggle.Style = (Style)Application.Current.Resources["ToggleIconButton"];
            lockToggle.Content = Icons.Create("lock", 18, "TextDim");
            lockToggle.ToolTip = "锁定桌面歌词（鼠标穿透，Ctrl+Alt+L）";
            lockToggle.Click += delegate { main.SetLyricLocked(lockToggle.IsChecked == true, true); };

            translationCheck.Style = (Style)Application.Current.Resources["ModernCheckBox"];
            translationCheck.Content = "显示翻译";
            translationCheck.VerticalAlignment = VerticalAlignment.Center;
            translationCheck.Click += delegate
            {
                main.Settings.LyricShowTranslation = translationCheck.IsChecked == true;
                main.SaveSettings();
                ApplyTranslationVisibility();
                if (main.DesktopLyrics != null) main.DesktopLyrics.ApplySettings();
            };

            Button minus = Ui.IconButton("minimize", 15, "歌词提前 0.5 秒", delegate { AddOffset(-0.5); });
            Button plus = Ui.IconButton("plus", 15, "歌词延后 0.5 秒", delegate { AddOffset(0.5); });
            offsetLabel.VerticalAlignment = VerticalAlignment.Center;
            offsetLabel.Width = 52;
            offsetLabel.TextAlignment = TextAlignment.Center;

            StackPanel first = Ui.Row(6, desktopToggle, lockToggle, translationCheck);
            first.HorizontalAlignment = HorizontalAlignment.Center;
            first.Margin = new Thickness(0, 0, 0, 10);

            TextBlock offsetTitle = Ui.Text("歌词偏移", 12, "TextMuted");
            offsetTitle.VerticalAlignment = VerticalAlignment.Center;
            offsetTitle.Margin = new Thickness(0, 0, 4, 0);
            StackPanel second = Ui.Row(2, offsetTitle, minus, offsetLabel, plus);
            second.HorizontalAlignment = HorizontalAlignment.Center;

            return Ui.Column(0, first, second);
        }

        #endregion

        #region 右侧歌词区

        private UIElement BuildLyricsArea()
        {
            viewport.ClipToBounds = true;
            lyricsPanel.RenderTransform = lyricsTransform;
            lyricsPanel.VerticalAlignment = VerticalAlignment.Top;
            viewport.Children.Add(lyricsPanel);

            statusPanel.HorizontalAlignment = HorizontalAlignment.Center;
            statusPanel.VerticalAlignment = VerticalAlignment.Center;
            Canvas statusIcon = Icons.Create("lyrics", 36, "TextMuted");
            statusIcon.HorizontalAlignment = HorizontalAlignment.Center;
            statusText.HorizontalAlignment = HorizontalAlignment.Center;
            statusText.TextAlignment = TextAlignment.Center;
            statusText.LineHeight = 22;
            statusText.Margin = new Thickness(0, 12, 0, 0);
            statusPanel.Children.Add(statusIcon);
            statusPanel.Children.Add(statusText);
            statusPanel.IsHitTestVisible = false;
            viewport.Children.Add(statusPanel);

            // 上下淡出遮罩，让滚动更柔和
            Grid masks = new Grid();
            masks.IsHitTestVisible = false;
            masks.Children.Add(BuildFadeMask(VerticalAlignment.Top, true));
            masks.Children.Add(BuildFadeMask(VerticalAlignment.Bottom, false));
            viewport.Children.Add(masks);

            backButton.Style = (Style)Application.Current.Resources["OutlineButton"];
            backButton.Content = Ui.Text("回到当前歌词", 12, "Text");
            backButton.HorizontalAlignment = HorizontalAlignment.Center;
            backButton.VerticalAlignment = VerticalAlignment.Bottom;
            backButton.Margin = new Thickness(0, 0, 0, 10);
            backButton.Visibility = Visibility.Collapsed;
            backButton.Click += delegate
            {
                manualDelta = 0;
                backButton.Visibility = Visibility.Collapsed;
                ScrollToActive(true);
            };
            viewport.Children.Add(backButton);

            viewport.SizeChanged += delegate
            {
                UpdateSpacers();
                // 等布局真正完成后再定位，否则拿到的行位置还是旧的
                Dispatcher.BeginInvoke((Action)delegate
                {
                    UpdateSpacers();
                    ScrollToActive(false);
                }, DispatcherPriority.Loaded);
            };
            viewport.MouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                manualDelta -= e.Delta * 0.7;
                ClampManual();
                ScrollToActive(false);
                backButton.Visibility = Visibility.Visible;
                RestartManualTimer();
                e.Handled = true;
            };
            IsVisibleChanged += delegate
            {
                if (!IsVisible) return;
                Dispatcher.BeginInvoke((Action)delegate
                {
                    UpdateSpacers();
                    ScrollToActive(false);
                }, DispatcherPriority.Loaded);
            };
            return viewport;
        }

        private Border BuildFadeMask(VerticalAlignment alignment, bool fromCard)
        {
            Border mask = new Border();
            mask.Height = 64;
            mask.VerticalAlignment = alignment;
            Color card = ((SolidColorBrush)Application.Current.Resources["Card"]).Color;
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, fromCard ? 1 : 0);
            brush.EndPoint = new Point(0, fromCard ? 0 : 1);
            GradientStop solid = new GradientStop(card, 0);
            GradientStop clear = new GradientStop(Color.FromArgb(0, card.R, card.G, card.B), 1);
            brush.GradientStops.Add(solid);
            brush.GradientStops.Add(clear);
            mask.Background = brush;
            return mask;
        }

        #endregion

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

        private void SyncToggles()
        {
            desktopToggle.IsChecked = main.Settings.DesktopLyricsOn;
            lockToggle.IsChecked = main.Settings.LyricLocked;
            translationCheck.IsChecked = main.Settings.LyricShowTranslation;
        }

        private void AddOffset(double delta)
        {
            main.Settings.LyricOffset += delta;
            if (main.Settings.LyricOffset > 10) main.Settings.LyricOffset = 10;
            if (main.Settings.LyricOffset < -10) main.Settings.LyricOffset = -10;
            main.SaveSettings();
            UpdateOffsetLabel();
            activeIndex = -2;
            if (main.DesktopLyrics != null) main.DesktopLyrics.ApplySettings();
        }

        private void UpdateOffsetLabel()
        {
            offsetLabel.Text = (main.Settings.LyricOffset > 0 ? "+" : "") +
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
                lineTexts[activeIndex].FontWeight = FontWeights.Normal;
                lineTexts[activeIndex].FontSize = 16;
            }
            activeIndex = index;
            if (activeIndex >= 0 && activeIndex < lineTexts.Count)
            {
                lineTexts[activeIndex].FontWeight = FontWeights.SemiBold;
                lineTexts[activeIndex].FontSize = 17.5;
                lineTexts[activeIndex].SetResourceReference(TextBlock.ForegroundProperty, "Text");
                manualDelta = 0;
                backButton.Visibility = Visibility.Collapsed;
                ScrollToActive(true);
            }
            UpdateLineOpacities();
        }

        private void UpdateLineOpacities()
        {
            for (int i = 0; i < lineElements.Count; i++)
            {
                double opacity;
                if (activeIndex < 0) opacity = 0.7;
                else
                {
                    int distance = Math.Abs(i - activeIndex);
                    opacity = Math.Max(0.25, 1.0 - distance * 0.3);
                }
                lineElements[i].Opacity = opacity;
                if (i != activeIndex)
                    lineTexts[i].SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
            }
        }

        /// <summary>载入指定歌曲的歌词。</summary>
        public void Load(Song song)
        {
            lines.Clear();
            lineElements.Clear();
            lineTexts.Clear();
            lyricsPanel.Children.Clear();
            activeIndex = -1;
            manualDelta = 0;
            backButton.Visibility = Visibility.Collapsed;

            songTitle.Text = song == null ? "未在播放" : song.Title;
            songArtist.Text = song == null ? "" : song.ArtistText;
            songFile.Text = song == null ? "从音乐库中选择一首歌开始播放" : song.FileName;

            if (song == null)
            {
                synced = false;
                statusText.Text = "还没有正在播放的歌曲\n在音乐库里点一下歌名就能播放";
                statusPanel.Visibility = Visibility.Visible;
                UpdateSpacers();
                return;
            }

            LyricDocument doc;
            if (song.IsCloud && !string.IsNullOrEmpty(song.LyricText))
            {
                doc = LrcParser.Parse(song.LyricText);
                doc.Found = doc.Lines.Count > 0;
            }
            else if (song.IsCloud)
            {
                doc = new LyricDocument();
                doc.Found = false;
                doc.Message = song.HasLyrics ? "正在从云盘获取歌词…" : "这首歌还没有歌词";
            }
            else
            {
                doc = LrcParser.Load(song.LyricPath);
            }
            if (!doc.Found || doc.Lines.Count == 0)
            {
                synced = false;
                string baseName = System.IO.Path.GetFileNameWithoutExtension(song.FileName);
                statusText.Text = (string.IsNullOrEmpty(song.LyricPath) ? "这首歌还没有歌词" : doc.Message) +
                    (song.IsCloud ? "" : "\n\n把歌词文件放在歌曲同一个目录，文件名与歌曲相同：\n" + baseName + ".lrc");
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
                item.Margin = new Thickness(28, 5, 28, 5);
                item.Cursor = Cursors.Hand;
                item.Background = Brushes.Transparent;
                item.HorizontalAlignment = HorizontalAlignment.Center;

                TextBlock text = Ui.Text(line.Text, 16, "TextDim");
                text.TextWrapping = TextWrapping.Wrap;
                text.TextAlignment = TextAlignment.Center;
                text.LineHeight = 26;
                item.Children.Add(text);

                if (line.HasTranslation)
                {
                    TextBlock translation = Ui.Text(line.Translation, 12.5, "TextMuted");
                    translation.TextWrapping = TextWrapping.Wrap;
                    translation.TextAlignment = TextAlignment.Center;
                    translation.Margin = new Thickness(0, 2, 0, 0);
                    item.Tag = translation;
                    item.Children.Add(translation);
                }

                LyricLine captured = line;
                item.MouseEnter += delegate(object sender, MouseEventArgs e)
                {
                    FrameworkElement element = (FrameworkElement)sender;
                    element.Opacity = 1;
                };
                item.MouseLeave += delegate { UpdateLineOpacities(); };
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
            UpdateLineOpacities();
            lyricsTransform.Y = 0;
            ScrollToActive(false);
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
            double max = viewport.ActualHeight * 0.9;
            if (manualDelta > max) manualDelta = max;
            if (manualDelta < -max) manualDelta = -max;
        }

        private void RestartManualTimer()
        {
            if (manualTimer == null)
            {
                manualTimer = new DispatcherTimer();
                manualTimer.Interval = TimeSpan.FromSeconds(6);
                manualTimer.Tick += delegate
                {
                    manualTimer.Stop();
                    manualDelta = 0;
                    backButton.Visibility = Visibility.Collapsed;
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
                    TimeSpan.FromMilliseconds(380));
                animation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                lyricsTransform.BeginAnimation(TranslateTransform.YProperty, animation);
            }
            else
            {
                lyricsTransform.BeginAnimation(TranslateTransform.YProperty, null);
                lyricsTransform.Y = target;
            }
        }
    }
}
