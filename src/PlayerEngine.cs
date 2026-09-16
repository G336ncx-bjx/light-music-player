using System;
using System.Diagnostics;
using System.Windows.Media;

namespace Skylark
{
    /// <summary>
    /// 基于 WPF MediaPlayer 的播放内核，附带平滑进度估算（用于歌词与进度条）。
    /// </summary>
    public class PlayerEngine
    {
        private readonly MediaPlayer player = new MediaPlayer();
        private readonly Stopwatch clock = new Stopwatch();

        private Song current;
        private double anchor;
        private double pendingSeek = -1;
        private bool pendingPlay;
        private bool playing;
        private double volume = 0.8;
        private bool muted;

        public event EventHandler TrackEnded;
        public event EventHandler Opened;
        public event EventHandler Failed;

        public PlayerEngine()
        {
            player.MediaOpened += HandleOpened;
            player.MediaEnded += HandleEnded;
            player.MediaFailed += HandleFailed;
            player.Volume = volume;
        }

        public Song Current
        {
            get { return current; }
        }

        public bool IsPlaying
        {
            get { return playing; }
        }

        public double Volume
        {
            get { return volume; }
            set
            {
                if (value < 0) value = 0;
                if (value > 1) value = 1;
                volume = value;
                player.Volume = volume;
            }
        }

        public bool IsMuted
        {
            get { return muted; }
            set
            {
                muted = value;
                player.IsMuted = muted;
            }
        }

        public double Duration
        {
            get
            {
                if (current != null && current.Duration > 0) return current.Duration;
                if (player.NaturalDuration.HasTimeSpan) return player.NaturalDuration.TimeSpan.TotalSeconds;
                return 0;
            }
        }

        public void Open(Song song, bool autoPlay, double startAt)
        {
            Open(song, song == null ? null : song.Path, autoPlay, startAt);
        }

        /// <summary>打开歌曲；云盘歌曲可以传入已缓存到本地的路径。</summary>
        public void Open(Song song, string localPath, bool autoPlay, double startAt)
        {
            if (song == null) return;
            current = song;
            playing = false;
            anchor = startAt > 0 ? startAt : 0;
            clock.Reset();
            pendingSeek = startAt > 0 ? startAt : -1;
            pendingPlay = autoPlay;
            try
            {
                player.Open(new Uri(localPath));
            }
            catch (Exception ex)
            {
                RaiseFailed("无法打开文件：" + ex.Message);
            }
        }

        public void Play()
        {
            if (current == null) return;
            try
            {
                double pos = GetPosition();
                player.Play();
                playing = true;
                anchor = pos;
                clock.Restart();
            }
            catch (Exception ex)
            {
                RaiseFailed(ex.Message);
            }
        }

        public void Pause()
        {
            if (current == null) return;
            anchor = GetPosition();
            clock.Reset();
            try
            {
                player.Pause();
            }
            catch (Exception)
            {
            }
            playing = false;
        }

        public void TogglePlay()
        {
            if (playing) Pause();
            else Play();
        }

        public void Stop()
        {
            try
            {
                player.Stop();
            }
            catch (Exception)
            {
            }
            playing = false;
            anchor = 0;
            clock.Reset();
        }

        /// <summary>平滑的当前播放位置（秒）。</summary>
        public double GetPosition()
        {
            if (current == null) return 0;
            if (!playing) return anchor;

            double actual = anchor;
            try
            {
                actual = player.Position.TotalSeconds;
            }
            catch (Exception)
            {
            }
            double estimate = anchor + clock.Elapsed.TotalSeconds;

            if (estimate - actual > 1.2 || actual - estimate > 0.25)
            {
                anchor = actual;
                clock.Restart();
                return actual;
            }
            double duration = Duration;
            if (duration > 0 && estimate > duration) estimate = duration;
            return estimate;
        }

        public void Seek(double seconds)
        {
            if (current == null) return;
            double duration = Duration;
            if (seconds < 0) seconds = 0;
            if (duration > 0 && seconds > duration) seconds = duration;

            try
            {
                player.Position = TimeSpan.FromSeconds(seconds);
            }
            catch (Exception)
            {
            }
            anchor = seconds;
            clock.Restart();
        }

        public void Close()
        {
            playing = false;
            try
            {
                player.Stop();
                player.Close();
            }
            catch (Exception)
            {
            }
        }

        private void HandleOpened(object sender, EventArgs e)
        {
            if (player.NaturalDuration.HasTimeSpan)
            {
                double seconds = player.NaturalDuration.TimeSpan.TotalSeconds;
                if (current != null && seconds > 0) current.Duration = seconds;
            }
            if (pendingSeek > 0)
            {
                try
                {
                    player.Position = TimeSpan.FromSeconds(pendingSeek);
                }
                catch (Exception)
                {
                }
                anchor = pendingSeek;
                pendingSeek = -1;
            }
            else
            {
                anchor = 0;
            }
            clock.Reset();

            bool autoPlay = pendingPlay;
            pendingPlay = false;
            if (autoPlay)
            {
                try
                {
                    player.Play();
                    playing = true;
                    clock.Restart();
                }
                catch (Exception ex)
                {
                    RaiseFailed(ex.Message);
                }
            }

            EventHandler handler = Opened;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void HandleEnded(object sender, EventArgs e)
        {
            playing = false;
            clock.Reset();
            EventHandler handler = TrackEnded;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void HandleFailed(object sender, ExceptionEventArgs e)
        {
            playing = false;
            string message = "播放失败";
            if (e != null && e.ErrorException != null) message = e.ErrorException.Message;
            RaiseFailed(message);
        }

        private void RaiseFailed(string message)
        {
            playing = false;
            EventHandler handler = Failed;
            if (handler != null) Failed(this, new PlayerErrorArgs(message));
        }
    }

    public class PlayerErrorArgs : EventArgs
    {
        public string Message { get; set; }

        public PlayerErrorArgs(string message)
        {
            Message = message;
        }
    }
}
