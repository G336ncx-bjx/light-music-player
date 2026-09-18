using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.Serialization;

namespace Skylark
{
    /// <summary>播放模式</summary>
    public enum PlayMode
    {
        Sequential = 0, // 顺序播放（播完即停）
        ListLoop = 1,   // 列表循环
        SingleLoop = 2, // 单曲循环
        Shuffle = 3     // 随机播放
    }

    /// <summary>列表排序字段</summary>
    public enum SortField
    {
        Default = 0,
        Title = 1,
        Artist = 2,
        Duration = 3
    }

    /// <summary>一首歌。带标签的媒体文件与歌词文件在同一个目录。</summary>
    public class Song : INotifyPropertyChanged
    {
        private double duration;
        private string indexText = string.Empty;
        private string queueIndexText = string.Empty;
        private bool isCurrent;

        public string Path { get; set; }
        public string FileName { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public List<string> Artists { get; set; }
        public string Album { get; set; }

        /// <summary>
        /// 所属歌单 = 云盘上这首歌所在的文件夹名（根目录下的歌为空）。
        /// 一首歌要进两个歌单，就是在两个文件夹里各放一份音频+歌词。
        /// </summary>
        public string Playlist { get; set; }
        public string LyricPath { get; set; }

        /// <summary>云盘歌曲：扫描时后台拉取的歌词文本。</summary>
        public string LyricText { get; set; }

        /// <summary>是否为云盘歌曲。</summary>
        public bool IsCloud { get; set; }

        /// <summary>云盘上的相对路径（以 / 开头）。</summary>
        public string CloudPath { get; set; }

        public long Size { get; set; }
        public long ModifiedTicks { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public Song()
        {
            Path = string.Empty;
            FileName = string.Empty;
            Title = string.Empty;
            Artist = string.Empty;
            Album = string.Empty;
            CloudPath = string.Empty;
            Artists = new List<string>();
        }

        public double Duration
        {
            get { return duration; }
            set
            {
                if (Math.Abs(duration - value) < 0.001) return;
                duration = value;
                Raise("Duration");
                Raise("DurationText");
            }
        }

        /// <summary>列表中的序号文本。</summary>
        public string IndexText
        {
            get { return indexText; }
            set
            {
                if (indexText == value) return;
                indexText = value;
                Raise("IndexText");
            }
        }

        /// <summary>播放队列中的序号文本（与音乐库序号分开保存）。</summary>
        public string QueueIndexText
        {
            get { return queueIndexText; }
            set
            {
                if (queueIndexText == value) return;
                queueIndexText = value;
                Raise("QueueIndexText");
            }
        }

        /// <summary>是否为当前播放（或队列当前项）。</summary>
        public bool IsCurrent
        {
            get { return isCurrent; }
            set
            {
                if (isCurrent == value) return;
                isCurrent = value;
                Raise("IsCurrent");
            }
        }

        public bool HasLyrics
        {
            get { return !string.IsNullOrEmpty(LyricPath); }
        }

        public string DurationText
        {
            get
            {
                if (Duration <= 0) return "--:--";
                int total = (int)Math.Round(Duration);
                return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", total / 60, total % 60);
            }
        }

        private void Raise(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }

        public string ArtistText
        {
            get { return string.IsNullOrEmpty(Artist) ? "未知歌手" : Artist; }
        }

        public string ToolTipText
        {
            get
            {
                return Title + "\n" + ArtistText + "\n" + Path;
            }
        }
    }

    /// <summary>一行歌词（含翻译行）。</summary>
    public class LyricLine
    {
        public double Time { get; set; }
        public string Text { get; set; }
        public string Translation { get; set; }

        public LyricLine()
        {
            Text = string.Empty;
            Translation = string.Empty;
        }

        public bool HasTranslation
        {
            get { return !string.IsNullOrEmpty(Translation); }
        }
    }

    /// <summary>整份歌词文档。</summary>
    public class LyricDocument
    {
        public List<LyricLine> Lines { get; set; }
        public bool Synced { get; set; }
        public bool Found { get; set; }
        public double OffsetSeconds { get; set; }
        public string Message { get; set; }

        public LyricDocument()
        {
            Lines = new List<LyricLine>();
            Message = string.Empty;
        }
    }

    /// <summary>时长缓存条目，避免每次启动都重新解析文件。</summary>
    [DataContract]
    public class DurationEntry
    {
        [DataMember(Name = "path", Order = 1)]
        public string Path { get; set; }

        [DataMember(Name = "size", Order = 2)]
        public long Size { get; set; }

        [DataMember(Name = "ticks", Order = 3)]
        public long ModifiedTicks { get; set; }

        [DataMember(Name = "dur", Order = 4)]
        public double Duration { get; set; }

        public DurationEntry()
        {
            Path = string.Empty;
        }
    }

    /// <summary>应用配置，保存在 %APPDATA%\Skylark\settings.json。</summary>
    [DataContract]
    public class AppSettings
    {
        /// <summary>未设置坐标时使用的占位值（保持配置文件是合法 JSON）。</summary>
        public const double Unset = -99999;

        [DataMember(Name = "musicDir", Order = 1)]
        public string MusicDir { get; set; }

        [DataMember(Name = "recursive", Order = 2)]
        public bool Recursive { get; set; }

        [DataMember(Name = "volume", Order = 3)]
        public double Volume { get; set; }

        [DataMember(Name = "muted", Order = 4)]
        public bool Muted { get; set; }

        [DataMember(Name = "playMode", Order = 5)]
        public int PlayModeValue { get; set; }

        [DataMember(Name = "sortField", Order = 6)]
        public int SortFieldValue { get; set; }

        [DataMember(Name = "sortAsc", Order = 7)]
        public bool SortAscending { get; set; }

        [DataMember(Name = "lastSong", Order = 8)]
        public string LastSongPath { get; set; }

        [DataMember(Name = "lastPosition", Order = 9)]
        public double LastPosition { get; set; }

        [DataMember(Name = "resumeLast", Order = 10)]
        public bool ResumeLast { get; set; }

        [DataMember(Name = "nextOnStart", Order = 11)]
        public bool AutoPlayOnStart { get; set; }

        [DataMember(Name = "desktopLyrics", Order = 12)]
        public bool DesktopLyricsOn { get; set; }

        [DataMember(Name = "lyricFontSize", Order = 13)]
        public double LyricFontSize { get; set; }

        [DataMember(Name = "lyricColor", Order = 14)]
        public string LyricColor { get; set; }

        [DataMember(Name = "lyricOpacity", Order = 15)]
        public double LyricOpacity { get; set; }

        [DataMember(Name = "lyricLocked", Order = 16)]
        public bool LyricLocked { get; set; }

        [DataMember(Name = "lyricTranslation", Order = 17)]
        public bool LyricShowTranslation { get; set; }

        [DataMember(Name = "lyricOffset", Order = 18)]
        public double LyricOffset { get; set; }

        [DataMember(Name = "lyricX", Order = 19)]
        public double LyricX { get; set; }

        [DataMember(Name = "lyricY", Order = 20)]
        public double LyricY { get; set; }

        [DataMember(Name = "windowW", Order = 21)]
        public double WindowWidth { get; set; }

        [DataMember(Name = "windowH", Order = 22)]
        public double WindowHeight { get; set; }

        [DataMember(Name = "windowX", Order = 23)]
        public double WindowX { get; set; }

        [DataMember(Name = "windowY", Order = 24)]
        public double WindowY { get; set; }

        [DataMember(Name = "maximized", Order = 25)]
        public bool WindowMaximized { get; set; }

        [DataMember(Name = "minimizeToTray", Order = 26)]
        public bool MinimizeToTray { get; set; }

        [DataMember(Name = "closeToTray", Order = 27)]
        public bool CloseToTray { get; set; }

        /// <summary>主题模式：system（跟随系统，默认）/ dark / light</summary>
        [DataMember(Name = "theme", Order = 28)]
        public string Theme { get; set; }

        [DataMember(Name = "queue", Order = 29)]
        public List<string> Queue { get; set; }

        [DataMember(Name = "queueIndex", Order = 30)]
        public int QueueIndex { get; set; }

        /// <summary>
        /// 进入随机播放前的队列顺序（歌曲路径）。关掉随机播放时按它还原，
        /// 所以在随机模式里待多久、重启几次都不会把原来的列表顺序弄丢。
        /// </summary>
        [DataMember(Name = "shuffleRestore", Order = 31)]
        public List<string> ShuffleRestore { get; set; }

        [DataMember(Name = "hidden", Order = 31)]
        public List<string> Hidden { get; set; }

        [DataMember(Name = "durations", Order = 32)]
        public List<DurationEntry> Durations { get; set; }

        [DataMember(Name = "mediaKeys", Order = 33)]
        public bool MediaKeys { get; set; }

        [DataMember(Name = "lastView", Order = 34)]
        public string LastView { get; set; }

        /// <summary>音乐来源：local（本地文件夹）/ cloud（云盘分享链接）</summary>
        [DataMember(Name = "source", Order = 35)]
        public string Source { get; set; }

        /// <summary>云盘分享链接（或 token）。</summary>
        [DataMember(Name = "cloudUrl", Order = 36)]
        public string CloudUrl { get; set; }

        /// <summary>资料库 API 令牌（可选；填了就优先使用，支持删除云端文件）。</summary>
        [DataMember(Name = "cloudToken", Order = 38)]
        public string CloudToken { get; set; }

        /// <summary>（v3.1 及以前）云端歌曲播放时是否保留本地缓存。</summary>
        [DataMember(Name = "cloudCache", Order = 37)]
        public bool CloudCacheEnabled { get; set; }

        /// <summary>
        /// 本地占用策略：0 不落地（只在线播） / 1 只留正在听的和下一首（默认） / 2 听过的都留。
        /// 老配置里只有 cloudCache 开关，由 SettingsStore.Load 负责迁移。
        /// </summary>
        [DataMember(Name = "cloudCacheMode", Order = 40)]
        public int CloudCacheModeValue { get; set; }

        /// <summary>软件内歌词页的字号（12–40，默认 16）。</summary>
        [DataMember(Name = "lyricPageSize", Order = 41)]
        public double LyricPageFontSize { get; set; }

        /// <summary>桌面歌词文字的描边（阴影）强度：0 关 / 1 弱（默认）/ 2 强。</summary>
        [DataMember(Name = "lyricShadow", Order = 43)]
        public int LyricShadowMode { get; set; }

        /// <summary>本地占用策略（对外用这个）。</summary>
        public int CloudCacheMode
        {
            get { return CloudCacheModeValue < 0 || CloudCacheModeValue > 2 ? 1 : CloudCacheModeValue; }
            set { CloudCacheModeValue = value < 0 ? 0 : (value > 2 ? 2 : value); }
        }

        /// <summary>可选的 ffmpeg 路径：用于把 OGG / OPUS / APE / WV 这类系统内核放不了的格式转成 MP3。</summary>
        [DataMember(Name = "ffmpegPath", Order = 39)]
        public string FfmpegPath { get; set; }

        public AppSettings()
        {
            MusicDir = string.Empty;
            Volume = 0.8;
            PlayModeValue = (int)PlayMode.ListLoop;
            SortFieldValue = (int)SortField.Default;
            SortAscending = true;
            LyricFontSize = 34;
            LyricPageFontSize = 16;
            LyricShadowMode = 1;
            LyricColor = "#FFFFFF";
            LyricOpacity = 1.0;
            LyricShowTranslation = true;
            LyricX = Unset;
            LyricY = Unset;
            WindowWidth = 1180;
            WindowHeight = 740;
            WindowX = Unset;
            WindowY = Unset;
            Theme = "system";
            Queue = new List<string>();
            Hidden = new List<string>();
            Durations = new List<DurationEntry>();
            MediaKeys = true;
            LastView = "library";
            Source = "cloud";
            CloudUrl = string.Empty;
            CloudToken = string.Empty;
            // 默认只留「正在听的那一首 + 下一首」：切歌几乎不用等，占的空间也很小
            CloudCacheModeValue = 1;
            CloudCacheEnabled = false;
            FfmpegPath = string.Empty;
            CloseToTray = false;
            ResumeLast = true;
        }

        public PlayMode Mode
        {
            get { return (PlayMode)PlayModeValue; }
            set { PlayModeValue = (int)value; }
        }

        public SortField Sort
        {
            get { return (SortField)SortFieldValue; }
            set { SortFieldValue = (int)value; }
        }
    }
}
