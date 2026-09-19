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

    }
}
