using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Skylark
{
    /// <summary>把云盘分享链接当成一个音乐库来扫描。</summary>
    public static class CloudLibrary
    {
        public const string PseudoScheme = "cloud://";

        /// <summary>列出云盘上的音频文件并组装成歌曲列表。</summary>
        public static ScanResult Scan(string urlOrToken, List<DurationEntry> cache, List<string> hidden)
        {
            ScanResult result = new ScanResult();
            if (string.IsNullOrEmpty(urlOrToken)) return result;

            Dictionary<string, DurationEntry> cached = new Dictionary<string, DurationEntry>(StringComparer.OrdinalIgnoreCase);
            if (cache != null)
            {
                foreach (DurationEntry entry in cache)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.Path)) cached[entry.Path] = entry;
                }
            }
            HashSet<string> hiddenSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (hidden != null)
            {
                foreach (string h in hidden)
                {
                    if (!string.IsNullOrEmpty(h)) hiddenSet.Add(h);
                }
            }

            List<CloudEntry> files = CloudClient.ListAllFiles(urlOrToken, 3);
            Dictionary<string, CloudEntry> lyrics = new Dictionary<string, CloudEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (CloudEntry file in files)
            {
                // 歌单＝云盘上的文件夹：空文件夹也记下来，这样新建完立刻能看到
                if (file.IsDirectory)
                {
                    string p = file.Path.Trim('/');
                    int slash = p.IndexOf('/');
                    string dir = slash > 0 ? p.Substring(0, slash) : p;
                    if (!string.IsNullOrEmpty(dir) && !result.Playlists.Contains(dir))
                        result.Playlists.Add(dir);
                    continue;
                }
                if (string.Equals(Path.GetExtension(file.Name), ".lrc", StringComparison.OrdinalIgnoreCase))
                    lyrics[ChangeExtension(file.Path, string.Empty)] = file;
            }

            string token = CloudClient.ParseToken(urlOrToken);
            foreach (CloudEntry file in files)
            {
                string ext = Path.GetExtension(file.Name).ToLowerInvariant();
                if (Array.IndexOf(LibraryScanner.Extensions, ext) < 0)
                {
                    if (Array.IndexOf(LibraryScanner.UnsupportedExtensions, ext) >= 0)
                        result.SkippedUnsupported++;
                    continue;
                }

                string pseudo = PseudoScheme + token + file.Path;
                if (hiddenSet.Contains(pseudo)) continue;

                Song song = new Song();
                song.IsCloud = true;
                song.CloudPath = file.Path;
                song.Path = pseudo;
                song.FileName = file.Name;
                song.Size = file.Size;
                song.ModifiedTicks = file.Modified.Ticks;

                string title, artist, album;
                TextUtil.ParseSongName(Path.GetFileNameWithoutExtension(file.Name), out title, out artist, out album);
                song.Title = title;
                song.Artist = artist;
                song.Album = album;
                song.Artists = TextUtil.SplitArtists(artist);
                song.Playlist = CloudClient.PlaylistOf(file.Path);

                CloudEntry lrc;
                if (lyrics.TryGetValue(ChangeExtension(file.Path, string.Empty), out lrc))
                    song.LyricPath = PseudoScheme + token + lrc.Path;

                DurationEntry entry;
                if (cached.TryGetValue(pseudo, out entry) && entry.Size == song.Size
                    && entry.ModifiedTicks == song.ModifiedTicks)
                {
                    song.Duration = entry.Duration;
                }

                DurationEntry fresh = new DurationEntry();
                fresh.Path = pseudo;
                fresh.Size = song.Size;
                fresh.ModifiedTicks = song.ModifiedTicks;
                fresh.Duration = song.Duration;
                result.Cache.Add(fresh);
                result.Songs.Add(song);
            }

            result.Songs.Sort(delegate(Song a, Song b)
            {
                return string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        private static string ChangeExtension(string cloudPath, string newExtension)
        {
            if (string.IsNullOrEmpty(newExtension))
            {
                int slash = cloudPath.LastIndexOf('/');
                int dot = cloudPath.LastIndexOf('.');
                if (dot > slash) return cloudPath.Substring(0, dot);
                return cloudPath;
            }
            return ChangeExtension(cloudPath, string.Empty) + newExtension;
        }

        /// <summary>
        /// 后台补齐歌曲信息：歌词文本与时长（时长用 Range 只取文件头）。
        /// 每个 callback 都在调用线程里执行，由调用方切回 UI 线程。
        /// </summary>
        public static void FillDetails(string urlOrToken, List<Song> songs, Action<Song> onUpdated, int limit)
        {
            int count = 0;
            foreach (Song song in songs)
            {
                if (limit > 0 && count >= limit) break;
                if (!song.IsCloud) continue;
                count++;
                try
                {
                    // 时长不再探测（界面上不显示了，还能省下每首 512KB 的流量）
                    if (song.LyricText == null && !string.IsNullOrEmpty(song.LyricPath))
                    {
                        string lrcPath = song.LyricPath.Substring((PseudoScheme + CloudClient.ParseToken(urlOrToken)).Length);
                        song.LyricText = CloudClient.GetText(urlOrToken, lrcPath);
                        if (onUpdated != null) onUpdated(song);
                    }
                }
                catch (Exception)
                {
                    // 单首失败不影响其它歌曲
                }
            }
        }
    }

    /// <summary>云盘歌曲的本地缓存。</summary>
    public static class CloudCache
    {
        public static string Directory
        {
            get
            {
                string dir = Path.Combine(AppPaths.DataDir, "cache");
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string FileFor(Song song)
        {
            string name = Sanitize(song.FileName);
            string ext = Path.GetExtension(name);
            string baseName = Path.GetFileNameWithoutExtension(name);
            if (baseName.Length > 60) baseName = baseName.Substring(0, 60);
            return Path.Combine(Directory, baseName + "." + Hash(song.CloudPath) + ext);
        }

        public static string CachedPath(Song song)
        {
            if (song == null || !song.IsCloud) return null;
            string path = FileFor(song);
            if (File.Exists(path) && (song.Size <= 0 || new FileInfo(path).Length == song.Size)) return path;
            return null;
        }

        /// <summary>转码后的 MP3 缓存路径（OGG / OPUS / APE 等格式转码后放这里）。</summary>
        public static string TranscodedPath(Song song)
        {
            return Path.ChangeExtension(FileFor(song), ".mp3");
        }
        public static bool Remove(Song song)
        {
            try
            {
                string path = FileFor(song);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        public static void Clear()
        {
            try
            {
                foreach (string file in System.IO.Directory.GetFiles(Directory))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
        }
        public static long TotalSize()
        {
            long total = 0;
            try
            {
                foreach (string file in System.IO.Directory.GetFiles(Directory))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
            return total;
        }

        public static int Count()
        {
            try
            {
                return System.IO.Directory.GetFiles(Directory).Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static string Sanitize(string name)
        {
            StringBuilder sb = new StringBuilder(name.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in name)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return sb.ToString();
        }

        /// <summary>FNV-1a，够用且稳定。</summary>
        private static string Hash(string text)
        {
            if (text == null) text = string.Empty;
            uint hash = 2166136261;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }
}
