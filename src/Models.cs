using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.Serialization;

namespace LightMusic
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
        public string LyricPath { get; set; }
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

    /// <summary>应用配置，保存在 %APPDATA%\LightMusic\settings.json。</summary>
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

        [DataMember(Name = "theme", Order = 28)]
        public string Theme { get; set; }

        [DataMember(Name = "queue", Order = 29)]
        public List<string> Queue { get; set; }

        [DataMember(Name = "queueIndex", Order = 30)]
        public int QueueIndex { get; set; }

        [DataMember(Name = "hidden", Order = 31)]
        public List<string> Hidden { get; set; }

        [DataMember(Name = "durations", Order = 32)]
        public List<DurationEntry> Durations { get; set; }

        [DataMember(Name = "mediaKeys", Order = 33)]
        public bool MediaKeys { get; set; }

        [DataMember(Name = "lastView", Order = 34)]
        public string LastView { get; set; }

        public AppSettings()
        {
            MusicDir = string.Empty;
            Volume = 0.8;
            PlayModeValue = (int)PlayMode.ListLoop;
            SortFieldValue = (int)SortField.Default;
            SortAscending = true;
            LyricFontSize = 34;
            LyricColor = "#FFFFFF";
            LyricOpacity = 1.0;
            LyricShowTranslation = true;
            LyricX = Unset;
            LyricY = Unset;
            WindowWidth = 1180;
            WindowHeight = 740;
            WindowX = Unset;
            WindowY = Unset;
            Theme = "dark";
            Queue = new List<string>();
            Hidden = new List<string>();
            Durations = new List<DurationEntry>();
            MediaKeys = true;
            LastView = "library";
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
