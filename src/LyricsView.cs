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
        private readonly ScrollViewer scroller = new ScrollViewer();
        private readonly Border topSpacer = new Border();
        private readonly Border bottomSpacer = new Border();
        private readonly Grid viewport = new Grid();
        private readonly StackPanel statusPanel = new StackPanel();
        private readonly TextBlock statusText = Ui.Text("", 13, "TextMuted");
        private readonly Button backButton = new Button();
        private readonly Border rightCard = new Border();
        private Border topMask;
        private Border bottomMask;

        private readonly TextBlock songTitle = Ui.Text("未在播放", 21, "Text", FontWeights.SemiBold);
        private readonly TextBlock songArtist = Ui.Text("", 13, "TextDim");
        private readonly TextBlock songFile = Ui.Text("", 11.5, "TextMuted");
        private readonly TextBlock offsetLabel = Ui.Text("+0.0 s", 12, "TextMuted");
        private readonly ToggleButton desktopToggle = new ToggleButton();
        private readonly ToggleButton lockToggle = new ToggleButton();
        private readonly CheckBox translationCheck = new CheckBox();

        private int activeIndex = -1;
        private bool scrollingByCode;
        private DateTime lastProgrammaticScroll = DateTime.MinValue;
        private DispatcherTimer scrollTimer;
        private double scrollFrom;
        private double scrollTo;
        private int scrollStep;
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

            main.SettingsChanged += delegate
            {
                SyncToggles();
                UpdateOffsetLabel();
                RefreshMasks();
            };
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

            // 用 ScrollViewer 承载歌词：长歌词（几百行）也能正常滚动与渲染
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroller.CanContentScroll = false;
            scroller.Focusable = false;
            scroller.Content = lyricsPanel;
            scroller.ScrollChanged += OnScrollerChanged;
            viewport.Children.Add(scroller);

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
            topMask = BuildFadeMask(VerticalAlignment.Top, true);
            bottomMask = BuildFadeMask(VerticalAlignment.Bottom, false);
            masks.Children.Add(topMask);
            masks.Children.Add(bottomMask);
            viewport.Children.Add(masks);

            backButton.Style = (Style)Application.Current.Resources["OutlineButton"];
            backButton.Content = Ui.Text("回到当前歌词", 12, "Text");
            backButton.HorizontalAlignment = HorizontalAlignment.Center;
            backButton.VerticalAlignment = VerticalAlignment.Bottom;
            backButton.Margin = new Thickness(0, 0, 0, 10);
            backButton.Visibility = Visibility.Collapsed;
            backButton.Click += delegate
            {
                backButton.Visibility = Visibility.Collapsed;
                ScrollToActive(true);
            };
            viewport.Children.Add(backButton);

            scroller.SizeChanged += delegate
            {
                UpdateSpacers();
                // 等布局真正完成后再定位，否则拿到的行位置还是旧的
                Dispatcher.BeginInvoke((Action)delegate
                {
                    UpdateSpacers();
                    ScrollToActive(false);
                }, DispatcherPriority.Loaded);
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
            mask.Tag = fromCard;
            mask.Background = CreateMaskBrush(fromCard);
            return mask;
        }

        private static LinearGradientBrush CreateMaskBrush(bool fromCard)
        {
            SolidColorBrush cardBrush = Application.Current.Resources["Card"] as SolidColorBrush;
            Color card = cardBrush != null ? cardBrush.Color : Color.FromRgb(24, 27, 34);
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, fromCard ? 1 : 0);
            brush.EndPoint = new Point(0, fromCard ? 0 : 1);
            brush.GradientStops.Add(new GradientStop(card, 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, card.R, card.G, card.B), 1));
            return brush;
        }

        /// <summary>切换主题后重新生成淡出遮罩的颜色。</summary>
        private void RefreshMasks()
        {
            if (topMask != null) topMask.Background = CreateMaskBrush(true);
            if (bottomMask != null) bottomMask.Background = CreateMaskBrush(false);
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

        /// <summary>调试用：输出歌词滚动相关的尺寸信息。</summary>
        public string DebugState()
        {
            string top = "-";
            if (activeIndex >= 0 && activeIndex < lineElements.Count)
            {
                try
                {
                    top = lineElements[activeIndex].TransformToAncestor(lyricsPanel)
                        .Transform(new Point(0, 0)).Y.ToString("0.0");
                }
                catch (Exception)
                {
                }
            }
            return "viewportH=" + viewport.ActualHeight.ToString("0.0")
                + " panelH=" + lyricsPanel.ActualHeight.ToString("0.0")
                + " scroll=" + scroller.VerticalOffset.ToString("0.0") + "/" + scroller.ExtentHeight.ToString("0.0")
                + " activeTop=" + top
                + " elements=" + lineElements.Count;
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
                    TextBlock translation = Ui.Text(line.Translation, 14, "TextDim");
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
            double pad = Math.Max(60, scroller.ViewportHeight / 2 - 30);
            topSpacer.Height = pad;
            bottomSpacer.Height = pad;
        }

        /// <summary>用户手动滚动（滚轮 / 触摸板）时暂停自动跟随，并显示「回到当前歌词」。</summary>
        private void OnScrollerChanged(object sender, ScrollChangedEventArgs e)
        {
            if (scrollingByCode) return;
            if ((DateTime.Now - lastProgrammaticScroll).TotalMilliseconds < 250) return;
            if (Math.Abs(e.VerticalChange) < 0.5) return;
            backButton.Visibility = Visibility.Visible;
            RestartManualTimer();
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
            // 先让新的行高生效，否则拿到的行位置还是旧的，滚动位置会偏
            lyricsPanel.UpdateLayout();
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
            double target = top + element.ActualHeight / 2 - scroller.ViewportHeight / 2;
            if (target < 0) target = 0;
            double max = Math.Max(0, scroller.ExtentHeight - scroller.ViewportHeight);
            if (target > max) target = max;

            if (!animate)
            {
                StopScrollAnimation();
                SetScrollOffset(target);
                return;
            }
            StartScrollAnimation(target);
        }

        private void SetScrollOffset(double offset)
        {
            lastProgrammaticScroll = DateTime.Now;
            scrollingByCode = true;
            try
            {
                scroller.ScrollToVerticalOffset(offset);
            }
            finally
            {
                scrollingByCode = false;
            }
        }

        /// <summary>用缓动把滚动位置平滑推到目标（WPF 的 ScrollViewer 没有自带平滑滚动）。</summary>
        private void StartScrollAnimation(double target)
        {
            StopScrollAnimation();
            scrollFrom = scroller.VerticalOffset;
            scrollTo = target;
            scrollStep = 0;
            if (Math.Abs(scrollTo - scrollFrom) < 1)
            {
                SetScrollOffset(target);
                return;
            }
            scrollTimer = new DispatcherTimer();
            scrollTimer.Interval = TimeSpan.FromMilliseconds(16);
            scrollTimer.Tick += delegate
            {
                scrollStep++;
                double t = scrollStep / 22.0;
                if (t >= 1)
                {
                    t = 1;
                    StopScrollAnimation();
                }
                double eased = 1 - Math.Pow(1 - t, 3);
                SetScrollOffset(scrollFrom + (scrollTo - scrollFrom) * eased);
                if (t >= 1) StopScrollAnimationOnly();
            };
            scrollTimer.Start();
        }

        private void StopScrollAnimationOnly()
        {
            if (scrollTimer != null)
            {
                scrollTimer.Stop();
                scrollTimer = null;
            }
        }

        private void StopScrollAnimation()
        {
            StopScrollAnimationOnly();
        }
    }
}
