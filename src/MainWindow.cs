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
    public partial class MainWindow : Window
    {
        public const string AppName = "云雀";
        public const string AppVersion = "3.3.14";

        /// <summary>桌面歌词的预设颜色（浅色背景建议用后面的深色）。</summary>
        public static readonly string[] LyricColorPresets = new string[]
        {
            "#FFFFFF", "#FFE066", "#7CE7FF", "#FF9CC8", "#A8F0A0", "#C9B6FF",
            "#111111", "#4B5563", "#1E3A8A", "#7F1D1D"
        };

        private readonly AppSettings settings;
        private readonly PlayerEngine engine = new PlayerEngine();

        private List<Song> library = new List<Song>();
        private List<Song> queue = new List<Song>();
        private List<Song> visible = new List<Song>();
        private int queueIndex = -1;
        private Song currentSong;
        private string searchText = string.Empty;
        private bool scanning;
        private bool uploading;
        private string cloudRepoName = string.Empty;
        private string cloudRepoId = string.Empty;

        private DispatcherTimer timer;
        private Forms.NotifyIcon tray;
        private bool trayTipShown;
        private bool reallyExit;

        private Grid contentHost;
        private readonly Dictionary<string, ToggleButton> navToggles = new Dictionary<string, ToggleButton>();
        private LibraryView libraryView;
        private QueueView queueView;
        private LyricsView lyricsView;
        private SettingsView settingsView;
        private DesktopLyricsWindow desktopLyrics;

        private TextBox searchBox;
        private TextBlock searchPlaceholder;
        private TextBlock titleText;
        private TextBlock artistText;
        private TextBlock positionText;
        private TextBlock durationText;
        private Slider progressSlider;
        private ProgressBar bufferingBar;
        private Slider volumeSlider;
        private Button playButton;
        private Button modeButton;
        private Button queueButton;
        private ToggleButton lyricsToggle;
        private ToggleButton lockToggle;
        private TextBlock libraryCountText;
        private TextBlock queueCountText;
        private TextBlock statusText;
        private TextBlock dirLabel;
        private Border toast;
        private TextBlock toastText;
        private bool draggingProgress;

        public MainWindow()
        {
            settings = SettingsStore.Load();
            Theme.Apply(settings.Theme);
            Theme.EnsureStyles();
            TrimCloudCacheOnStart();

            Title = AppName;
            Width = 1180;
            Height = 740;
            MinWidth = 940;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = (Brush)Application.Current.Resources["Window"];
            FontFamily = Ui.Font;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            RestoreWindowBounds();

            engine.Volume = settings.Volume;
            engine.IsMuted = settings.Muted;
            engine.TrackEnded += delegate { Dispatcher.BeginInvoke((Action)delegate { Advance(false); }); };
            engine.Opened += delegate
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    TraceStep("engine opened: duration=" + engine.Duration.ToString("0.0")
                        + " playing=" + engine.IsPlaying);
                    OnTrackOpened();
                });
            };
            engine.Failed += delegate(object s, EventArgs e)
            {
                PlayerErrorArgs args = e as PlayerErrorArgs;
                string message = args != null ? args.Message : "播放失败";
                TraceStep("engine failed: " + message);
                Dispatcher.BeginInvoke((Action)delegate { ShowToast("播放失败：" + message); });
            };

            BuildUi();
            InitTray();

            Loaded += OnLoaded;
            Closing += OnClosing;
            Closed += OnClosed;
            StateChanged += OnStateChanged;
            PreviewKeyDown += OnPreviewKeyDown;
            AllowDrop = true;
            Drop += OnDropFiles;
        }

        #region 公开接口（供各视图调用）

        public AppSettings Settings { get { return settings; } }
        public PlayerEngine Engine { get { return engine; } }
        public LyricsView Lyrics { get { return lyricsView; } }
        public List<Song> Library { get { return library; } }
        public List<Song> Queue { get { return queue; } }
        public List<Song> VisibleSongs { get { return visible; } }
        public int QueueIndex { get { return queueIndex; } }
        public Song CurrentSong { get { return currentSong; } }
        public string SearchText { get { return searchText; } }

        public event EventHandler LibraryChanged;
        public event EventHandler QueueChanged;
        public event EventHandler SongChanged;
        public event EventHandler PlaybackStateChanged;
        public event EventHandler SearchChanged;
        public event EventHandler SettingsChanged;

        public void SetSearch(string text)
        {
            searchText = text == null ? string.Empty : text;
            ApplyFilter();
            Raise(SearchChanged);
        }

        public void PlayFrom(List<Song> context, int index)
        {
            if (context == null || index < 0 || index >= context.Count) return;
            queue = new List<Song>(context);
            queueIndex = index;
            Raise(QueueChanged);
            UpdateQueueState();
            PlayCurrent(true);
        }

        /// <summary>
        /// 点歌名：直接播放这一首，并把它加入播放列表（已经在列表里就只播放，不重复添加）。
        /// </summary>
        public void PlaySong(Song song)
        {
            if (song == null) return;
            int idx = queue.IndexOf(song);
            if (idx < 0)
            {
                queue.Add(song);
                idx = queue.Count - 1;
                Raise(QueueChanged);
                UpdateQueueState();
            }
            queueIndex = idx;
            PlayCurrent(true);
        }

        public void Enqueue(Song song, bool playNext)
        {
            if (song == null) return;

            int existing = queue.IndexOf(song);
            if (existing >= 0)
            {
                if (playNext)
                {
                    Song moved = queue[existing];
                    queue.RemoveAt(existing);
                    if (existing < queueIndex) queueIndex--;
                    int at = queueIndex + 1;
                    if (at > queue.Count) at = queue.Count;
                    if (at < 0) at = 0;
                    queue.Insert(at, moved);
                    Raise(QueueChanged);
                    UpdateQueueState();
                    ShowToast("已把「" + song.Title + "」调整到下一首");
                }
                else
                {
                    ShowToast("播放列表里已经有「" + song.Title + "」了");
                }
                return;
            }

            if (queue.Count == 0)
            {
                queue.Add(song);
                queueIndex = 0;
            }
            else if (playNext)
            {
                int at = queueIndex + 1;
                if (at < 0) at = 0;
                if (at > queue.Count) at = queue.Count;
                queue.Insert(at, song);
            }
            else
            {
                queue.Add(song);
            }
            Raise(QueueChanged);
            UpdateQueueState();
            ShowToast(playNext ? "已设为下一首播放：" + song.Title : "已加入播放队列：" + song.Title);
        }

        public void RemoveFromQueue(Song song)
        {
            int idx = queue.IndexOf(song);
            if (idx < 0) return;
            queue.RemoveAt(idx);
            if (idx < queueIndex) queueIndex--;
            else if (idx == queueIndex)
            {
                if (queue.Count == 0)
                {
                    queueIndex = -1;
                    StopAndClear();
                }
                else
                {
                    if (queueIndex >= queue.Count) queueIndex = 0;
                    PlayCurrent(true);
                }
            }
            Raise(QueueChanged);
            UpdateQueueState();
        }

        public void MoveInQueue(int from, int to)
        {
            if (from < 0 || from >= queue.Count) return;
            if (to < 0 || to >= queue.Count) return;
            Song song = queue[from];
            queue.RemoveAt(from);
            queue.Insert(to, song);
            if (queueIndex == from) queueIndex = to;
            else if (from < queueIndex && to >= queueIndex) queueIndex--;
            else if (from > queueIndex && to <= queueIndex) queueIndex++;
            Raise(QueueChanged);
            UpdateQueueState();
        }

        public void ClearQueue()
        {
            queue.Clear();
            queueIndex = -1;
            Raise(QueueChanged);
            UpdateQueueState();
            StopAndClear();
            ShowToast("已清空播放队列");
        }

        public void RemoveFromLibrary(Song song)
        {
            if (song == null) return;
            if (!settings.Hidden.Contains(song.Path)) settings.Hidden.Add(song.Path);
            library.Remove(song);
            if (queue.Contains(song)) queue.Remove(song);
            SaveSettings();
            ApplyFilter();
            Raise(QueueChanged);
            Raise(LibraryChanged);
            ShowToast("已从音乐库移除：" + song.Title);
        }

        public void PlayNextInQueue(Song song)
        {
            Enqueue(song, true);
        }

        public void Next()
        {
            Advance(true);
        }

        public void Previous()
        {
            if (queue.Count == 0) return;
            if (engine.GetPosition() > 4)
            {
                engine.Seek(0);
                return;
            }
            queueIndex--;
            if (queueIndex < 0)
                queueIndex = settings.Mode == PlayMode.Sequential ? 0 : queue.Count - 1;
            PlayCurrent(true);
        }

        public void TogglePlay()
        {
            if (currentSong == null)
            {
                if (queue.Count > 0)
                {
                    queueIndex = queueIndex >= 0 ? queueIndex : 0;
                    PlayCurrent(true);
                }
                else if (visible.Count > 0)
                {
                    PlayFrom(visible, 0);
                }
                else
                {
                    ShowToast("音乐库里还没有歌曲");
                }
                return;
            }
            engine.TogglePlay();
            UpdatePlayButton();
            Raise(PlaybackStateChanged);
        }

        public void SeekTo(double seconds)
        {
            engine.Seek(seconds);
            UpdateProgress(true);
        }

        public void SetMode(PlayMode mode)
        {
            PlayMode previous = settings.Mode;
            settings.Mode = mode;
            // 随机播放＝点的时候把列表真的打乱一次，之后按顺序往下放；
            // 关掉随机就把原来的顺序还原回来。
            if (mode == PlayMode.Shuffle && previous != PlayMode.Shuffle) EnterShuffle();
            else if (mode != PlayMode.Shuffle && previous == PlayMode.Shuffle) ExitShuffle();
            UpdateModeButton();
            SaveSettings();
            ShowToast("播放模式：" + ModeName(mode));
            Raise(PlaybackStateChanged);
        }

        /// <summary>
        /// 点「随机播放」：把播放列表打乱一次（当前这首放在最前，不打断正在听的），
        /// 之后就和列表循环一样按顺序播。抽签只发生在这一刻，不会每切一首再抽一次。
        /// </summary>
        private void EnterShuffle()
        {
            if (queue.Count <= 1) return;
            if (settings.ShuffleRestore == null || settings.ShuffleRestore.Count == 0)
            {
                settings.ShuffleRestore = new List<string>();
                foreach (Song s in queue) settings.ShuffleRestore.Add(s.Path);
            }
            Song current = queueIndex >= 0 && queueIndex < queue.Count ? queue[queueIndex] : null;
            List<Song> rest = new List<Song>();
            foreach (Song s in queue) if (s != current) rest.Add(s);
            Random random = new Random();
            for (int i = rest.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                Song tmp = rest[i];
                rest[i] = rest[j];
                rest[j] = tmp;
            }
            queue.Clear();
            if (current != null)
            {
                queue.Add(current);
                queueIndex = 0;
            }
            queue.AddRange(rest);
            if (current == null) queueIndex = 0;
            UpdateQueueState();
        }

        /// <summary>关掉随机播放：按进入随机前记下的顺序把列表还原（当前这首继续放）。</summary>
        private void ExitShuffle()
        {
            if (settings.ShuffleRestore == null || settings.ShuffleRestore.Count == 0) return;
            Song current = queueIndex >= 0 && queueIndex < queue.Count ? queue[queueIndex] : null;
            List<Song> ordered = new List<Song>();
            foreach (string path in settings.ShuffleRestore)
            {
                Song found = queue.Find(delegate(Song s) { return s.Path == path; });
                if (found != null && !ordered.Contains(found)) ordered.Add(found);
            }
            // 随机之后新加进来的歌，按添加顺序接在后面
            foreach (Song s in queue) if (!ordered.Contains(s)) ordered.Add(s);
            queue.Clear();
            queue.AddRange(ordered);
            queueIndex = current == null ? -1 : queue.IndexOf(current);
            settings.ShuffleRestore = null;
            UpdateQueueState();
        }

        public void CycleMode()
        {
            PlayMode next = (PlayMode)(((int)settings.Mode + 1) % 4);
            SetMode(next);
        }

        public void ShowDesktopLyrics(bool on)
        {
            settings.DesktopLyricsOn = on;
            if (lyricsToggle != null) lyricsToggle.IsChecked = on;
            if (on)
            {
                if (desktopLyrics == null)
                {
                    desktopLyrics = new DesktopLyricsWindow(this);
                    desktopLyrics.Closed += delegate { desktopLyrics = null; if (lyricsToggle != null) lyricsToggle.IsChecked = false; };
                }
                desktopLyrics.Show();
                desktopLyrics.UpdateNow();
            }
            else if (desktopLyrics != null)
            {
                desktopLyrics.Close();
                desktopLyrics = null;
            }
            SaveSettings();
            Raise(PlaybackStateChanged);
        }

        /// <summary>锁定 / 解锁桌面歌词（锁定时鼠标穿透，不干扰其它操作）。</summary>
        public void SetLyricLocked(bool locked, bool notify)
        {
            settings.LyricLocked = locked;
            if (lockToggle != null) lockToggle.IsChecked = locked;
            if (desktopLyrics != null)
            {
                desktopLyrics.ApplySettings();
                desktopLyrics.FlashHint(locked
                    ? "已锁定：鼠标可穿透（把鼠标移到右上角可解锁）"
                    : "已解锁：可拖动、可右键");
            }
            SaveSettings();
            if (notify)
            {
                ShowToast(locked
                    ? "桌面歌词已锁定（鼠标穿透）· 鼠标移到右上角、或按 Ctrl+Alt+L 解锁"
                    : "桌面歌词已解锁，可自由拖动");
                Raise(SettingsChanged);
            }
        }

        public void ToggleLyricLock()
        {
            SetLyricLocked(!settings.LyricLocked, true);
        }

        public void ShowView(string name)
        {
            if (contentHost == null) return;
            UIElement target = libraryView;
            if (name == "queue") target = queueView;
            else if (name == "lyrics") target = lyricsView;
            else if (name == "settings") target = settingsView;
            foreach (UIElement child in contentHost.Children) child.Visibility = Visibility.Collapsed;
            target.Visibility = Visibility.Visible;
            settings.LastView = name;
            foreach (KeyValuePair<string, ToggleButton> pair in navToggles)
            {
                pair.Value.IsChecked = pair.Key == name;
            }
            if (target is LibraryView) ((LibraryView)target).FocusList();
        }

        public void ShowToast(string message)
        {
            if (toast == null) return;
            toastText.Text = message;
            toast.Visibility = Visibility.Visible;
            toast.BeginAnimation(UIElement.OpacityProperty, null);
            DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(350));
            fadeOut.BeginTime = TimeSpan.FromMilliseconds(1900);
            fadeOut.Completed += delegate { toast.Visibility = Visibility.Collapsed; };
            Storyboard sb = new Storyboard();
            sb.Children.Add(fadeIn);
            sb.Children.Add(fadeOut);
            Storyboard.SetTarget(sb, toast);
            Storyboard.SetTargetProperty(sb, new PropertyPath(UIElement.OpacityProperty));
            sb.Begin();
        }

        public void ChooseMusicDir()
        {
            Forms.FolderBrowserDialog dialog = new Forms.FolderBrowserDialog();
            dialog.Description = "选择音乐文件夹（歌词 .lrc 与歌曲放在一起）";
            dialog.ShowNewFolderButton = false;
            string current = settings.MusicDir;
            if (!string.IsNullOrEmpty(current) && Directory.Exists(current)) dialog.SelectedPath = current;
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                SetMusicDir(dialog.SelectedPath);
            }
        }

        public void SetMusicDir(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            settings.MusicDir = dir;
            settings.Source = "local";
            SaveSettings();
            Raise(SettingsChanged);
            Rescan();
        }

        public void Rescan()
        {
            if (scanning) return;
            if (settings.Source == "cloud")
            {
                if (string.IsNullOrEmpty(CloudEndpoint))
                {
                    ShowToast("请先在设置里填写云盘分享链接或 API 令牌");
                    return;
                }
                scanning = true;
                RescanCloud();
                return;
            }
            scanning = true;
            string dir = settings.MusicDir;
            bool recursive = settings.Recursive;
            List<DurationEntry> cache = settings.Durations;
            List<string> hidden = settings.Hidden;
            ShowToast("正在扫描音乐文件夹…");
            if (statusText != null) statusText.Text = "正在扫描…";

            ThreadPool.QueueUserWorkItem(delegate
            {
                ScanResult result = LibraryScanner.Scan(dir, recursive, cache, hidden);
                Dispatcher.BeginInvoke((Action)delegate
                {
                    ApplyScan(result);
                });
            });
        }

        /// <summary>云盘连接方式：填了 API 令牌就用令牌，否则用分享链接。</summary>
        public string CloudEndpoint
        {
            get
            {
                if (!string.IsNullOrEmpty(settings.CloudToken)) return settings.CloudToken.Trim();
                return settings.CloudUrl == null ? string.Empty : settings.CloudUrl.Trim();
            }
        }

        /// <summary>是否可以使用 API 令牌删除云端文件。</summary>
        public bool CanDeleteCloud
        {
            get { return CloudClient.IsApiToken(CloudEndpoint); }
        }

        public bool IsCloudSource
        {
            get { return !string.IsNullOrEmpty(CloudEndpoint); }
        }

        private void RescanCloud()
        {
            string url = CloudEndpoint;
            List<DurationEntry> cache = settings.Durations;
            List<string> hidden = settings.Hidden;
            ShowToast("正在读取云盘文件列表…");
            if (statusText != null) statusText.Text = "正在读取云盘…";

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    FetchRepoInfo(url);
                    ScanResult result = CloudLibrary.Scan(url, cache, hidden);
                    Dispatcher.BeginInvoke((Action)delegate { ApplyScan(result); });
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        scanning = false;
                        ShowToast("无法读取云盘：" + ex.Message);
                        if (statusText != null) statusText.Text = "云盘读取失败";
                    });
                }
            });
        }

        /// <summary>令牌模式下取一次资料库名称与 ID（用于界面显示与「打开云盘」）。</summary>
        private void FetchRepoInfo(string endpoint)
        {
            if (!CloudClient.IsApiToken(endpoint)) return;
            try
            {
                CloudRepoInfo info = CloudClient.GetRepoInfo(endpoint);
                cloudRepoName = info.Name;
                cloudRepoId = info.RepoId;
            }
            catch (Exception)
            {
            }
        }

        /// <summary>后台补齐云盘歌曲的歌词与时长。</summary>
        private void FillCloudDetails()
        {
            if (!IsCloudSource || library.Count == 0) return;
            string url = CloudEndpoint;
            List<Song> songs = new List<Song>(library);
            ThreadPool.QueueUserWorkItem(delegate
            {
                CloudLibrary.FillDetails(url, songs, delegate(Song song)
                {
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        if (song == currentSong && lyricsView != null) lyricsView.Load(song);
                        if (song == currentSong && desktopLyrics != null) desktopLyrics.UpdateNow();
                    });
                }, 0);
                Dispatcher.BeginInvoke((Action)delegate
                {
                    SaveSettings();
                    UpdateStatusText();
                });
            });
        }

        public void ToggleTheme()
        {
            Theme.Toggle();
            settings.Theme = Theme.Mode;
            Background = (Brush)Application.Current.Resources["Window"];
            ApplyDarkTitleBar();
            SaveSettings();
            Raise(SettingsChanged);
        }

        /// <summary>设置主题模式：system / dark / light。</summary>
        public void SetThemeMode(string mode)
        {
            Theme.Apply(mode);
            settings.Theme = Theme.Mode;
            Background = (Brush)Application.Current.Resources["Window"];
            ApplyDarkTitleBar();
            SaveSettings();
            Raise(SettingsChanged);
        }

        public void OpenMusicFolder()
        {
            OpenMusicDir();
        }

        public void RefreshMediaKeys()
        {
            RegisterMediaKeys();
        }

        public void NotifySettingsChanged()
        {
            Raise(SettingsChanged);
        }

        public void ShowFromTrayPublic()
        {
            ShowFromTray();
        }

        /// <summary>重新构建歌词页面（翻译开关等变化后调用）。</summary>
        public void RefreshLyricsView()
        {
            if (lyricsView == null) return;
            lyricsView.Load(currentSong);
            if (desktopLyrics != null) desktopLyrics.UpdateNow();
        }

        public void SaveSettings()
        {
            settings.Volume = engine.Volume;
            settings.Muted = engine.IsMuted;
            // 播放列表：只有「已经恢复过上次的列表」或「列表里确实有歌」时才写回。
            // 否则启动早期（云盘还没扫完、内存列表还是空的）的那次自动保存
            // 会把配置文件里保存的播放列表清空，下次打开就什么都没了。
            if (restoreAttempted || queue.Count > 0)
            {
                settings.Queue = new List<string>();
                foreach (Song s in queue) settings.Queue.Add(s.Path);
                settings.QueueIndex = queueIndex;
            }
            SettingsStore.Save(settings);
        }

        public void RevealInExplorer(Song song)
        {
            if (song == null || !File.Exists(song.Path)) return;
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + song.Path + "\"");
            }
            catch (Exception)
            {
            }
        }

        public DesktopLyricsWindow DesktopLyrics
        {
            get { return desktopLyrics; }
        }

        #endregion

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

        #region 运行逻辑

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplyDarkTitleBar();
            UpdateSearchPlaceholder();
            if (!IsCloudSource)
            {
                SetMusicDirForFirstRun();
            }
            else if (string.IsNullOrEmpty(CloudEndpoint))
            {
                ShowView("settings");
                ShowToast("请先粘贴云盘分享链接或 API 令牌，再点「刷新列表」");
            }
            Rescan();
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(80);
            timer.Tick += delegate { Tick(); };
            timer.Start();
            if (settings.DesktopLyricsOn) ShowDesktopLyrics(true);
        }

        private void SetMusicDirForFirstRun()
        {
            if (string.IsNullOrEmpty(settings.MusicDir) || !Directory.Exists(settings.MusicDir))
            {
                settings.MusicDir = AppPaths.DetectMusicDir();
                SaveSettings();
            }
            if (dirLabel != null) dirLabel.Text = settings.MusicDir;
            SettingsChangedSafe();
        }

        private bool restoreAttempted;

        private void RestoreQueue()
        {
            restoreAttempted = true;
            List<Song> restored = new List<Song>();
            foreach (string path in settings.Queue)
            {
                foreach (Song song in library)
                {
                    if (string.Equals(song.Path, path, StringComparison.OrdinalIgnoreCase))
                    {
                        restored.Add(song);
                        break;
                    }
                }
            }
            queue = restored;
            queueIndex = settings.QueueIndex;
            if (queueIndex < 0 || queueIndex >= queue.Count) queueIndex = queue.Count > 0 ? 0 : -1;

            if (settings.ResumeLast && !string.IsNullOrEmpty(settings.LastSongPath))
            {
                foreach (Song song in library)
                {
                    if (string.Equals(song.Path, settings.LastSongPath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!queue.Contains(song)) queue.Insert(0, song);
                        queueIndex = queue.IndexOf(song);
                        OpenTrack(song, settings.AutoPlayOnStart, settings.LastPosition);
                        break;
                    }
                }
            }
            UpdateQueueState();
        }

        private void ApplyScan(ScanResult result)
        {
            scanning = false;
            settings.Durations = result.Cache;
            library = result.Songs;
            ApplyFilter();
            Raise(LibraryChanged);

            double total = 0;
            foreach (Song s in library) total += s.Duration;
            UpdateStatusText();
            SettingsChangedSafe();

            // 恢复上次的播放队列：必须放在 SaveSettings 之前，
            // 否则会用当前（空）队列覆盖配置文件里保存的播放列表
            if (!restoreAttempted && settings.Queue.Count > 0) RestoreQueue();
            SaveSettings();

            string message = library.Count == 0
                ? "没有找到音乐文件，请检查音乐目录"
                : string.Format(CultureInfo.InvariantCulture, "已载入 {0} 首歌曲", library.Count);
            if (result.SkippedUnsupported > 0)
            {
                message += string.Format(CultureInfo.InvariantCulture,
                    "（已忽略 {0} 个不支持的文件，如 ape / dsf / amr）", result.SkippedUnsupported);
            }
            ShowToast(message);
            UpdateQueueState();
            FillCloudDetails();
        }

        public void UpdateStatusText()
        {
            if (statusText == null) return;
            if (IsCloudSource)
            {
                int cached = 0;
                foreach (Song s in library)
                {
                    if (CloudCache.CachedPath(s) != null) cached++;
                }
                statusText.Text = string.Format(CultureInfo.InvariantCulture,
                    "云盘 · {0} 首 · 已缓存 {1} 首", library.Count, cached);
            }
            else
            {
                statusText.Text = string.Format(CultureInfo.InvariantCulture,
                    "共 {0} 首", library.Count);
            }
            if (dirLabel != null)
            {
                if (!IsCloudSource)
                {
                    dirLabel.Text = settings.MusicDir;
                }
                else if (CanDeleteCloud)
                {
                    // 令牌模式不要显示令牌本身，只显示资料库名称
                    dirLabel.Text = "云盘（API 令牌）："
                        + (string.IsNullOrEmpty(cloudRepoName) ? "已连接" : cloudRepoName);
                }
                else
                {
                    dirLabel.Text = "云盘（分享链接）：" + settings.CloudUrl;
                }
            }
        }

        /// <summary>切换音乐来源：local / cloud。</summary>
        public void SetSource(string source)
        {
            settings.Source = source == "cloud" ? "cloud" : "local";
            SaveSettings();
            Raise(SettingsChanged);
            Rescan();
        }

        public void SetCloudUrl(string url)
        {
            settings.CloudUrl = url == null ? string.Empty : url.Trim();
            SaveSettings();
        }

        public void SetCloudToken(string token)
        {
            settings.CloudToken = token == null ? string.Empty : token.Trim();
            SaveSettings();
        }

        /// <summary>删除云盘上的歌曲文件（仅在配置了 API 令牌时可用）。</summary>
        public void DeleteFromCloud(Song song)
        {
            if (song == null || !song.IsCloud) return;
            if (!CanDeleteCloud)
            {
                ShowToast("删除云端文件需要 API 令牌（见设置 → 云端音乐）");
                return;
            }

            MessageBoxResult confirm = MessageBox.Show(this,
                "确定要从云盘删除「" + song.Title + "」吗？\n\n文件名：" + song.FileName
                + "\n删除后云端文件会进入云盘的回收站。",
                "从云盘删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;

            string token = CloudEndpoint;
            string cloudPath = song.CloudPath;
            string parent = "/";
            int slash = cloudPath.LastIndexOf('/');
            if (slash > 0) parent = cloudPath.Substring(0, slash);
            string name = cloudPath.Substring(slash + 1);

            ShowToast("正在从云盘删除：" + song.FileName);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try
                {
                    List<string> names = new List<string>();
                    names.Add(name);
                    CloudClient.DeleteFiles(token, parent, names);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                Dispatcher.BeginInvoke((Action)delegate
                {
                    if (error == null)
                    {
                        CloudCache.Remove(song);
                        ShowToast("已从云盘删除：" + song.Title);
                        Rescan();
                    }
                    else
                    {
                        ShowToast("删除失败：" + error);
                    }
                });
            });
        }

        /// <summary>检测 API 令牌对应的资料库信息。</summary>
        public void TestCloudToken(string token, Action<bool, string> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                string message;
                try
                {
                    CloudRepoInfo info = CloudClient.GetRepoInfo(token);
                    ok = true;
                    message = "已连接资料库「" + info.Name + "」，共 " + info.FileCount + " 个文件（"
                        + (info.Size / 1024 / 1024) + " MB）";
                }
                catch (Exception ex)
                {
                    message = "令牌无效或网络异常：" + ex.Message;
                }
                if (done != null) Dispatcher.BeginInvoke((Action)delegate { done(ok, message); });
            });
        }

        /// <summary>测试云盘链接是否可用（后台执行，回调在 UI 线程）。</summary>
        public void TestCloudConnection(string url, Action<bool, string> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                string message;
                try
                {
                    List<CloudEntry> entries = CloudClient.ListAllFiles(url, 1);
                    int songs = 0;
                    foreach (CloudEntry entry in entries)
                    {
                        if (Array.IndexOf(LibraryScanner.Extensions,
                            Path.GetExtension(entry.Name).ToLowerInvariant()) >= 0) songs++;
                    }
                    ok = true;
                    message = "连接成功：发现 " + songs + " 首歌曲";
                }
                catch (Exception ex)
                {
                    message = "连接失败：" + ex.Message;
                }
                if (done != null)
                {
                    Dispatcher.BeginInvoke((Action)delegate { done(ok, message); });
                }
            });
        }

        public void ClearCloudCache()
        {
            CloudCache.Clear();
            UpdateStatusText();
            Raise(SettingsChanged);
            ShowToast("已清理云端缓存");
        }

        /// <summary>按当前的本地占用策略立刻清一遍（设置里切换后调用）。</summary>
        public void PruneCloudCacheNow()
        {
            if (settings.CloudCacheMode == 2) return;
            PruneCloudCache(currentSong, PeekNextSong());
            UpdateStatusText();
            Raise(SettingsChanged);
        }

        #region 上传到云盘

        private void OnDropFiles(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0) return;
            UploadFiles(paths);
        }

        /// <summary>选择本地文件上传到云盘分享目录。</summary>
        public void PickAndUploadFiles()
        {
            if (!IsCloudSource)
            {
                ShowToast("请先在设置里填写云盘分享链接");
                return;
            }
            Forms.OpenFileDialog dialog = new Forms.OpenFileDialog();
            dialog.Title = "选择要上传到云盘的歌曲 / 歌词";
            dialog.Multiselect = true;
            dialog.Filter = "歌曲与歌词|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.lrc|所有文件 (*.*)|*.*";
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
            UploadFiles(dialog.FileNames);
        }

        /// <summary>把文件（或文件夹里的歌曲与歌词）上传到云盘分享目录。</summary>
        public void UploadFiles(string[] paths)
        {
            if (!IsCloudSource)
            {
                ShowToast("请先在设置里填写云盘分享链接");
                return;
            }
            if (uploading)
            {
                ShowToast("还有上传任务在进行中");
                return;
            }

            List<string> files = new List<string>();
            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                try
                {
                    if (Directory.Exists(path))
                    {
                        foreach (string file in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories))
                        {
                            if (IsUploadable(file)) files.Add(file);
                        }
                    }
                    else if (File.Exists(path) && IsUploadable(path))
                    {
                        files.Add(path);
                    }
                }
                catch (Exception)
                {
                }
            }

            if (files.Count == 0)
            {
                ShowToast("没有可上传的文件（支持音频与 .lrc 歌词）");
                return;
            }

            uploading = true;
            string url = CloudEndpoint;
            List<string> batch = files;
            if (statusText != null) statusText.Text = "正在上传 0/" + batch.Count + "…";

            ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0;
                List<string> failed = new List<string>();
                for (int i = 0; i < batch.Count; i++)
                {
                    string path = batch[i];
                    string name = Path.GetFileName(path);
                    int index = i + 1;
                    try
                    {
                        bool replaced;
                        CloudClient.Upload(url, path, "/", delegate(long done, long total)
                        {
                            int percent = total > 0 ? (int)(done * 100 / total) : 0;
                            Dispatcher.BeginInvoke((Action)delegate
                            {
                                if (statusText != null)
                                    statusText.Text = "正在上传 " + index + "/" + batch.Count + "："
                                        + name + " " + percent + "%";
                            });
                        }, out replaced);
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        failed.Add(name + "：" + ex.Message);
                    }
                }

                Dispatcher.BeginInvoke((Action)delegate
                {
                    uploading = false;
                    UpdateStatusText();
                    if (failed.Count == 0)
                    {
                        ShowToast("已上传 " + ok + " 个文件到云盘");
                    }
                    else
                    {
                        ShowToast("上传完成：" + ok + " 个成功，" + failed.Count + " 个失败（"
                            + failed[0] + "）");
                    }
                    SaveSettings();
                    Rescan();
                });
            });
        }

        private static bool IsUploadable(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".lrc") return true;
            return Array.IndexOf(LibraryScanner.Extensions, ext) >= 0;
        }

        #endregion

        private string LongDuration(double seconds)
        {
            int total = (int)Math.Round(seconds);
            int h = total / 3600;
            int m = (total % 3600) / 60;
            if (h > 0) return string.Format(CultureInfo.InvariantCulture, "{0} 小时 {1} 分", h, m);
            return string.Format(CultureInfo.InvariantCulture, "{0} 分", m);
        }

        public void ApplyFilter()
        {
            List<Song> list = new List<Song>();
            string key = searchText.Trim().ToLowerInvariant();
            foreach (Song song in library)
            {
                if (key.Length == 0
                    || song.Title.ToLowerInvariant().Contains(key)
                    || song.Artist.ToLowerInvariant().Contains(key)
                    || song.FileName.ToLowerInvariant().Contains(key))
                {
                    list.Add(song);
                }
            }
            SortSongs(list);
            visible = list;
            if (libraryView != null) libraryView.RefreshItems();
            if (libraryCountText != null) libraryCountText.Text = library.Count.ToString(CultureInfo.InvariantCulture);
        }

        private void SortSongs(List<Song> list)
        {
            Comparison<Song> cmp;
            switch (settings.Sort)
            {
                case SortField.Title:
                    cmp = delegate(Song a, Song b) { return string.Compare(a.Title, b.Title, StringComparison.CurrentCulture); };
                    break;
                case SortField.Artist:
                    cmp = delegate(Song a, Song b) { return string.Compare(a.Artist, b.Artist, StringComparison.CurrentCulture); };
                    break;
                case SortField.Duration:
                    // 时长已经不在界面上展示了，历史设置落到「按歌名」排序
                    cmp = delegate(Song a, Song b) { return string.Compare(a.Title, b.Title, StringComparison.CurrentCulture); };
                    break;
                default:
                    cmp = delegate(Song a, Song b) { return string.Compare(a.FileName, b.FileName, StringComparison.CurrentCulture); };
                    break;
            }
            list.Sort(cmp);
            if (!settings.SortAscending) list.Reverse();
        }

        private void OpenTrack(Song song, bool autoPlay, double startAt)
        {
            if (song == null) return;
            ReleaseOldCloudCache(song);
            currentSong = song;
            PruneCloudCache(song, PeekNextSong());
            UpdateCurrentFlags();
            if (lyricsView != null) lyricsView.Load(song);
            if (titleText != null) titleText.Text = song.Title;
            if (artistText != null) artistText.Text = song.ArtistText + (song.HasLyrics ? " · 有歌词" : "");
            Title = song.Title + " - " + AppName;
            Raise(SongChanged);
            if (desktopLyrics != null) desktopLyrics.UpdateNow();
            UpdateProgress(true);

            string localPath = song.Path;
            if (song.IsCloud)
            {
                string cached = CloudCache.CachedPath(song);
                TraceStep("open: cloud  cached=" + (cached == null ? "NULL" : "hit")
                    + " songSize=" + song.Size + " fileSize="
                    + (File.Exists(CloudCache.FileFor(song)) ? new FileInfo(CloudCache.FileFor(song)).Length.ToString() : "-"));
                if (cached == null)
                {
                    StartCloudBuffering(song, autoPlay, startAt);
                    return;
                }
                localPath = cached;
            }

            PlayResolved(song, localPath, autoPlay, startAt);
        }

        /// <summary>
        /// 统一入口：根据格式选播放方式。
        /// mp3/wav/m4a… 直接播；OGG / OPUS 等冷门格式交给 ffmpeg（检测到时）。
        /// </summary>
        private void PlayResolved(Song song, string localPath, bool autoPlay, double startAt)
        {
            if (Ffmpeg.NeedsTranscode(localPath))
            {
                string transcoded = CloudCache.TranscodedPath(song);
                if (File.Exists(transcoded))
                {
                    PlayFile(song, transcoded, autoPlay, startAt);
                    return;
                }
                StartTranscode(song, localPath, autoPlay, startAt);
                return;
            }
            PlayFile(song, localPath, autoPlay, startAt);
        }

        /// <summary>真正交给播放内核，并顺手预取下一首。</summary>
        private void PlayFile(Song song, string path, bool autoPlay, double startAt)
        {
            try
            {
                engine.Open(song, path, autoPlay, startAt);
            }
            catch (Exception ex)
            {
                ShowToast("无法播放：" + ex.Message);
            }
            PrefetchNext();
        }

        /// <summary>把 OGG / OPUS 等格式用 ffmpeg 转成 MP3 后播放（结果进缓存）。</summary>
        private void StartTranscode(Song song, string sourcePath, bool autoPlay, double startAt)
        {
            string ffmpeg = Ffmpeg.Locate(settings.FfmpegPath);
            if (string.IsNullOrEmpty(ffmpeg))
            {
                ShowToast("这首歌是 " + Path.GetExtension(sourcePath).TrimStart('.').ToUpperInvariant()
                    + " 格式，系统播放内核不支持；装一个 ffmpeg 即可自动转码播放");
                if (artistText != null) artistText.Text = song.ArtistText;
                return;
            }

            string target = CloudCache.TranscodedPath(song);
            if (artistText != null) artistText.Text = "正在转换格式…";
            ShowToast("正在把 " + Path.GetExtension(sourcePath).TrimStart('.').ToUpperInvariant()
                + " 转成 MP3（只转这一次，之后直接用缓存）");
            if (bufferingBar != null)
            {
                bufferingBar.Visibility = Visibility.Visible;
                bufferingBar.Value = 0;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                bool ok = Ffmpeg.ToMp3(ffmpeg, sourcePath, target, song.Duration, delegate(int percent)
                {
                    if (percent < 0) return;
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        if (currentSong != song) return;
                        if (bufferingBar != null) bufferingBar.Value = percent;
                        if (artistText != null) artistText.Text = "正在转换格式… " + percent + "%";
                    });
                }, out error);

                Dispatcher.BeginInvoke((Action)delegate
                {
                    if (bufferingBar != null) bufferingBar.Visibility = Visibility.Collapsed;
                    if (!ok)
                    {
                        ShowToast("转换失败：" + error);
                        if (artistText != null) artistText.Text = song.ArtistText;
                        return;
                    }
                    if (currentSong != song) return;
                    try
                    {
                        engine.Open(song, target, autoPlay, startAt);
                    }
                    catch (Exception ex)
                    {
                        ShowToast("无法播放：" + ex.Message);
                    }
                    if (artistText != null)
                        artistText.Text = song.ArtistText + (song.HasLyrics ? " · 有歌词" : "");
                    PrefetchNext();
                });
            });
        }

        /// <summary>云盘歌曲：先下载到本地缓存再播放，并显示进度。</summary>
        private void StartCloudBuffering(Song song, bool autoPlay, double startAt)
        {
            string url = CloudEndpoint;
            string target = CloudCache.FileFor(song);
            if (artistText != null) artistText.Text = "正在缓冲… 0%";
            ShowToast("正在缓冲云端歌曲：" + song.Title);
            if (bufferingBar != null)
            {
                bufferingBar.Visibility = Visibility.Visible;
                bufferingBar.Value = 0;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    TraceStep("buffer start: " + song.FileName + " -> " + target);
                    CloudClient.DownloadTo(url, song.CloudPath, target, delegate(long done, long total)
                    {
                        int percent = total > 0 ? (int)(done * 100 / total) : 0;
                        Dispatcher.BeginInvoke((Action)delegate
                        {
                            if (currentSong != song) return;
                            if (artistText != null) artistText.Text = "正在缓冲… " + percent + "%";
                            if (bufferingBar != null) bufferingBar.Value = percent;
                        });
                    });

                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        TraceStep("buffer done: " + target);
                        if (bufferingBar != null) bufferingBar.Visibility = Visibility.Collapsed;
                        if (currentSong != song) return;
                        if (artistText != null)
                            artistText.Text = song.ArtistText + (song.HasLyrics ? " · 有歌词" : "");
                        UpdateStatusText();
                        // ★ 下载完必须再走一遍「按格式选播放方式」，
                        //   否则需要 ffmpeg 转码的格式会被直接丢给系统内核（它放不了）。
                        PlayResolved(song, target, autoPlay, startAt);
                    });
                }
                catch (Exception ex)
                {
                    TraceStep("buffer failed: " + ex.Message);
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        if (bufferingBar != null) bufferingBar.Visibility = Visibility.Collapsed;
                        ShowToast("云端缓冲失败：" + ex.Message);
                        if (artistText != null) artistText.Text = song.ArtistText;
                    });
                }
            });
        }

        /// <summary>提前把队列里的下一首云端歌曲下载好。</summary>
        private void PrefetchNext()
        {
            if (!IsCloudSource || queue.Count == 0) return;
            int next = queueIndex + 1;
            if (next >= queue.Count)
                next = (settings.Mode == PlayMode.ListLoop || settings.Mode == PlayMode.Shuffle) ? 0 : -1;
            if (next < 0 || next >= queue.Count) return;
            Song song = queue[next];
            if (song == null || !song.IsCloud || song == currentSong) return;
            if (CloudCache.CachedPath(song) != null) return;

            string url = CloudEndpoint;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    CloudClient.DownloadTo(url, song.CloudPath, CloudCache.FileFor(song), null);
                    Dispatcher.BeginInvoke((Action)delegate { UpdateStatusText(); });
                }
                catch (Exception)
                {
                }
            });
        }

        /// <summary>
        /// 关闭「保留缓存」时，切歌后删掉上一首的缓存文件。
        /// 注意：如果新播放的就是同一首（重播、暂停后再点播放），不能删，
        /// 否则会把自己刚下载好的文件删掉，导致又重新下载一遍。
        /// </summary>
        private void ReleaseOldCloudCache(Song next)
        {
            if (settings.CloudCacheMode == 2) return;
            Song previous = currentSong;
            if (previous == null || !previous.IsCloud) return;
            if (next != null && object.ReferenceEquals(next, previous)) return;
            string path = CloudCache.FileFor(previous);
            try
            {
                engine.Close();
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>下一首会播哪首（顺序播放到底就是没有；随机模式随便挑一首）。</summary>
        private Song PeekNextSong()
        {
            if (queue.Count == 0) return null;
            int at = queueIndex + 1;
            if (at >= queue.Count)
            {
                if (settings.Mode == PlayMode.Sequential) return null;
                at = 0;
            }
            if (at == queueIndex) return null;
            return queue[at];
        }

        /// <summary>
        /// 空间策略：不开缓存时，本机只留「正在听的那一首 + 下一首」，其余文件删掉；
        /// .part 是正在下载的临时文件，跳过。
        /// </summary>
        private void PruneCloudCache(Song keepCurrent, Song keepNext)
        {
            if (settings.CloudCacheMode == 2) return;
            try
            {
                string a = keepCurrent != null && keepCurrent.IsCloud ? CloudCache.FileFor(keepCurrent) : null;
                string b = keepNext != null && keepNext.IsCloud ? CloudCache.FileFor(keepNext) : null;
                foreach (string file in System.IO.Directory.GetFiles(CloudCache.Directory))
                {
                    if (file.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue;
                    if (a != null && string.Equals(file, a, StringComparison.OrdinalIgnoreCase)) continue;
                    if (b != null && string.Equals(file, b, StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(file); }
                    catch (Exception) { }
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 启动时收尾：没开缓存时，本机最多留最近用过的那一个文件。
        /// 正常退出已经收过一次，这里主要防崩溃 / 断电留下的残留。
        /// </summary>
        private void TrimCloudCacheOnStart()
        {
            int mode = settings.CloudCacheMode;
            if (mode == 2) return;
            try
            {
                string[] files = System.IO.Directory.GetFiles(CloudCache.Directory);
                int keep = 2;   // 最多留「正在听的那一首 + 下一首」
                if (files.Length <= keep) return;
                Array.Sort(files, delegate(string x, string y)
                {
                    return File.GetLastWriteTimeUtc(y).CompareTo(File.GetLastWriteTimeUtc(x));
                });
                for (int i = keep; i < files.Length; i++)
                {
                    if (files[i].EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(files[i]); }
                    catch (Exception) { }
                }
            }
            catch (Exception)
            {
            }
        }

        private void UpdateCurrentFlags()
        {
            foreach (Song song in library) song.IsCurrent = song == currentSong;
            foreach (Song song in queue) song.IsCurrent = song == currentSong;
        }

        private void PlayCurrent(bool autoPlay)
        {
            if (queueIndex < 0 || queueIndex >= queue.Count) return;
            OpenTrack(queue[queueIndex], autoPlay, 0);
        }

        private void OnTrackOpened()
        {
            UpdatePlayButton();
            UpdateProgress(true);
            Raise(PlaybackStateChanged);
        }

        private void StopAndClear()
        {
            engine.Stop();
            currentSong = null;
            UpdateCurrentFlags();
            if (lyricsView != null) lyricsView.Load(null);
            if (desktopLyrics != null) desktopLyrics.UpdateNow();
            if (titleText != null) titleText.Text = "未在播放";
            if (artistText != null) artistText.Text = "双击列表中的歌曲开始播放";
            Title = AppName;
            UpdateProgress(true);
            UpdatePlayButton();
            Raise(SongChanged);
        }

        /// <summary>按当前播放模式切换到下一首。</summary>
        private void Advance(bool userInitiated)
        {
            if (queue.Count == 0)
            {
                if (userInitiated && visible.Count > 0) PlayFrom(visible, 0);
                return;
            }
            if (settings.Mode == PlayMode.SingleLoop && !userInitiated && queueIndex >= 0)
            {
                engine.Seek(0);
                engine.Play();
                UpdatePlayButton();
                return;
            }
            queueIndex++;
            if (queueIndex >= queue.Count)
            {
                if (settings.Mode == PlayMode.Sequential)
                {
                    queueIndex = queue.Count - 1;
                    engine.Stop();
                    UpdatePlayButton();
                    ShowToast("播放列表已结束");
                    return;
                }
                queueIndex = 0;
            }
            PlayCurrent(true);
        }

        private int RandomIndex()
        {
            if (queue.Count <= 1) return 0;
            Random random = new Random();
            int next = queueIndex;
            for (int i = 0; i < 20 && next == queueIndex; i++) next = random.Next(queue.Count);
            return next;
        }

        private void Tick()
        {
            if (currentSong == null) return;
            UpdateProgress(false);
            UpdateLyrics();
        }

        private double Duration
        {
            get { return engine.Duration; }
        }

        private void UpdateProgress(bool force)
        {
            if (progressSlider == null) return;
            double duration = Duration;
            double pos = engine.GetPosition();
            if (duration > 0)
            {
                progressSlider.Maximum = duration;
                if (!draggingProgress) progressSlider.Value = pos;
            }
            if (!draggingProgress)
            {
                positionText.Text = TextUtil.FormatTime(pos);
                durationText.Text = duration > 0 ? "/ " + TextUtil.FormatTime(duration) : "/ --:--";
            }
            if (force) UpdatePlayButton();
        }

        private void UpdatePlayButton()
        {
            if (playButton == null) return;
            bool playing = engine.IsPlaying;
            playButton.Content = Icons.Create(playing ? "pause" : "play", 20, "OnAccent");
        }

        private void UpdateModeButton()
        {
            if (modeButton == null) return;
            string icon = "repeat";
            if (settings.Mode == PlayMode.SingleLoop) icon = "repeat-one";
            else if (settings.Mode == PlayMode.Shuffle) icon = "shuffle";
            else if (settings.Mode == PlayMode.Sequential) icon = "sequential";
            modeButton.Content = Icons.Create(icon, 18, "TextDim");
            modeButton.ToolTip = "播放模式：" + ModeName(settings.Mode) + "（点击切换）";
        }

        public static string ModeName(PlayMode mode)
        {
            switch (mode)
            {
                case PlayMode.SingleLoop: return "单曲循环";
                case PlayMode.Shuffle: return "随机播放";
                case PlayMode.Sequential: return "顺序播放";
                default: return "列表循环";
            }
        }

        private void UpdateVolumeSlider()
        {
            if (volumeSlider == null) return;
            volumeSlider.Value = engine.Volume * 100;
        }

        private void UpdateMuteIcon(Button muteButton)
        {
            if (muteButton == null) return;
            muteButton.Content = Icons.Create(engine.IsMuted || engine.Volume <= 0.001 ? "mute" : "volume", 18, "TextDim");
        }

        public void UpdateQueueState()
        {
            if (queueCountText != null)
                queueCountText.Text = queue.Count.ToString(CultureInfo.InvariantCulture);
            if (queueButton != null)
                queueButton.ToolTip = "播放队列（" + queue.Count + " 首）";
            if (queueView != null) queueView.RefreshItems();
            // 播放列表变化后落盘，下次打开自动接着上次的列表
            SaveSettingsDebounced();
        }

        private void UpdateLyrics()
        {
            if (currentSong == null) return;
            double pos = engine.GetPosition() + settings.LyricOffset;
            int index = lyricsView != null ? lyricsView.CurrentIndex : -1;
            if (lyricsView != null)
            {
                int i = lyricsView.IndexAt(pos);
                if (i != index)
                {
                    lyricsView.SetActive(i);
                    if (desktopLyrics != null) desktopLyrics.UpdateNow();
                }
            }
            else if (desktopLyrics != null)
            {
                desktopLyrics.UpdateNow();
            }
        }

        private void UpdateSearchPlaceholder()
        {
            if (searchPlaceholder == null || searchBox == null) return;
            searchPlaceholder.Visibility = searchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public void FocusSearch()
        {
            if (searchBox == null) return;
            searchBox.Focus();
            searchBox.SelectAll();
        }

        private void OpenMusicDir()
        {
            if (IsCloudSource)
            {
                try
                {
                    if (CanDeleteCloud)
                    {
                        // 令牌模式：直接打开资料库网页（需要 repo id，后台取一次）
                        string host = CloudClient.ParseHost(CloudEndpoint);
                        string repoId = cloudRepoId;
                        if (string.IsNullOrEmpty(repoId))
                        {
                            FetchRepoInfo(CloudEndpoint);
                            repoId = cloudRepoId;
                        }
                        System.Diagnostics.Process.Start(string.IsNullOrEmpty(repoId)
                            ? host
                            : host + "/library/" + repoId + "/");
                    }
                    else
                    {
                        System.Diagnostics.Process.Start(CloudClient.ShareUrl(settings.CloudUrl));
                    }
                }
                catch (Exception)
                {
                }
                return;
            }
            if (string.IsNullOrEmpty(settings.MusicDir) || !Directory.Exists(settings.MusicDir))
            {
                ChooseMusicDir();
                return;
            }
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", "\"" + settings.MusicDir + "\"");
            }
            catch (Exception)
            {
            }
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void SettingsChangedSafe()
        {
            Raise(SettingsChanged);
        }

        private DispatcherTimer saveTimer;

        public void SaveSettingsDebounced()
        {
            if (saveTimer == null)
            {
                saveTimer = new DispatcherTimer();
                saveTimer.Interval = TimeSpan.FromMilliseconds(600);
                saveTimer.Tick += delegate
                {
                    saveTimer.Stop();
                    SaveSettings();
                };
            }
            saveTimer.Stop();
            saveTimer.Start();
        }

        #endregion

        #region 托盘 / 热键 / 窗口

        private static Drawing.Icon AppIcon()
        {
            try
            {
                return Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetEntryAssembly().Location);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void ApplyWindowIcon()
        {
            Drawing.Icon icon = AppIcon();
            if (icon == null) return;
            ImageSource source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            Icon = source;
        }

        private void InitTray()
        {
            ApplyWindowIcon();
            if (MainWindow.Headless) return;
            tray = new Forms.NotifyIcon();
            Drawing.Icon icon = AppIcon();
            if (icon != null) tray.Icon = icon;
            tray.Text = AppName;
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowFromTray(); };

            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("显示主界面", null, delegate { ShowFromTray(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("播放 / 暂停", null, delegate { Dispatcher.BeginInvoke((Action)delegate { TogglePlay(); }); });
            menu.Items.Add("上一首", null, delegate { Dispatcher.BeginInvoke((Action)delegate { Previous(); }); });
            menu.Items.Add("下一首", null, delegate { Dispatcher.BeginInvoke((Action)delegate { Next(); }); });
            menu.Items.Add("桌面歌词", null, delegate
            {
                Dispatcher.BeginInvoke((Action)delegate { ShowDesktopLyrics(!settings.DesktopLyricsOn); });
            });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Dispatcher.BeginInvoke((Action)delegate { ExitApp(); }); });
            tray.ContextMenuStrip = menu;
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
        }

        private void ExitApp()
        {
            reallyExit = true;
            Close();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            TraceStep("closing");
            if (!reallyExit && settings.CloseToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            reallyExit = true;
            // 退出时不必手动注销热键：进程结束系统会自动释放，
            // 在窗口关闭过程中调用 UnregisterHotKey 反而可能卡住消息循环。
            TraceStep("hotkeys skipped");
            SaveStateBeforeExit();
            TraceStep("state saved");
        }

        private void SaveStateBeforeExit()
        {
            try
            {
                if (currentSong != null)
                {
                    settings.LastSongPath = currentSong.Path;
                    settings.LastPosition = engine.GetPosition();
                }
                if (WindowState == WindowState.Normal)
                {
                    settings.WindowWidth = Width;
                    settings.WindowHeight = Height;
                    settings.WindowX = Left;
                    settings.WindowY = Top;
                }
                settings.WindowMaximized = WindowState == WindowState.Maximized;
                if (desktopLyrics != null)
                {
                    settings.LyricX = desktopLyrics.Left;
                    settings.LyricY = desktopLyrics.Top;
                }
                SaveSettings();
            }
            catch (Exception)
            {
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            TraceStep("closed: stop timer");
            if (timer != null) timer.Stop();
            TraceStep("closed: engine");
            engine.Close();
            // 不开缓存时，退出也只留「正在听的那一首 + 下一首」，多余的清掉
            if (settings.CloudCacheMode != 2) PruneCloudCache(currentSong, PeekNextSong());
            TraceStep("closed: tray");
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
                tray = null;
            }
            TraceStep("closed: lyrics");
            if (desktopLyrics != null) desktopLyrics.Close();
            TraceStep("closed: shutdown");
            Application.Current.Shutdown();
            TraceStep("closed: done");
        }

        /// <summary>仅在自检 / 冒烟模式下写步骤日志，方便定位卡死。</summary>
        internal static void TraceStep(string step)
        {
            if (!Headless) return;
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skylark-smoke.log");
                using (System.IO.FileStream fs = new System.IO.FileStream(path, System.IO.FileMode.Append,
                    System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite))
                using (System.IO.StreamWriter writer = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8))
                {
                    writer.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  [win] " + step);
                }
            }
            catch (Exception)
            {
            }
        }

        private void HideToTray()
        {
            Hide();
            if (!trayTipShown)
            {
                trayTipShown = true;
                if (tray != null) tray.ShowBalloonTip(2000, AppName, "程序已最小化到托盘，双击图标可以恢复。", Forms.ToolTipIcon.Info);
            }
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && settings.MinimizeToTray)
            {
                HideToTray();
            }
        }

        private void RestoreWindowBounds()
        {
            if (settings.WindowWidth >= MinWidth && settings.WindowHeight >= MinHeight)
            {
                Width = settings.WindowWidth;
                Height = settings.WindowHeight;
            }
            if (!double.IsNaN(settings.WindowX) && !double.IsNaN(settings.WindowY)
                && settings.WindowX > AppSettings.Unset + 1 && settings.WindowY > AppSettings.Unset + 1)
            {
                double vLeft = SystemParameters.VirtualScreenLeft;
                double vTop = SystemParameters.VirtualScreenTop;
                double vRight = vLeft + SystemParameters.VirtualScreenWidth;
                double vBottom = vTop + SystemParameters.VirtualScreenHeight;
                if (settings.WindowX >= vLeft - 50 && settings.WindowX < vRight - 100
                    && settings.WindowY >= vTop - 20 && settings.WindowY < vBottom - 80)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = settings.WindowX;
                    Top = settings.WindowY;
                }
            }
            if (settings.WindowMaximized) WindowState = WindowState.Maximized;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool inTextBox = Keyboard.FocusedElement is TextBox;
            if (e.Key == Key.Escape && inTextBox)
            {
                searchBox.Text = string.Empty;
                e.Handled = true;
                return;
            }
            if (inTextBox) return;

            ModifierKeys mods = Keyboard.Modifiers;
            switch (e.Key)
            {
                case Key.Space:
                    TogglePlay();
                    e.Handled = true;
                    break;
                case Key.Left:
                    if (mods == ModifierKeys.Control) Previous();
                    else SeekTo(Math.Max(0, engine.GetPosition() - 5));
                    e.Handled = true;
                    break;
                case Key.Right:
                    if (mods == ModifierKeys.Control) Next();
                    else SeekTo(engine.GetPosition() + 5);
                    e.Handled = true;
                    break;
                case Key.Up:
                    engine.Volume = Math.Min(1, engine.Volume + 0.05);
                    UpdateVolumeSlider();
                    SaveSettingsDebounced();
                    e.Handled = true;
                    break;
                case Key.Down:
                    engine.Volume = Math.Max(0, engine.Volume - 0.05);
                    UpdateVolumeSlider();
                    SaveSettingsDebounced();
                    e.Handled = true;
                    break;
                case Key.F:
                    FocusSearch();
                    e.Handled = true;
                    break;
                case Key.L:
                    ShowView("lyrics");
                    e.Handled = true;
                    break;
                case Key.Q:
                    ShowView("queue");
                    e.Handled = true;
                    break;
                case Key.D:
                    ShowDesktopLyrics(!settings.DesktopLyricsOn);
                    e.Handled = true;
                    break;
                case Key.M:
                    engine.IsMuted = !engine.IsMuted;
                    SettingsChangedSafe();
                    break;
            }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const int WM_SETTINGCHANGE = 0x001A;
        private const int HotkeyPlay = 1;
        private const int HotkeyPrev = 2;
        private const int HotkeyNext = 3;
        private const int HotkeyLockLyrics = 4;
        private const int HotkeyToggleLyrics = 5;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint VkMediaPlayPause = 0xB3;
        private const uint VkMediaPrev = 0xB1;
        private const uint VkMediaNext = 0xB0;

        private HwndSource hotkeySource;
        private bool mediaKeysRegistered;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hotkeySource = (HwndSource)PresentationSource.FromVisual(this);
            if (hotkeySource != null) hotkeySource.AddHook(WndProc);
            RegisterMediaKeys();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SETTINGCHANGE && Theme.Mode == "system")
            {
                string before = Theme.Current;
                Theme.Apply("system");
                if (Theme.Current != before)
                {
                    Background = (Brush)Application.Current.Resources["Window"];
                    ApplyDarkTitleBar();
                    Raise(SettingsChanged);
                }
            }
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HotkeyPlay)
                {
                    TogglePlay();
                    handled = true;
                }
                else if (id == HotkeyPrev)
                {
                    Previous();
                    handled = true;
                }
                else if (id == HotkeyNext)
                {
                    Next();
                    handled = true;
                }
                else if (id == HotkeyLockLyrics)
                {
                    ToggleLyricLock();
                    handled = true;
                }
                else if (id == HotkeyToggleLyrics)
                {
                    ShowDesktopLyrics(!settings.DesktopLyricsOn);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void RegisterMediaKeys()
        {
            if (hotkeySource == null) return;
            UnregisterMediaKeys();
            if (!settings.MediaKeys) return;
            IntPtr handle = new WindowInteropHelper(this).Handle;
            bool any = false;
            if (RegisterHotKey(handle, HotkeyPlay, 0, VkMediaPlayPause)) any = true;
            if (RegisterHotKey(handle, HotkeyPrev, 0, VkMediaPrev)) any = true;
            if (RegisterHotKey(handle, HotkeyNext, 0, VkMediaNext)) any = true;

            uint mods = ModControl | ModAlt;
            if (RegisterHotKey(handle, HotkeyLockLyrics, mods, (uint)KeyInterop.VirtualKeyFromKey(Key.L))) any = true;
            if (RegisterHotKey(handle, HotkeyToggleLyrics, mods, (uint)KeyInterop.VirtualKeyFromKey(Key.D))) any = true;
            mediaKeysRegistered = any;
        }

        private void UnregisterMediaKeys()
        {
            if (!mediaKeysRegistered) return;
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                UnregisterHotKey(handle, HotkeyPlay);
                UnregisterHotKey(handle, HotkeyPrev);
                UnregisterHotKey(handle, HotkeyNext);
                UnregisterHotKey(handle, HotkeyLockLyrics);
                UnregisterHotKey(handle, HotkeyToggleLyrics);
            }
            catch (Exception)
            {
            }
            mediaKeysRegistered = false;
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                int value = Theme.Current == "dark" ? 1 : 0;
                if (DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, 19, ref value, sizeof(int));
            }
            catch (Exception)
            {
            }
        }

        public void RefreshTitleBar()
        {
            ApplyDarkTitleBar();
        }

        #endregion

        private static Color ParseColor(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
    }
}



