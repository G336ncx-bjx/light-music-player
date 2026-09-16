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
                if (lyricPath != null) song.LyricPath = lyricPath;
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
            lyricsView.SetActive(2);

            double total = 0;
            foreach (Song song in library) total += song.Duration;
            statusText.Text = "共 " + library.Count + " 首 · 时长 " + Math.Round(total / 60) + " 分";
            dirLabel.Text = settings.MusicDir;

            titleText.Text = currentSong.Title;
            artistText.Text = currentSong.ArtistText + " · 有歌词";
            UpdatePlayButton();
            UpdateQueueState();
            UpdateProgress(true);
            progressSlider.Maximum = 269;
            progressSlider.Value = 96;
            positionText.Text = "1:36";
            durationText.Text = "/ 4:29";
            ShowView(view);
        }
    }
}
