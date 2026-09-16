using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace LightMusic
{
    /// <summary>无界面自检：歌词解析、文件名解析、时长解析、配置读写、扫描、M3U。</summary>
    public static class SelfTest
    {
        private static int failures;
        private static readonly StringBuilder Log = new StringBuilder();

        public static int Run()
        {
            try
            {
                TestSongName();
                TestLrc();
                TestEncoding();
                TestSettingsRoundTrip();
                TestDuration();
                TestPlayback();
                TestScan();
                TestM3u();
            }
            catch (Exception ex)
            {
                failures++;
                Log.AppendLine("异常: " + ex);
            }

            string text = Log.ToString();
            Console.WriteLine(text);
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "lightmusic-selftest.log"), text, new UTF8Encoding(true));
            }
            catch (Exception)
            {
            }
            Console.WriteLine(failures == 0 ? "SELFTEST OK" : "SELFTEST FAILED: " + failures);
            return failures == 0 ? 0 : 1;
        }

        private static void Check(string name, bool ok, string detail)
        {
            if (!ok) failures++;
            Log.AppendLine((ok ? "[ok]   " : "[FAIL] ") + name + (detail == null ? "" : "  -> " + detail));
        }

        private static void TestSongName()
        {
            string title, artist, album;
            TextUtil.ParseSongName("冬眠 - 司南", out title, out artist, out album);
            Check("文件名解析：单歌手", title == "冬眠" && artist == "司南", title + " / " + artist);

            TextUtil.ParseSongName("City of Stars - Ryan Gosling、Emma Stone", out title, out artist, out album);
            List<string> artists = TextUtil.SplitArtists(artist);
            Check("文件名解析：多歌手", title == "City of Stars" && artists.Count == 2,
                title + " / " + artists.Count + " 位歌手");

            TextUtil.ParseSongName("只有歌名", out title, out artist, out album);
            Check("文件名解析：无歌手", title == "只有歌名" && artist.Length == 0, title + " / 空歌手");
        }

        private static void TestLrc()
        {
            string text =
                "[ti:测试]\n[ar:歌手]\n[offset:+500]\n" +
                "[00:01.00]第一句\n" +
                "[00:05.50][01:05.50]重复段落\n" +
                "[00:10.20]原文\n[00:10.20]Translation\n" +
                "无时间标签的一行\n";
            LyricDocument doc = LrcParser.Parse(text);
            Check("歌词：解析行数", doc.Lines.Count == 5, doc.Lines.Count + " 行");
            Check("歌词：已同步", doc.Synced, "synced=" + doc.Synced);
            LyricLine first = null;
            foreach (LyricLine line in doc.Lines)
            {
                if (line.Text == "第一句") first = line;
            }
            Check("歌词：offset 生效", first != null && Math.Abs(first.Time - 1.5) < 0.001,
                first == null ? "未找到歌词行" : first.Time.ToString("0.000"));
            bool hasTranslation = false;
            string detail = "";
            foreach (LyricLine line in doc.Lines)
            {
                if (line.Translation == "Translation") hasTranslation = true;
                if (line.Text == "原文" || line.Text == "Translation")
                    detail += "[" + line.Text + "|" + line.Translation + "@" + line.Time.ToString("0.00") + "]";
            }
            Check("歌词：翻译行合并", hasTranslation, detail);

            LyricDocument plain = LrcParser.Parse("第一行\n第二行\n");
            Check("歌词：纯文本歌词", !plain.Synced && plain.Lines.Count == 2, plain.Lines.Count + " 行");
        }

        private static void TestEncoding()
        {
            string file = Path.Combine(Path.GetTempPath(), "lightmusic-gbk.lrc");
            string content = "[00:01.00]中文歌词测试";
            try
            {
                File.WriteAllBytes(file, Encoding.GetEncoding(936).GetBytes(content));
                string read = TextUtil.ReadAllTextSmart(file);
                Check("编码：GBK 歌词读取", read.Contains("中文歌词测试"), read.Trim());
            }
            catch (Exception ex)
            {
                Check("编码：GBK 歌词读取", false, ex.Message);
            }
        }

        private static void TestSettingsRoundTrip()
        {
            AppSettings settings = new AppSettings();
            settings.Volume = 0.42;
            settings.MusicDir = "D:\\音乐\\music";
            settings.Queue = new List<string>(new string[] { "a.mp3", "b.mp3" });
            settings.Mode = PlayMode.Shuffle;
            settings.Durations.Add(new DurationEntry());

            string file = Path.Combine(Path.GetTempPath(), "lightmusic-settings.json");
            using (FileStream fs = new FileStream(file, FileMode.Create))
            {
                System.Runtime.Serialization.Json.DataContractJsonSerializer ser =
                    new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AppSettings));
                ser.WriteObject(fs, settings);
            }
            AppSettings loaded;
            using (FileStream fs = new FileStream(file, FileMode.Open))
            {
                System.Runtime.Serialization.Json.DataContractJsonSerializer ser =
                    new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AppSettings));
                loaded = (AppSettings)ser.ReadObject(fs);
            }
            Check("配置：JSON 往返", Math.Abs(loaded.Volume - 0.42) < 0.0001
                && loaded.Mode == PlayMode.Shuffle && loaded.Queue.Count == 2, "音量 " + loaded.Volume);
        }

        private static void TestDuration()
        {
            string dir = "D:\\Lai Siyu\\music";
            string[] files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.mp3") : new string[0];
            if (files.Length == 0)
            {
                Check("时长：MP3 解析", true, "跳过（没有测试文件）");
                return;
            }
            double parsed = DurationReader.Read(files[0]);
            Check("时长：MP3 解析", parsed > 30 && parsed < 900, Path.GetFileName(files[0]) + " = " + parsed.ToString("0.0") + " s");

            double actual = ProbeDurationWithMediaPlayer(files[0]);
            if (actual > 0)
            {
                double diff = Math.Abs(actual - parsed);
                Check("时长：与解码器一致", diff < 1.5, "解析 " + parsed.ToString("0.0") + " s / 解码器 " + actual.ToString("0.0") + " s");
            }
            else
            {
                Check("时长：与解码器一致", true, "跳过（解码器未返回时长）");
            }
        }

        private static double ProbeDurationWithMediaPlayer(string path)
        {
            MediaPlayer player = new MediaPlayer();
            double result = 0;
            bool done = false;
            player.MediaOpened += delegate
            {
                if (player.NaturalDuration.HasTimeSpan) result = player.NaturalDuration.TimeSpan.TotalSeconds;
                done = true;
            };
            player.MediaFailed += delegate { done = true; };
            try
            {
                player.Open(new Uri(path));
            }
            catch (Exception)
            {
                return 0;
            }

            DispatcherFrame frame = new DispatcherFrame();
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(150);
            int ticks = 0;
            timer.Tick += delegate
            {
                ticks++;
                if (done || ticks > 40)
                {
                    timer.Stop();
                    frame.Continue = false;
                }
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
            player.Close();
            return result;
        }

        /// <summary>真实调用播放内核：打开文件、播放、跳转。</summary>
        private static void TestPlayback()
        {
            string dir = "D:\\Lai Siyu\\music";
            string[] files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.mp3") : new string[0];
            if (files.Length == 0)
            {
                Check("播放：打开并播放", true, "跳过（没有测试文件）");
                return;
            }

            PlayerEngine engine = new PlayerEngine();
            string error = null;
            int opened = 0;
            engine.Opened += delegate { opened++; };
            engine.Failed += delegate(object sender, EventArgs args)
            {
                PlayerErrorArgs info = args as PlayerErrorArgs;
                error = info == null ? "未知错误" : info.Message;
            };

            Song song = new Song();
            song.Path = files[0];
            song.Title = Path.GetFileNameWithoutExtension(files[0]);
            song.Duration = DurationReader.Read(files[0]);
            engine.Volume = 0;
            engine.Open(song, true, 0);
            Pump(2.5);

            double position = engine.GetPosition();
            Check("播放：打开并开始播放", opened > 0 && engine.IsPlaying && error == null,
                "opened=" + opened + " playing=" + engine.IsPlaying + " pos=" + position.ToString("0.00") + "s"
                + (error == null ? "" : " err=" + error));
            Check("播放：进度推进", position > 0.5, position.ToString("0.00") + " s");

            engine.Seek(30);
            Pump(0.6);
            double afterSeek = engine.GetPosition();
            Check("播放：跳转", Math.Abs(afterSeek - 30) < 4, afterSeek.ToString("0.0") + " s");

            engine.Pause();
            Pump(0.3);
            double paused = engine.GetPosition();
            Pump(0.6);
            double stillPaused = engine.GetPosition();
            Check("播放：暂停后进度不动", Math.Abs(paused - stillPaused) < 0.2,
                paused.ToString("0.00") + " / " + stillPaused.ToString("0.00"));
            engine.Close();
        }

        /// <summary>推动 WPF 消息循环若干秒，让 MediaPlayer 的异步事件得以送达。</summary>
        private static void Pump(double seconds)
        {
            DispatcherFrame frame = new DispatcherFrame();
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(seconds);
            timer.Tick += delegate
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static void TestScan()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lightmusic-scan");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "sub"));

            string music = "D:\\Lai Siyu\\music";
            string[] sources = Directory.Exists(music) ? Directory.GetFiles(music, "*.mp3") : new string[0];
            if (sources.Length > 0)
            {
                File.Copy(sources[0], Path.Combine(dir, "测试歌曲 - 甲、乙.mp3"), true);
                File.Copy(sources[0], Path.Combine(dir, "sub", "子目录歌曲 - 丙.mp3"), true);
                File.WriteAllText(Path.Combine(dir, "测试歌曲 - 甲、乙.lrc"),
                    "[00:01.00]测试歌词", new UTF8Encoding(true));
            }
            else
            {
                File.WriteAllText(Path.Combine(dir, "空文件 - 测试.mp3"), "");
            }

            ScanResult flat = LibraryScanner.Scan(dir, false, null, null);
            ScanResult deep = LibraryScanner.Scan(dir, true, null, null);
            Check("扫描：非递归", flat.Songs.Count == 1, flat.Songs.Count + " 首");
            Check("扫描：递归子目录", deep.Songs.Count == 2, deep.Songs.Count + " 首");
            if (flat.Songs.Count > 0)
            {
                Check("扫描：关联歌词", flat.Songs[0].HasLyrics, flat.Songs[0].LyricPath);
                Check("扫描：歌手拆分", flat.Songs[0].Artists.Count == 2, flat.Songs[0].Artist);
            }

            List<DurationEntry> cache = flat.Cache;
            ScanResult again = LibraryScanner.Scan(dir, false, cache, null);
            Check("扫描：缓存复用", again.Songs.Count == 1 && Math.Abs(again.Songs[0].Duration - flat.Songs[0].Duration) < 0.001,
                "缓存 " + again.Cache.Count + " 条");

            List<string> hidden = new List<string>();
            hidden.Add(Path.Combine(dir, "测试歌曲 - 甲、乙.mp3"));
            ScanResult hiddenScan = LibraryScanner.Scan(dir, false, null, hidden);
            Check("扫描：忽略已移除歌曲", hiddenScan.Songs.Count == 0, hiddenScan.Songs.Count + " 首");
        }

        private static void TestM3u()
        {
            string file = Path.Combine(Path.GetTempPath(), "lightmusic-playlist.m3u");
            string audio = Path.Combine(Path.GetTempPath(), "lightmusic-m3u-test.mp3");
            File.WriteAllText(audio, "x");
            List<Song> songs = new List<Song>();
            Song song = new Song();
            song.Path = audio;
            song.Title = "测试";
            song.Artist = "歌手";
            songs.Add(song);
            M3u.Save(file, songs);
            List<string> loaded = M3u.Load(file);
            Check("播放列表：M3U 往返", loaded.Count == 1 && loaded[0] == audio, loaded.Count + " 项");
        }
    }
}
