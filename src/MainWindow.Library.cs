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
            folderPlaylists.Clear();
            if (result.Playlists != null) folderPlaylists.AddRange(result.Playlists);
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
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Song song in library)
            {
                // 歌单筛选：只在选中的歌单里找
                if (playlistFilter.Length > 0
                    && !string.Equals(song.Playlist, playlistFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (key.Length == 0
                    || song.Title.ToLowerInvariant().Contains(key)
                    || song.Artist.ToLowerInvariant().Contains(key)
                    || song.FileName.ToLowerInvariant().Contains(key))
                {
                    // 「全部」视图里，同一首歌出现在多个歌单时不重复显示（进具体歌单能看到那份拷贝）
                    if (playlistFilter.Length == 0)
                    {
                        string id = song.Title + "\u0001" + song.Artist;
                        if (!seen.Add(id)) continue;
                    }
                    list.Add(song);
                }
            }
            SortSongs(list);
            visible = list;
            if (libraryView != null) libraryView.RefreshItems();
            if (libraryCountText != null)
                libraryCountText.Text = DistinctCount().ToString(CultureInfo.InvariantCulture);
            RefreshPlaylists();
        }

        /// <summary>
        /// 按「歌名 + 歌手」去重后的曲目数：一首歌放进两个歌单只算一首，
        /// 侧栏数字和「全部歌曲」视图的口径跟它保持一致。
        /// </summary>
        public int DistinctCount()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < library.Count; i++)
                seen.Add(library[i].Title + "\u0001" + library[i].Artist);
            return seen.Count;
        }

        /// <summary>
        /// 刷新侧栏的歌单列表。歌单＝云盘上的文件夹，所以直接看歌曲的所属文件夹。
        /// 列表没变化时不做重建，免得每次筛选都闪一下。
        /// </summary>
        private void RefreshPlaylists()
        {
            // 歌单来源＝扫描到的文件夹（空歌单也在）+ 歌曲里出现过的歌单
            List<string> names = PlaylistNames();
            if (playlistFilter.Length > 0 && !names.Contains(playlistFilter)) playlistFilter = "";

            string signature = playlistFilter + "|" + string.Join("|", names.ToArray());
            if (signature == playlistSignature) return;
            playlistSignature = signature;

            playlistNav.Children.Clear();
            playlistNav.Children.Add(NavHeader("歌单"));
            playlistNav.Children.Add(PlaylistRow("全部歌曲", "", DistinctCount()));
            for (int i = 0; i < names.Count; i++)
            {
                int count = 0;
                for (int k = 0; k < library.Count; k++)
                    if (string.Equals(library[k].Playlist, names[i], StringComparison.OrdinalIgnoreCase)) count++;
                playlistNav.Children.Add(PlaylistRow(names[i], names[i], count));
            }
            Button create = Ui.Button("＋ 新建歌单", "OutlineButton", delegate { NewPlaylist(); });
            create.Margin = new Thickness(6, 6, 6, 0);
            playlistNav.Children.Add(create);
        }

        // ---------------- 歌单（一个云盘文件夹＝一个歌单） ----------------

        private static string lastDownloadDir;

        /// <summary>下载选中的歌：先问「下什么」（音频 / 歌词 / 两者），再后台下载。</summary>
        public void DownloadSongs(List<Song> songs)
        {
            if (songs == null || songs.Count == 0)
            {
                ShowToast("先点「批量编辑」勾几首歌，或点每行右边的下载图标");
                return;
            }
            if (string.IsNullOrEmpty(lastDownloadDir))
            {
                string music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                lastDownloadDir = string.IsNullOrEmpty(music)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : music;
            }
            List<Song> picked = new List<Song>(songs);

            TextBlock where = Ui.Text(lastDownloadDir, 13, "Text");
            where.TextWrapping = TextWrapping.Wrap;
            TextBlock caption = Ui.Text("保存到", 11.5, "TextMuted");
            StackPanel place = Ui.Column(3, caption, where);
            place.VerticalAlignment = VerticalAlignment.Center;

            Button change = Ui.Button("更改…", "GhostButton", delegate
            {
                Forms.FolderBrowserDialog pick = new Forms.FolderBrowserDialog();
                pick.Description = "选择下载保存的位置";
                pick.SelectedPath = lastDownloadDir;
                if (pick.ShowDialog() == Forms.DialogResult.OK)
                {
                    lastDownloadDir = pick.SelectedPath;
                    where.Text = lastDownloadDir;
                }
            });
            change.VerticalAlignment = VerticalAlignment.Center;

            Grid placeBox = new Grid();
            placeBox.ColumnDefinitions.Add(new ColumnDefinition());
            placeBox.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            placeBox.ColumnDefinitions.Add(new ColumnDefinition());
            placeBox.ColumnDefinitions[1].Width = GridLength.Auto;
            placeBox.Children.Add(place);
            Grid.SetColumn(change, 1);
            placeBox.Children.Add(change);
            Border placeCard = new Border();
            placeCard.Style = (Style)Application.Current.Resources["CardBox"];
            placeCard.Padding = new Thickness(14, 11, 12, 11);
            placeCard.Child = placeBox;

            ShowModal("下载 " + picked.Count + " 首",
                "下到本机，随时可以拷到别的地方听。",
                placeCard,
                new List<ModalAction>
                {
                    new ModalAction("取消", "OutlineButton", null),
                    new ModalAction("只下歌词", "OutlineButton",
                        delegate { StartDownload(picked, lastDownloadDir, false, true); }),
                    new ModalAction("只下音频", "OutlineButton",
                        delegate { StartDownload(picked, lastDownloadDir, true, false); }),
                    new ModalAction("音频 + 歌词", "PrimaryButton",
                        delegate { StartDownload(picked, lastDownloadDir, true, true); })
                });
        }

        private void StartDownload(List<Song> songs, string dir, bool audio, bool lyrics)
        {
            lastDownloadDir = dir;
            string endpoint = CloudEndpoint;
            ShowToast("正在下载 " + songs.Count + " 首…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0;
                int failed = 0;
                for (int i = 0; i < songs.Count; i++)
                {
                    try
                    {
                        if (audio) DownloadOne(songs[i], dir, endpoint);
                        if (lyrics) DownloadLyric(songs[i], dir, endpoint);
                        ok++;
                    }
                    catch (Exception)
                    {
                        failed++;
                    }
                }
                Dispatcher.BeginInvoke((Action)delegate
                {
                    ShowToast("下载完成：成功 " + ok + " 首"
                        + (failed > 0 ? "，失败 " + failed + " 首" : "") + " → " + dir);
                });
            });
        }

        private void DownloadOne(Song song, string dir, string endpoint)
        {
            string target = System.IO.Path.Combine(dir, song.FileName);
            if (!song.IsCloud)
            {
                System.IO.File.Copy(song.Path, target, true);
                return;
            }
            CloudClient.DownloadTo(endpoint, song.CloudPath, target, null);
        }

        /// <summary>下载这首歌的歌词（没有歌词就跳过）。</summary>
        private void DownloadLyric(Song song, string dir, string endpoint)
        {
            string target = System.IO.Path.Combine(dir,
                System.IO.Path.ChangeExtension(song.FileName, ".lrc"));
            if (!song.IsCloud)
            {
                string local = System.IO.Path.ChangeExtension(song.Path, ".lrc");
                if (System.IO.File.Exists(local)) System.IO.File.Copy(local, target, true);
                return;
            }
            if (string.IsNullOrEmpty(song.LyricPath)) return;
            string prefix = CloudLibrary.PseudoScheme + CloudClient.ParseToken(endpoint);
            string path = song.LyricPath.StartsWith(prefix, StringComparison.Ordinal)
                ? song.LyricPath.Substring(prefix.Length)
                : song.LyricPath;
            CloudClient.DownloadTo(endpoint, path, target, null);
        }

        /// <summary>从云盘删除选中的歌（连同同名歌词），会二次确认。</summary>
        public void DeleteSongsFromCloud(List<Song> songs)
        {
            if (songs == null || songs.Count == 0)
            {
                ShowToast("先点「批量编辑」勾几首歌");
                return;
            }
            if (!IsCloudSource || !CanDeleteCloud)
            {
                ShowToast("当前是分享链接模式，删不了云端文件");
                return;
            }
            List<Song> picked = new List<Song>(songs);
            string endpoint = CloudEndpoint;
            ShowModal("从云盘删除 " + picked.Count + " 首歌？",
                "这是直接从云盘上删，连同同名歌词一起删掉。删了就找不回来了，"
                + "只能自己去云盘的历史记录里翻。",
                null,
                new List<ModalAction>
                {
                    new ModalAction("取消", "OutlineButton", null),
                    new ModalAction("删除", "DangerButton", delegate
                    {
                        RunCloudAction("删除", delegate
                        {
                            foreach (Song song in picked)
                            {
                                if (!song.IsCloud) continue;
                                string dir = string.IsNullOrEmpty(song.Playlist) ? "/" : PlaylistDir(song.Playlist);
                                List<string> items = new List<string>();
                                items.Add(song.FileName);
                                items.Add(System.IO.Path.ChangeExtension(song.FileName, ".lrc"));
                                try
                                {
                                    CloudClient.DeleteFiles(endpoint, dir, items);
                                }
                                catch (Exception)
                                {
                                    // 没有歌词时整批会失败，退一步只删音频
                                    CloudClient.DeleteFiles(endpoint, dir,
                                        new List<string>(new string[] { song.FileName }));
                                }
                            }
                        });
                    })
                });
        }

        /// <summary>把选中项移出播放队列（不动云盘文件）。</summary>
        public void RemoveFromQueue(List<Song> songs)
        {
            if (songs == null || songs.Count == 0) return;
            bool removedCurrent = false;
            foreach (Song song in songs)
            {
                int at = queue.IndexOf(song);
                if (at < 0) continue;
                queue.RemoveAt(at);
                if (at < queueIndex) queueIndex--;
                else if (at == queueIndex) removedCurrent = true;
            }
            if (queue.Count == 0)
            {
                queueIndex = -1;
                if (removedCurrent) StopAndClear();
            }
            else if (removedCurrent)
            {
                if (queueIndex >= queue.Count) queueIndex = queue.Count - 1;
                if (queueIndex < 0) queueIndex = 0;
                PlayCurrent(true);
            }
            UpdateQueueState();
            Raise(QueueChanged);
        }

        /// <summary>在后台执行云盘操作，完成后自动重扫曲库。</summary>
        private void RunCloudAction(string label, Action work)
        {
            ShowToast(label + "…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    work();
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        ShowToast(label + "完成");
                        Rescan();
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke((Action)delegate { ShowToast(label + "失败：" + ex.Message); });
                }
            });
        }

        private string PlaylistDir(string name)
        {
            return "/" + name.Trim().Trim('/');
        }

        private static bool IsValidPlaylistName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOfAny(new char[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) < 0;
        }

        public void NewPlaylist()
        {
            PromptText("新建歌单", "云盘上会新建一个同名文件夹，上传的歌就放进这个文件夹里。", "",
                delegate(string input)
                {
                    string name = input == null ? "" : input.Trim();
                    if (!IsValidPlaylistName(name))
                    {
                        ShowToast("这个名字不能用在文件夹上");
                        return;
                    }
                    string target = name;
                    RunCloudAction("新建歌单", delegate
                    {
                        if (IsCloudSource) CloudClient.EnsureDir(CloudEndpoint, PlaylistDir(target));
                        else System.IO.Directory.CreateDirectory(System.IO.Path.Combine(settings.MusicDir, target));
                    });
                });
        }

        /// <summary>新建歌单，并把选中的歌复制进去（「加入歌单」里没有合适的歌单时用）。</summary>
        private void NewPlaylistFor(List<Song> songs)
        {
            PromptText("新建歌单", "云盘上会新建一个同名文件夹，选中的歌会复制进去。", "",
                delegate(string input)
                {
                    string name = input == null ? "" : input.Trim();
                    if (!IsValidPlaylistName(name))
                    {
                        ShowToast("这个名字不能用在文件夹上");
                        return;
                    }
                    AddSongsToPlaylist(songs, name);
                });
        }

        public void RenamePlaylist(string oldName)
        {
            if (string.IsNullOrEmpty(oldName)) return;
            PromptText("重命名歌单", "歌单文件夹里的歌会整体搬到新文件夹（服务端直接搬，不重新上传）。", oldName,
                delegate(string input)
                {
                    string name = input == null ? "" : input.Trim();
                    if (name.Length == 0 || name == oldName) return;
                    if (!IsValidPlaylistName(name))
                    {
                        ShowToast("这个名字不能用在文件夹上");
                        return;
                    }
                    RunCloudAction("重命名歌单", delegate
                    {
                        if (!IsCloudSource)
                        {
                            System.IO.Directory.Move(System.IO.Path.Combine(settings.MusicDir, oldName),
                                System.IO.Path.Combine(settings.MusicDir, name));
                            return;
                        }
                        CloudClient.EnsureDir(CloudEndpoint, PlaylistDir(name));
                        List<CloudEntry> files = CloudClient.ListAllFiles(CloudEndpoint, 3);
                        List<string> moving = new List<string>();
                        foreach (CloudEntry file in files)
                        {
                            if (file.IsDirectory) continue;
                            if (CloudClient.PlaylistOf(file.Path) == oldName) moving.Add(file.Name);
                        }
                        if (moving.Count > 0)
                            CloudClient.MoveItems(CloudEndpoint, PlaylistDir(oldName), moving, PlaylistDir(name));
                        CloudClient.DeleteFiles(CloudEndpoint, "/", new List<string>(new string[] { oldName }));
                    });
                });
        }

        public void DeletePlaylist(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            ShowModal("删除歌单「" + name + "」？",
                "歌单就是云盘上的一个文件夹，删除会把里面的音频和歌词一起删掉，恢复只能自己去云盘的历史记录里找。",
                null,
                new List<ModalAction>
                {
                    new ModalAction("取消", "OutlineButton", null),
                    new ModalAction("删除", "DangerButton", delegate
                    {
                        RunCloudAction("删除歌单", delegate
                        {
                            if (IsCloudSource) CloudClient.DeleteFiles(CloudEndpoint, "/", new List<string>(new string[] { name }));
                            else System.IO.Directory.Delete(System.IO.Path.Combine(settings.MusicDir, name), true);
                        });
                    })
                });
        }

        /// <summary>把选中的歌复制到某个歌单（一首歌要进两个歌单＝两处各放一份音频+歌词）。</summary>
        public void AddSongsToPlaylist(List<Song> songs, string target)
        {
            if (songs == null || songs.Count == 0 || string.IsNullOrEmpty(target)) return;
            List<Song> copy = new List<Song>(songs);
            RunCloudAction("加入歌单", delegate
            {
                if (IsCloudSource)
                {
                    CloudClient.EnsureDir(CloudEndpoint, PlaylistDir(target));
                    foreach (Song song in copy)
                    {
                        string from = string.IsNullOrEmpty(song.Playlist) ? "/" : PlaylistDir(song.Playlist);
                        List<string> items = new List<string>();
                        items.Add(song.FileName);
                        string lrcName = System.IO.Path.ChangeExtension(song.FileName, ".lrc");
                        items.Add(lrcName);
                        try
                        {
                            CloudClient.CopyItems(CloudEndpoint, from, items, PlaylistDir(target));
                        }
                        catch (Exception)
                        {
                            // 没有歌词时会整体失败，退一步只复制音频
                            CloudClient.CopyItems(CloudEndpoint, from,
                                new List<string>(new string[] { song.FileName }), PlaylistDir(target));
                        }
                    }
                    return;
                }
                foreach (Song song in copy)
                {
                    string to = System.IO.Path.Combine(settings.MusicDir, target, song.FileName);
                    System.IO.File.Copy(song.Path, to, true);
                    string lrc = System.IO.Path.ChangeExtension(song.Path, ".lrc");
                    if (System.IO.File.Exists(lrc))
                        System.IO.File.Copy(lrc, System.IO.Path.ChangeExtension(to, ".lrc"), true);
                }
            });
        }

        /// <summary>
        /// 选歌单（「加入歌单」用）：正文是一列跟侧栏一样的歌单行，点一下选中，再点「加入」。
        /// </summary>
        private UIElement BuildPlaylistPicker(List<string> names, string initial,
            out Func<string> getPicked)
        {
            StackPanel list = new StackPanel();
            List<ToggleButton> rows = new List<ToggleButton>();
            string picked = string.IsNullOrEmpty(initial) ? names[0] : initial;
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                ToggleButton row = new ToggleButton();
                row.Style = (Style)Application.Current.Resources["NavToggle"];
                row.Content = Ui.Text(name, 13, "Text");
                row.HorizontalContentAlignment = HorizontalAlignment.Left;
                row.IsChecked = name == picked;
                row.Margin = new Thickness(0, 1, 0, 1);
                row.Click += delegate
                {
                    picked = name;
                    foreach (ToggleButton other in rows) other.IsChecked = other == row;
                };
                rows.Add(row);
                list.Children.Add(row);
            }
            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.MaxHeight = 240;
            scroll.Content = list;
            getPicked = delegate { return picked; };
            return scroll;
        }

        /// <summary>当前曲库里出现过的歌单名（歌单＝云盘上的一个文件夹）。</summary>
        public List<string> PlaylistNames()
        {
            List<string> names = new List<string>();
            for (int i = 0; i < folderPlaylists.Count; i++)
            {
                string folder = folderPlaylists[i];
                if (!string.IsNullOrEmpty(folder) && !names.Contains(folder)) names.Add(folder);
            }
            for (int i = 0; i < library.Count; i++)
            {
                string name = library[i].Playlist;
                if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
            }
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            return names;
        }

        /// <summary>「加入歌单」：先让用户选一个歌单，再把选中的歌复制过去。</summary>
        public void AddSelectionToPlaylist(List<Song> songs)
        {
            if (songs == null || songs.Count == 0)
            {
                ShowToast("先点「批量编辑」勾几首歌");
                return;
            }
            List<string> names = PlaylistNames();
            if (names.Count == 0)
            {
                // 一个歌单都还没有：直接让用户建一个，建完把歌放进去
                NewPlaylistFor(songs);
                return;
            }
            Func<string> picked;
            UIElement picker = BuildPlaylistPicker(names, playlistFilter, out picked);
            ShowModal("加入歌单",
                "歌会被复制一份过去，原来的歌单里那份还在。",
                picker,
                new List<ModalAction>
                {
                    new ModalAction("取消", "OutlineButton", null),
                    new ModalAction("新建歌单…", "OutlineButton", delegate { NewPlaylistFor(songs); }),
                    new ModalAction("加入", "PrimaryButton", delegate
                    {
                        string target = picked();
                        if (!string.IsNullOrEmpty(target)) AddSongsToPlaylist(songs, target);
                    })
                }, 400);
        }

        /// <summary>
        /// 删除选中的歌：云盘令牌模式＝从云盘删（二次确认）；本地文件夹模式＝只从列表隐藏；
        /// 分享链接模式＝删不了云端文件。
        /// </summary>
        public void DeleteSongs(List<Song> songs)
        {
            if (songs == null || songs.Count == 0)
            {
                ShowToast("先点「批量编辑」勾几首歌");
                return;
            }
            bool hasCloud = false;
            for (int i = 0; i < songs.Count; i++) if (songs[i].IsCloud) hasCloud = true;

            if (!hasCloud)
            {
                foreach (Song song in songs)
                {
                    if (!settings.Hidden.Contains(song.Path)) settings.Hidden.Add(song.Path);
                    library.Remove(song);
                    queue.Remove(song);
                }
                SaveSettings();
                ApplyFilter();
                Raise(QueueChanged);
                Raise(LibraryChanged);
                ShowToast("已从音乐库移除 " + songs.Count + " 首（本地文件没动）");
                return;
            }
            DeleteSongsFromCloud(songs);
        }

        /// <summary>极简输入框（新建 / 重命名歌单）。</summary>
        private void PromptText(string title, string hint, string initial, Action<string> onOk)
        {
            TextBox input = new TextBox();
            input.Style = (Style)Application.Current.Resources["InputBox"];
            input.Text = initial == null ? "" : initial;
            input.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                CloseModal();
                if (onOk != null) onOk(input.Text);
            };
            ShowModal(title, hint, input,
                new List<ModalAction>
                {
                    new ModalAction("取消", "OutlineButton", null),
                    new ModalAction("确定", "PrimaryButton",
                        delegate { if (onOk != null) onOk(input.Text); })
                }, 400);
        }

        private UIElement NavHeader(string text)
        {
            TextBlock t = Ui.Text(text, 11.5, "TextMuted");
            t.Margin = new Thickness(12, 14, 6, 4);
            return t;
        }

        /// <summary>侧栏的歌单行：点一下把音乐库筛到这个歌单。</summary>
        private UIElement PlaylistRow(string label, string value, int count)
        {
            ToggleButton toggle = new ToggleButton();
            toggle.Style = (Style)Application.Current.Resources["NavToggle"];
            StackPanel row = Ui.Row(0, Icons.Create(value.Length == 0 ? "library" : "music", 16, "TextDim"));
            TextBlock name = Ui.Text(label, 13, "Text");
            name.Margin = new Thickness(10, 0, 0, 0);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(name);
            if (count >= 0)
            {
                TextBlock number = Ui.Text(count.ToString(CultureInfo.InvariantCulture), 11.5, "TextMuted");
                number.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(number);
            }
            toggle.Content = row;
            toggle.IsChecked = playlistFilter == value;
            string captured = value;
            toggle.Click += delegate
            {
                ShowView("library");
                SetPlaylistFilter(captured);
            };
            if (value.Length > 0)
            {
                // 右键歌单：改名 / 删除（歌单就是云盘上的一个文件夹）
                ContextMenu menu = new ContextMenu();
                MenuItem rename = new MenuItem();
                rename.Header = "重命名歌单…";
                rename.Click += delegate { RenamePlaylist(captured); };
                MenuItem remove = new MenuItem();
                remove.Header = "删除歌单…";
                remove.Click += delegate { DeletePlaylist(captured); };
                menu.Items.Add(rename);
                menu.Items.Add(remove);
                toggle.ContextMenu = menu;
            }
            toggle.Margin = new Thickness(0, 1, 0, 1);
            return toggle;
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

    }
}
