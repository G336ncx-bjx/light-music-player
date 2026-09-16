using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace LightMusic
{
    /// <summary>
    /// 仅用于开发期自检 / 截图的美化数据注入（不参与正常使用流程）。
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>无界面模式下运行（不创建托盘图标）。</summary>
        public static bool Headless;

        /// <summary>冒烟测试：播放列表里的第一首，返回描述。</summary>
        public string SmokePlayFirst()
        {
            return SmokePlayFirst(null);
        }

        /// <summary>冒烟测试：播放列表里第一首（可按标题关键字筛选）。</summary>
        public string SmokePlayFirst(string keyword)
        {
            if (visible.Count == 0) return "列表为空";
            Song song = visible[0];
            if (!string.IsNullOrEmpty(keyword))
            {
                foreach (Song candidate in visible)
                {
                    if (candidate.Title.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                        || candidate.FileName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        song = candidate;
                        break;
                    }
                }
            }
            PlaySong(song);
            return "开始播放：" + song.Title + (song.IsCloud ? "（云盘）" : "（本地）");
        }

        /// <summary>冒烟测试：当前播放状态描述。</summary>
        public string SmokeState()
        {
            return "song=" + (currentSong == null ? "-" : currentSong.Title)
                + " playing=" + engine.IsPlaying
                + " pos=" + engine.GetPosition().ToString("0.0") + "s"
                + " duration=" + engine.Duration.ToString("0.0") + "s"
                + " queue=" + queue.Count
                + " cached=" + CloudCache.Count()
                + " lyrics=" + (lyricsView.Lines.Count > 0 ? lyricsView.Lines.Count.ToString() : "-");
        }

        /// <summary>冒烟测试：左下角状态文字（用于确认没有把令牌显示出来）。</summary>
        public string SmokeStatusText()
        {
            return "status=" + (statusText == null ? "-" : statusText.Text)
                + " | source=" + (dirLabel == null ? "-" : dirLabel.Text);
        }

        public void LoadDemoForShot(string view, string lyricPath)
        {
            string[] titles = new string[]
            {
                "冬眠", "晴天", "起风了", "孤勇者", "夜空中最亮的星", "平凡之路",
                "卡农", "Merry Christmas Mr. Lawrence", "City of Stars", "夜曲"
            };
            string[] artists = new string[]
            {
                "司南", "周杰伦", "买辣椒也用券", "陈奕迅", "逃跑计划", "朴树",
                "Johann Pachelbel", "坂本龙一", "Ryan Gosling、Emma Stone", "周杰伦"
            };
            double[] durations = new double[]
            {
                238, 269, 325, 256, 252, 302, 216, 288, 154, 227
            };

            library = new List<Song>();
            if (string.IsNullOrEmpty(settings.MusicDir)) settings.MusicDir = "D:\\Lai Siyu\\music";
            for (int i = 0; i < titles.Length; i++)
            {
                Song song = new Song();
                song.Title = titles[i];
                song.Artist = artists[i];
                song.Artists = TextUtil.SplitArtists(artists[i]);
                song.FileName = titles[i] + " - " + artists[i] + ".mp3";
                song.Path = Path.Combine(settings.MusicDir == null ? "D:\\music" : settings.MusicDir, song.FileName);
                song.Duration = durations[i];
                song.Size = 9000000;
                song.IsCloud = true;
                song.CloudPath = "/" + song.FileName;
                if (lyricPath != null)
                {
                    song.LyricPath = lyricPath;
                    // 云盘歌曲的歌词文本是扫描时后台取回来的，这里直接填好方便截图
                    try { song.LyricText = File.ReadAllText(lyricPath); } catch (Exception) { }
                }
                library.Add(song);
            }
            ApplyFilter();

            queue = new List<Song>(library);
            queueIndex = 0;
            for (int i = 0; i < queue.Count; i++)
            {
                if (queue[i].Title == "晴天") queueIndex = i;
            }
            currentSong = queue[queueIndex];
            UpdateCurrentFlags();
            lyricsView.Load(currentSong);
            lyricsView.SetActive(view == "lyrics-late" ? 45 : 2);

            double total = 0;
            foreach (Song song in library) total += song.Duration;
            statusText.Text = "云盘 · " + library.Count + " 首 · 已缓存 6 首 · " + Math.Round(total / 60) + " 分";
            dirLabel.Text = "云盘：" + (string.IsNullOrEmpty(settings.CloudUrl)
                ? "https://cloud.tsinghua.edu.cn/d/xxxxxxxxxxxx/"
                : settings.CloudUrl);

            titleText.Text = currentSong.Title;
            artistText.Text = currentSong.ArtistText + " · 有歌词";
            UpdatePlayButton();
            UpdateQueueState();
            UpdateProgress(true);
            progressSlider.Maximum = 269;
            progressSlider.Value = 96;
            positionText.Text = "1:36";
            durationText.Text = "/ 4:29";
            ShowView(view == "lyrics-late" ? "lyrics" : view);
        }
    }
}
