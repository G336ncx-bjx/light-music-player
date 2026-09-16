using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Skylark
{
    public static class AppPaths
    {
        public static string ExeDir
        {
            get
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                if (dir.EndsWith("\\") || dir.EndsWith("/")) dir = dir.Substring(0, dir.Length - 1);
                return dir;
            }
        }

        private static string dataDir;

        /// <summary>
        /// 配置目录：优先 %APPDATA%\Skylark，若不可写（例如受限环境）则退回到 exe 目录下的 data。
        /// 第一次以新名字启动时，会把旧版「LightMusic」目录整体搬过来（配置 + 缓存都不丢）。
        /// </summary>
        public static string DataDir
        {
            get
            {
                if (dataDir != null) return dataDir;

                string preferred = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Skylark");
                MigrateLegacyFolder(preferred);
                if (TryPrepare(preferred))
                {
                    dataDir = preferred;
                    return dataDir;
                }

                string fallback = Path.Combine(ExeDir, "data");
                if (TryPrepare(fallback))
                {
                    dataDir = fallback;
                    return dataDir;
                }

                dataDir = ExeDir;
                return dataDir;
            }
        }

        /// <summary>旧版本（叫 LightMusic 时）的数据目录整体搬到新目录，配置与缓存都不丢。</summary>
        private static void MigrateLegacyFolder(string preferred)
        {
            try
            {
                string legacy = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LightMusic");
                if (Directory.Exists(preferred)) return;
                if (!Directory.Exists(legacy)) return;

                Directory.CreateDirectory(preferred);

                // 配置一定要带过来（令牌、播放列表、设置都在里面）
                string oldSettings = Path.Combine(legacy, "settings.json");
                if (File.Exists(oldSettings))
                {
                    File.Copy(oldSettings, Path.Combine(preferred, "settings.json"), true);
                }

                // 缓存尽量搬过去；搬不动就算了（需要时会重新下载）
                try
                {
                    string oldCache = Path.Combine(legacy, "cache");
                    string newCache = Path.Combine(preferred, "cache");
                    if (Directory.Exists(oldCache) && !Directory.Exists(newCache))
                        Directory.Move(oldCache, newCache);
                }
                catch (Exception)
                {
                }

                // 旧目录尽力清理，删不掉就留着（不影响使用）
                try
                {
                    Directory.Delete(legacy, true);
                }
                catch (Exception)
                {
                }
            }
            catch (Exception)
            {
                // 搬不动就算了，后面会退回到新建目录
            }
        }

        private static bool TryPrepare(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(DataDir, "settings.json"); }
        }

        /// <summary>
        /// 首次运行时猜测音乐目录：
        /// 1) exe 同级 music  2) 逐级向上查找 music 目录（最多 3 层）  3) 系统“音乐”目录
        /// </summary>
        public static string DetectMusicDir()
        {
            string dir = Path.Combine(ExeDir, "music");
            if (Directory.Exists(dir)) return dir;

            try
            {
                DirectoryInfo parent = Directory.GetParent(ExeDir);
                int level = 0;
                while (parent != null && level < 3)
                {
                    dir = Path.Combine(parent.FullName, "music");
                    if (Directory.Exists(dir)) return dir;
                    parent = parent.Parent;
                    level++;
                }
            }
            catch (Exception)
            {
            }

            string music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (!string.IsNullOrEmpty(music) && Directory.Exists(music)) return music;
            return Path.Combine(ExeDir, "music");
        }
    }

    public static class TextUtil
    {
        public static string FormatTime(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
            int total = (int)Math.Floor(seconds);
            int h = total / 3600;
            int m = (total % 3600) / 60;
            int s = total % 60;
            if (h > 0) return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", h, m, s);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", m, s);
        }

        /// <summary>按 BOM / UTF-8 / GBK 顺序猜测文本编码读取。</summary>
        public static string ReadAllTextSmart(string path)
        {
            return DecodeBytes(File.ReadAllBytes(path));
        }

        /// <summary>按 BOM / UTF-8 / GBK 顺序猜测编码解码字节数组。</summary>
        public static string DecodeBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            try
            {
                UTF8Encoding strict = new UTF8Encoding(false, true);
                return strict.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                try
                {
                    return Encoding.GetEncoding(936).GetString(bytes);
                }
                catch (Exception)
                {
                    return Encoding.Default.GetString(bytes);
                }
            }
        }

        /// <summary>把 "歌名 - 歌手" 拆成标题与歌手。</summary>
        public static void ParseSongName(string fileNameNoExt, out string title, out string artist, out string album)
        {
            title = fileNameNoExt.Trim();
            artist = string.Empty;
            album = string.Empty;

            string[] seps = new string[] { " - ", " – ", " — ", "-", "–", "—" };
            string best = null;
            foreach (string sep in seps)
            {
                int idx = title.IndexOf(sep, StringComparison.Ordinal);
                if (idx > 0)
                {
                    best = sep;
                    break;
                }
            }
            if (best == null) return;

            string[] parts = title.Split(new string[] { best }, StringSplitOptions.None);
            if (parts.Length < 2) return;

            title = parts[0].Trim();
            artist = parts[1].Trim();
            if (parts.Length > 2) album = parts[2].Trim();
            if (title.Length == 0) title = fileNameNoExt.Trim();
        }

        /// <summary>"歌手A、歌手B" → 列表</summary>
        public static List<string> SplitArtists(string artist)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(artist)) return list;
            string[] parts = artist.Split(new char[] { '、', ',', '，', '&', '＆', '/', ';', '；', '+' });
            foreach (string p in parts)
            {
                string t = p.Trim();
                if (t.Length > 0 && !list.Contains(t)) list.Add(t);
            }
            return list;
        }
    }

    /// <summary>LRC 歌词解析。</summary>
    public static class LrcParser
    {
        public static LyricDocument Load(string path)
        {
            LyricDocument doc = new LyricDocument();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                doc.Found = false;
                doc.Message = "未找到歌词文件";
                return doc;
            }
            try
            {
                string text = TextUtil.ReadAllTextSmart(path);
                doc = Parse(text);
                doc.Found = doc.Lines.Count > 0;
                if (!doc.Found) doc.Message = "歌词文件为空";
                return doc;
            }
            catch (Exception ex)
            {
                LyricDocument bad = new LyricDocument();
                bad.Found = false;
                bad.Message = "歌词读取失败：" + ex.Message;
                return bad;
            }
        }

        public static LyricDocument Parse(string text)
        {
            LyricDocument doc = new LyricDocument();
            if (text == null) return doc;

            string[] rawLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            List<LyricLine> lines = new List<LyricLine>();
            bool synced = false;
            double offset = 0;

            for (int i = 0; i < rawLines.Length; i++)
            {
                string raw = rawLines[i].Trim();
                if (raw.Length == 0) continue;

                List<double> times = new List<double>();
                int pos = 0;
                while (pos < raw.Length && raw[pos] == '[')
                {
                    int end = raw.IndexOf(']', pos);
                    if (end < 0) break;
                    string tag = raw.Substring(pos + 1, end - pos - 1);
                    double t;
                    if (TryParseTimeTag(tag, out t))
                    {
                        times.Add(t);
                        synced = true;
                    }
                    else if (tag.StartsWith("offset:", StringComparison.OrdinalIgnoreCase))
                    {
                        double off;
                        if (double.TryParse(tag.Substring(7).Trim(), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out off))
                            offset = off / 1000.0;
                    }
                    pos = end + 1;
                }

                string content = raw.Substring(pos).Trim();
                if (content.Length == 0) continue;

                if (times.Count == 0)
                {
                    // 无时间标签的纯文本歌词，作为静态歌词展示
                    LyricLine plain = new LyricLine();
                    plain.Time = -1;
                    plain.Text = content;
                    lines.Add(plain);
                    continue;
                }

                for (int k = 0; k < times.Count; k++)
                {
                    LyricLine line = new LyricLine();
                    line.Time = times[k] + offset;
                    line.Text = content;
                    lines.Add(line);
                }
            }

            if (synced)
            {
                // 稳定排序：同一时间戳的行保持原有先后顺序，第二行才会被识别为翻译
                lines = new List<LyricLine>(lines.OrderBy(delegate(LyricLine line) { return line.Time; }));
                // 同一时间戳的第二行作为翻译
                List<LyricLine> merged = new List<LyricLine>();
                for (int i = 0; i < lines.Count; i++)
                {
                    LyricLine cur = lines[i];
                    if (merged.Count > 0)
                    {
                        LyricLine prev = merged[merged.Count - 1];
                        if (Math.Abs(prev.Time - cur.Time) < 0.05 && string.IsNullOrEmpty(prev.Translation))
                        {
                            prev.Translation = cur.Text;
                            continue;
                        }
                    }
                    merged.Add(cur);
                }
                lines = merged;
            }

            doc.Lines = lines;
            doc.Synced = synced;
            doc.OffsetSeconds = offset;
            return doc;
        }

        private static bool TryParseTimeTag(string tag, out double seconds)
        {
            seconds = 0;
            int colon = tag.IndexOf(':');
            if (colon <= 0) return false;

            string mm = tag.Substring(0, colon).Trim();
            string rest = tag.Substring(colon + 1).Trim();
            if (mm.Length == 0 || rest.Length == 0) return false;

            long minutes;
            if (!long.TryParse(mm, NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes)) return false;

            string ss = rest;
            string frac = string.Empty;
            int dot = rest.IndexOfAny(new char[] { '.', ':' });
            if (dot >= 0)
            {
                ss = rest.Substring(0, dot);
                frac = rest.Substring(dot + 1);
            }

            double sec;
            if (!double.TryParse(ss, NumberStyles.Float, CultureInfo.InvariantCulture, out sec)) return false;

            double fracValue = 0;
            if (frac.Length > 0 && frac.Length <= 3)
            {
                double f;
                if (double.TryParse(frac, NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                    fracValue = f / Math.Pow(10, frac.Length);
            }

            seconds = minutes * 60.0 + sec + fracValue;
            return true;
        }
    }

    /// <summary>
    /// 轻量级时长解析：直接读取媒体文件头，不依赖解码器，
    /// 支持 MP3（含 Xing/VBRI 帧数）、FLAC、WAV、M4A/MP4。
    /// </summary>
    public static class DurationReader
    {
        private static readonly int[] BitrateV1L1 = { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448, 0 };
        private static readonly int[] BitrateV1L2 = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 0 };
        private static readonly int[] BitrateV1L3 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
        private static readonly int[] BitrateV2L1 = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256, 0 };
        private static readonly int[] BitrateV2L23 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 };
        private static readonly int[] SampleRateV1 = { 44100, 48000, 32000, 0 };
        private static readonly int[] SampleRateV2 = { 22050, 24000, 16000, 0 };
        private static readonly int[] SampleRateV25 = { 11025, 12000, 8000, 0 };

        public static double Read(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    return Read(fs, fs.Length, ext);
                }
            }
            catch (Exception)
            {
            }
            return 0;
        }

        /// <summary>从已读取的文件头（也可以是 HTTP Range 拿到的前若干 KB）解析时长。</summary>
        public static double ReadBytes(byte[] head, long totalLength, string ext)
        {
            if (head == null || head.Length == 0) return 0;
            try
            {
                using (MemoryStream fs = new MemoryStream(head, false))
                {
                    return Read(fs, totalLength, ext.ToLowerInvariant());
                }
            }
            catch (Exception)
            {
            }
            return 0;
        }

        public static double Read(Stream fs, long totalLength, string ext)
        {
            try
            {
                if (ext == ".mp3") return ReadMp3(fs, totalLength);
                if (ext == ".flac") return ReadFlac(fs, totalLength);
                if (ext == ".wav") return ReadWav(fs, totalLength);
                if (ext == ".m4a" || ext == ".mp4" || ext == ".aac") return ReadMp4(fs, totalLength);
            }
            catch (Exception)
            {
            }
            return 0;
        }

        private static double ReadMp3(Stream fs, long totalLength)
        {
            {
                long len = totalLength;
                byte[] head = new byte[10];
                if (fs.Read(head, 0, 10) < 10) return 0;
                long start = 0;
                if (head[0] == 'I' && head[1] == 'D' && head[2] == '3')
                {
                    int b6 = head[6] & 0x7F;
                    int b7 = head[7] & 0x7F;
                    int b8 = head[8] & 0x7F;
                    int b9 = head[9] & 0x7F;
                    long size = ((long)b6 << 21) | ((long)b7 << 14) | ((long)b8 << 7) | (long)(uint)b9;
                    start = size + 10;
                    if ((head[5] & 0x10) != 0) start += 10;
                }
                if (start >= len - 4) return 0;

                fs.Position = start;
                byte[] buf = new byte[64 * 1024];
                int read = fs.Read(buf, 0, buf.Length);
                int found = -1;
                for (int i = 0; i + 4 <= read; i++)
                {
                    if (buf[i] != 0xFF || (buf[i + 1] & 0xE0) != 0xE0) continue;
                    int version = (buf[i + 1] >> 3) & 0x03;
                    int layer = (buf[i + 1] >> 1) & 0x03;
                    int bitrateIdx = (buf[i + 2] >> 4) & 0x0F;
                    int rateIdx = (buf[i + 2] >> 2) & 0x03;
                    if (version == 1 || layer == 0 || bitrateIdx == 0 || bitrateIdx == 15 || rateIdx == 3) continue;
                    found = i;
                    break;
                }
                if (found < 0) return 0;

                int v = (buf[found + 1] >> 3) & 0x03;   // 3 = MPEG1, 2 = MPEG2, 0 = 2.5
                int lay = (buf[found + 1] >> 1) & 0x03; // 3 = L1, 2 = L2, 1 = L3
                int brIdx = (buf[found + 2] >> 4) & 0x0F;
                int srIdx = (buf[found + 2] >> 2) & 0x03;
                int padding = (buf[found + 2] >> 1) & 0x01;
                int channelMode = (buf[found + 3] >> 6) & 0x03;

                int bitrate;
                int sampleRate;
                int samplesPerFrame;
                if (v == 3)
                {
                    bitrate = lay == 3 ? BitrateV1L1[brIdx] : (lay == 2 ? BitrateV1L2[brIdx] : BitrateV1L3[brIdx]);
                    sampleRate = SampleRateV1[srIdx];
                    samplesPerFrame = lay == 3 ? 384 : 1152;
                }
                else
                {
                    bitrate = lay == 3 ? BitrateV2L1[brIdx] : BitrateV2L23[brIdx];
                    sampleRate = v == 2 ? SampleRateV2[srIdx] : SampleRateV25[srIdx];
                    samplesPerFrame = lay == 3 ? 384 : 576;
                }
                if (bitrate <= 0 || sampleRate <= 0) return 0;

                int frameLen;
                if (lay == 3) frameLen = (12 * bitrate * 1000 / sampleRate + padding) * 4;
                else if (lay == 2) frameLen = 144 * bitrate * 1000 / sampleRate + padding;
                else frameLen = (v == 3 ? 144 : 72) * bitrate * 1000 / sampleRate + padding;
                if (frameLen <= 4) return 0;

                long frameCount = -1;
                int xingOff = found + (v == 3 ? (channelMode == 3 ? 17 : 32) : (channelMode == 3 ? 9 : 17));
                if (xingOff + 12 <= read)
                {
                    bool xing = (buf[xingOff] == 'X' && buf[xingOff + 1] == 'i' && buf[xingOff + 2] == 'n' && buf[xingOff + 3] == 'g')
                             || (buf[xingOff] == 'I' && buf[xingOff + 1] == 'n' && buf[xingOff + 2] == 'f' && buf[xingOff + 3] == 'o');
                    if (xing)
                    {
                        int flags = BE32(buf, xingOff + 4);
                        if ((flags & 0x01) != 0) frameCount = BE32(buf, xingOff + 8);
                    }
                }
                if (frameCount < 0)
                {
                    foreach (int vbri in new int[] { found + 36, found + 32 })
                    {
                        if (vbri + 18 > read) continue;
                        if (buf[vbri] == 'V' && buf[vbri + 1] == 'B' && buf[vbri + 2] == 'R' && buf[vbri + 3] == 'I')
                        {
                            frameCount = BE32(buf, vbri + 14);
                            break;
                        }
                    }
                }

                if (frameCount > 0)
                    return frameCount * (double)samplesPerFrame / sampleRate;

                long audioStart = start + found;
                return (len - audioStart) * 8.0 / (bitrate * 1000.0);
            }
        }

        private static double ReadFlac(Stream fs, long totalLength)
        {
            {
                byte[] buf = new byte[42];
                if (fs.Read(buf, 0, buf.Length) < 42) return 0;
                if (buf[0] != 'f' || buf[1] != 'L' || buf[2] != 'a' || buf[3] != 'C') return 0;
                int type = buf[4] & 0x7F;
                if (type != 0) return 0;
                int s = 8;
                int sampleRate = (buf[s + 10] << 12) | (buf[s + 11] << 4) | (buf[s + 12] >> 4);
                long totalSamples = ((long)(buf[s + 13] & 0x0F) << 32)
                                  | ((long)buf[s + 14] << 24) | ((long)buf[s + 15] << 16)
                                  | ((long)buf[s + 16] << 8) | (long)buf[s + 17];
                if (sampleRate <= 0 || totalSamples <= 0) return 0;
                return totalSamples / (double)sampleRate;
            }
        }

        private static double ReadWav(Stream fs, long totalLength)
        {
            {
                BinaryReader br = new BinaryReader(fs);
                if (new string(br.ReadChars(4)) != "RIFF") return 0;
                br.ReadInt32();
                if (new string(br.ReadChars(4)) != "WAVE") return 0;
                long byteRate = 0;
                long dataSize = 0;
                while (fs.Position + 8 <= totalLength)
                {
                    string id = new string(br.ReadChars(4));
                    int size = br.ReadInt32();
                    long next = fs.Position + size + (size % 2);
                    if (id == "fmt ")
                    {
                        br.ReadInt16();
                        br.ReadInt16();
                        br.ReadInt32();
                        byteRate = br.ReadInt32();
                    }
                    else if (id == "data")
                    {
                        dataSize = size;
                    }
                    if (next <= fs.Position || next > totalLength) break;
                    fs.Position = next;
                }
                if (byteRate <= 0 || dataSize <= 0) return 0;
                return dataSize / (double)byteRate;
            }
        }

        private static double ReadMp4(Stream fs, long totalLength)
        {
            return ReadMp4Atoms(fs, 0, totalLength, 0);
        }

        private static double ReadMp4Atoms(Stream fs, long start, long end, int depth)
        {
            if (depth > 4) return 0;
            long pos = start;
            byte[] header = new byte[8];
            while (pos + 8 <= end)
            {
                fs.Position = pos;
                if (fs.Read(header, 0, 8) < 8) break;
                long size = BE32(header, 0);
                string type = Encoding.ASCII.GetString(header, 4, 4);
                long headerSize = 8;
                if (size == 1)
                {
                    byte[] big = new byte[8];
                    if (fs.Read(big, 0, 8) < 8) break;
                    size = ((long)BE32(big, 0) << 32) | (long)(uint)BE32(big, 4);
                    headerSize = 16;
                }
                else if (size == 0)
                {
                    size = end - pos;
                }
                if (size < headerSize) break;

                long contentStart = pos + headerSize;
                if (type == "mvhd")
                {
                    byte[] buf = new byte[32];
                    fs.Position = contentStart;
                    if (fs.Read(buf, 0, buf.Length) < buf.Length) return 0;
                    int version = buf[0];
                    long timescale;
                    long duration;
                    if (version == 1)
                    {
                        timescale = BE32(buf, 20);
                        duration = ((long)BE32(buf, 24) << 32) | (uint)BE32(buf, 28);
                    }
                    else
                    {
                        timescale = BE32(buf, 12);
                        duration = BE32(buf, 16);
                    }
                    if (timescale <= 0) return 0;
                    return duration / (double)timescale;
                }
                if (type == "moov" || type == "trak" || type == "mdia")
                {
                    double d = ReadMp4Atoms(fs, contentStart, pos + size, depth + 1);
                    if (d > 0) return d;
                }
                pos += size;
            }
            return 0;
        }

        private static int BE32(byte[] b, int i)
        {
            return (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
        }
    }

    public class ScanResult
    {
        public List<Song> Songs;
        public List<DurationEntry> Cache;
        /// <summary>被跳过的、不再支持的音频文件数量（例如 FLAC）。</summary>
        public int SkippedUnsupported;

        public ScanResult()
        {
            Songs = new List<Song>();
            Cache = new List<DurationEntry>();
        }
    }

    public static class LibraryScanner
    {
        public static readonly string[] Extensions =
            new string[] { ".mp3", ".wav", ".m4a", ".aac", ".wma" };

        /// <summary>能识别但不再支持的音频格式（扫描时跳过，并给用户提示）。</summary>
        public static readonly string[] UnsupportedExtensions =
            new string[] { ".flac", ".ogg", ".opus", ".ape", ".wv", ".aif", ".aiff" };

        public static ScanResult Scan(string dir, bool recursive, List<DurationEntry> cache, List<string> hidden)
        {
            ScanResult result = new ScanResult();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return result;

            Dictionary<string, DurationEntry> cached = new Dictionary<string, DurationEntry>(StringComparer.OrdinalIgnoreCase);
            if (cache != null)
            {
                foreach (DurationEntry e in cache)
                {
                    if (e != null && !string.IsNullOrEmpty(e.Path)) cached[e.Path] = e;
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

            List<string> files = new List<string>();
            try
            {
                SearchOption opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (string f in Directory.GetFiles(dir, "*.*", opt))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(Extensions, ext) < 0)
                    {
                        if (Array.IndexOf(UnsupportedExtensions, ext) >= 0) result.SkippedUnsupported++;
                        continue;
                    }
                    if (hiddenSet.Contains(f)) continue;
                    files.Add(f);
                }
            }
            catch (Exception)
            {
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                FileInfo info = new FileInfo(file);
                Song song = new Song();
                song.Path = file;
                song.FileName = info.Name;
                song.Size = info.Length;
                song.ModifiedTicks = info.LastWriteTimeUtc.Ticks;

                string title, artist, album;
                TextUtil.ParseSongName(Path.GetFileNameWithoutExtension(file), out title, out artist, out album);
                song.Title = title;
                song.Artist = artist;
                song.Album = album;
                song.Artists = TextUtil.SplitArtists(artist);

                string lrc = Path.ChangeExtension(file, ".lrc");
                if (File.Exists(lrc)) song.LyricPath = lrc;

                DurationEntry entry;
                if (cached.TryGetValue(file, out entry) && entry.Size == song.Size && entry.ModifiedTicks == song.ModifiedTicks)
                {
                    song.Duration = entry.Duration;
                }
                else
                {
                    song.Duration = DurationReader.Read(file);
                }

                DurationEntry fresh = new DurationEntry();
                fresh.Path = file;
                fresh.Size = song.Size;
                fresh.ModifiedTicks = song.ModifiedTicks;
                fresh.Duration = song.Duration;
                result.Cache.Add(fresh);

                result.Songs.Add(song);
            }

            return result;
        }
    }

    public static class SettingsStore
    {
        public static AppSettings Load()
        {
            try
            {
                string file = AppPaths.SettingsFile;
                if (!File.Exists(file)) return new AppSettings();
                using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(AppSettings));
                    AppSettings s = (AppSettings)ser.ReadObject(fs);
                    if (s == null) return new AppSettings();
                    if (s.Queue == null) s.Queue = new List<string>();
                    if (s.Hidden == null) s.Hidden = new List<string>();
                    if (s.Durations == null) s.Durations = new List<DurationEntry>();
                    if (string.IsNullOrEmpty(s.Theme)) s.Theme = "dark";
                    if (s.LyricFontSize < 16 || s.LyricFontSize > 96) s.LyricFontSize = 34;
                    // v3.2 起用 cloudCacheMode（1 只留正在听的和下一首 / 2 听过的歌都留）代替老的 cloudCache 开关
                    if (s.CloudCacheEnabled) s.CloudCacheModeValue = 2;
                    else if (s.CloudCacheModeValue != 2) s.CloudCacheModeValue = 1;
                    return s;
                }
            }
            catch (Exception)
            {
                return new AppSettings();
            }
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                // 老的 cloudCache 开关继续同步写，保证旧版本读到的新配置也说得通
                settings.CloudCacheEnabled = settings.CloudCacheMode == 2;
                string file = AppPaths.SettingsFile;
                string tmp = file + ".tmp";
                using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(AppSettings));
                    ser.WriteObject(fs, settings);
                }
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file);
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>M3U 播放列表读写。</summary>
    public static class M3u
    {
        public static void Save(string file, List<Song> songs)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("#EXTM3U");
            foreach (Song s in songs)
            {
                sb.AppendLine("#EXTINF:-1," + s.ArtistText + " - " + s.Title);
                sb.AppendLine(s.Path);
            }
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
        }

        public static List<string> Load(string file)
        {
            List<string> paths = new List<string>();
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string full = line;
                if (!Path.IsPathRooted(full))
                {
                    full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file), line));
                }
                if (File.Exists(full)) paths.Add(full);
            }
            return paths;
        }
    }
}
