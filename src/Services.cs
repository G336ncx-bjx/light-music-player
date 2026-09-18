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
        /// 测试 / 截图模式用：把数据目录指到临时目录，绝不碰用户真实配置。
        /// （以前自检和截图模式会读写 %APPDATA%\Skylark\settings.json，
        /// 退出时还会把「当前播放列表」写回去，等于把用户的列表覆盖掉。）
        /// </summary>
        public static void UseIsolatedDataDir(string tag)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "skylark-" + tag);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                dataDir = dir;
            }
            catch (Exception)
            {
                // 建不出来就退回默认目录
            }
        }

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
            string titleTag = null;
            string artistTag = null;

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
                    else if (tag.StartsWith("ti:", StringComparison.OrdinalIgnoreCase))
                    {
                        titleTag = tag.Substring(3).Trim();
                    }
                    else if (tag.StartsWith("ar:", StringComparison.OrdinalIgnoreCase))
                    {
                        artistTag = tag.Substring(3).Trim();
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
                // 按文件顺序把译文并到它自己的原文上（细节见 MergeTranslations）
                lines = MergeTranslations(lines, titleTag, artistTag);
                // 稳定排序：同一时间戳保持原有先后顺序
                lines = new List<LyricLine>(lines.OrderBy(delegate(LyricLine line) { return line.Time; }));
            }

            doc.Lines = lines;
            doc.Synced = synced;
            doc.OffsetSeconds = offset;
            return doc;
        }

        /// <summary>
        /// 把歌词规范化成「译文与原文同一时间戳」的标准写法：每个原文行后面紧跟一行同时间戳的译文。
        /// 双语歌词的两种常见写法（译文同时间戳 / 译文带着下一句的时间戳）都会被整理成同一种，
        /// 这样两端播放器都不需要再猜配对关系。
        /// </summary>
        public static string Normalize(string text)
        {
            return Normalize(text, 0);
        }

        /// <summary>
        /// 规范化成「原文在上、译文在下、同一时间戳」，并整体平移 <paramref name="shiftSeconds"/> 秒。
        /// 平移用于修正整条时间轴偏早/偏晚的歌词（例如片源开头带了静音垫）。
        /// </summary>
        public static string Normalize(string text, double shiftSeconds)
        {
            if (text == null) return "";
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";

            // 保留开头的元数据行（[ti:] / [ar:] / [al:] / [by:] / [offset:] / [ml:] …），它们没有时间戳
            List<string> headers = new List<string>();
            string[] rows = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string row in rows)
            {
                string raw = row.Trim();
                if (raw.Length == 0 || raw[0] != '[') continue;
                int end = raw.IndexOf(']');
                if (end <= 1) continue;
                string tag = raw.Substring(1, end - 1);
                if (tag.IndexOf(':') > 0)
                {
                    string head = tag.Substring(0, tag.IndexOf(':'));
                    double ignored;
                    bool isTime = head.Length > 0 && char.IsDigit(head[0]) && TryParseTimeTag(tag, out ignored);
                    if (!isTime && headers.IndexOf(raw) < 0) headers.Add(raw);
                }
            }

            LyricDocument doc = Parse(text);
            StringBuilder sb = new StringBuilder();
            foreach (string header in headers) sb.Append(header).Append(newline);
            foreach (LyricLine line in doc.Lines)
            {
                if (line.Time < 0)
                {
                    sb.Append(line.Text).Append(newline);   // 纯文本歌词
                    continue;
                }
                double time = line.Time + shiftSeconds;
                if (time < 0) time = 0;
                string stamp = "[" + FormatStamp(time) + "]";
                sb.Append(stamp).Append(line.Text).Append(newline);
                if (!string.IsNullOrEmpty(line.Translation))
                    sb.Append(stamp).Append(line.Translation).Append(newline);
            }
            return sb.ToString();
        }

        private static string FormatStamp(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int total = (int)Math.Floor(seconds);
            int minutes = total / 60;
            double rest = seconds - minutes * 60;
            return minutes.ToString("00", CultureInfo.InvariantCulture) + ":"
                 + rest.ToString("00.00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 双语歌词配对。两种常见写法都要照顾：
        ///   A) 原文与译文同一时间戳（原文在前、译文在后）；
        ///   B) 译文紧跟在原文之后，但时间戳被标成了下一句的时间
        ///      （例如 [00:59.99]In my dreams / [01:01.66]我的梦里 / [01:01.66]I feel your light）。
        /// 两种写法里「译文都紧跟在它自己的原文之后」，所以按文件顺序配对、并沿用原文的时间戳；
        /// 老写法「同一时间戳取第二行当译文」在 B 里会把上一句的译文配到下一句原文上，才会出现错位。
        ///
        /// 判断哪一行是译文：用 [ti:标题] 的语言（其次看第一行、最后看多数派），细节见 PickOriginalLanguage。
        /// </summary>
        private static List<LyricLine> MergeTranslations(List<LyricLine> lines, string titleTag, string artistTag)
        {
            if (lines.Count == 0) return lines;

            // 网易云导出的 lrc 第一行常常是「歌名 - 歌手」这种自动生成的行，它不是歌词。
            // 以前它会被当成「原文」，把真正的第一句歌词当成译文吃掉，整首歌就错开一行。
            if (IsAutoTitleLine(lines[0].Text, titleTag, artistTag)) lines.RemoveAt(0);
            if (lines.Count == 0) return lines;

            // 双语歌词里「原文＋译文」的时间戳必定成对：要么两者相同，要么译文被标成下一句的时间
            //（那样也会跟下一句撞上）。整篇一个重复时间戳都没有 → 这文件根本没有译文，别硬配，
            // 否则中文歌里夹的英文副歌（例如《星辰大海》的 It's my dream it's magic）会被当成上一句的译文。
            if (!HasRepeatedTimestamp(lines)) return lines;

            int zh = 0, ja = 0, latin = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string kind = ScriptOf(lines[i].Text);
                if (kind == "ja") ja++;
                else if (kind == "zh") zh++;
                else latin++;
            }

            // 第一行往往是「作词 : 某某」这类信息行，判断语言要用第一句真正的歌词
            string firstLyric = null;
            for (int i = 0; i < lines.Count; i++)
            {
                if (IsMetadataLine(lines[i].Text)) continue;
                firstLyric = lines[i].Text;
                break;
            }
            if (firstLyric == null) firstLyric = lines[0].Text;
            string original = PickOriginalLanguage(titleTag, ScriptOf(firstLyric),
                zh, ja, latin, lines.Count);

            // 标准排版（原文在上、译文在下、两者时间戳相同）就按时间戳分组配对：
            // 组内第一行一定是原文。这样连「絶対徹夜」这种纯汉字日文原句也不会被当成译文。
            if (PrefersGroupLayout(lines, original)) return PairByGroup(lines);

            List<LyricLine> result = new List<LyricLine>();
            LyricLine pending = null;   // 还没配到译文的原文
            for (int i = 0; i < lines.Count; i++)
            {
                LyricLine line = lines[i];
                // 「词：/曲：/Lyrics by」这类信息行不参与配对，
                // 否则它们会把整首歌词的原文/译文错开一行
                if (IsMetadataLine(line.Text))
                {
                    result.Add(line);
                    continue;
                }
                if (ScriptOf(line.Text) == original)
                {
                    result.Add(line);
                    pending = line;
                    continue;
                }
                // 译文：贴到上一句原文上。判断依据是「位置」——译文永远紧跟在它自己的原文之后，
                // 所以任何非原文语言的行都算译文（同一首歌里译文可能中文、英文混着来）。
                // 时间差只做一个很宽松的保险：长间奏会让两者相隔十几秒（实测有 16 秒的）。
                if (pending != null && string.IsNullOrEmpty(pending.Translation)
                    && line.Time - pending.Time <= 60.0)
                {
                    // 例外：日语原句里「絶対徹夜」这类纯汉字行会被判成中文，它不是上一句的译文，
                    // 而是自己的原文（下一行「绝对要熬夜了」才是它的译文）。
                    // 分辨方法：如果下一行也不是原文语言，那这一行更可能是原文。
                    if (i + 1 < lines.Count)
                    {
                        LyricLine next = lines[i + 1];
                        // 下一行和本行时间戳相同 → 本行多半是「旧排版」里那句被标成下一句时间的译文，
                        // 不是原文（例：[34.98]英勇无畏 维新革命 / [34.98]磊々落々反戦国家）。
                        if (ScriptOf(next.Text) != original && next.Time - line.Time <= 60.0
                            && Math.Abs(next.Time - line.Time) > 0.02)
                        {
                            result.Add(line);
                            pending = line;
                            continue;
                        }
                    }
                    pending.Translation = line.Text;
                    pending = null;
                    continue;
                }
                // 配不上就别当译文了，自己当原文（例如日语歌里「絶対徹夜」这种纯汉字行会被判成中文，
                // 但它其实是原文；当成原文后，紧跟的「绝对要熬夜了」才能配到它）
                result.Add(line);
                pending = line;
            }
            return result;
        }

        /// <summary>
        /// 整篇有没有两行共用同一个时间戳（双语文件必定有；纯单语文件不会有）。
        /// </summary>
        private static bool HasRepeatedTimestamp(List<LyricLine> lines)
        {
            for (int i = 0; i + 1 < lines.Count; i++)
            {
                if (Math.Abs(lines[i + 1].Time - lines[i].Time) <= 0.02) return true;
            }
            return false;
        }

        /// <summary>
        /// 判断这首歌是不是「标准排版」：原文与译文时间戳相同、原文在前。
        /// 看同一时间戳的成对行里，原文语言出现在前/后的次数谁多。
        /// </summary>
        private static bool PrefersGroupLayout(List<LyricLine> lines, string original)
        {
            int firstWins = 0, secondWins = 0;
            for (int i = 0; i + 1 < lines.Count; i++)
            {
                if (Math.Abs(lines[i + 1].Time - lines[i].Time) > 0.02) continue;
                bool a = ScriptOf(lines[i].Text) == original;
                bool b = ScriptOf(lines[i + 1].Text) == original;
                if (a && !b) firstWins++;
                else if (b && !a) secondWins++;
            }
            return firstWins > 0 && firstWins >= secondWins;
        }

        /// <summary>标准排版：同一时间戳的一组合并成「第一行原文 + 其余全部作译文」。</summary>
        private static List<LyricLine> PairByGroup(List<LyricLine> lines)
        {
            List<LyricLine> result = new List<LyricLine>();
            int i = 0;
            while (i < lines.Count)
            {
                LyricLine line = lines[i];
                if (IsMetadataLine(line.Text))
                {
                    result.Add(line);
                    i++;
                    continue;
                }
                StringBuilder extra = null;
                int j = i + 1;
                while (j < lines.Count && Math.Abs(lines[j].Time - line.Time) <= 0.02
                       && !IsMetadataLine(lines[j].Text))
                {
                    if (extra == null) extra = new StringBuilder();
                    if (extra.Length > 0) extra.Append('\n');
                    extra.Append(lines[j].Text);
                    j++;
                }
                if (extra != null && extra.Length > 0) line.Translation = extra.ToString();
                result.Add(line);
                i = j;
            }
            return result;
        }


        /// <summary>粗略判断一行歌词属于哪种文字：ja 含假名 / zh 只有汉字 / latin 其它。</summary>
        /// <summary>
        /// 判断整篇歌词里哪种文字是「原文」：
        ///   1) 优先用 [ti:标题] 的语言——标题一般就是原文语言
        ///      （实测《Take Me Hand》=latin、《願い～あの頃のキミへ～》=ja，两首都判对）；
        ///   2) 没有标题标签时用第一行的语言，并要求它在全篇占比不低于 1/4；
        ///   3) 再不行才用出现最多的那种语言（并列时以第一行为准）。
        /// 不能只看「谁多」：日语歌的中文译文里常多出「词：/曲：」这类信息行，
        /// 可能比原文还多一行，那样就会把译文当成原文，整篇配对错位。
        /// </summary>
        private static string PickOriginalLanguage(string titleTag, string firstKind,
            int zh, int ja, int latin, int total)
        {
            // 有假名的行只可能来自日文原文——中文译文里绝不会出现假名。
            // 这条要放在最前面：实测《summertime》的 [ti:] 是英文标题、歌词却是日文，
            // 而中文译文的行数又可能比日文原文多，只看标题或只看行数都会判错。
            if (ja >= 1 && ja * 4 >= total) return "ja";
            string titleKind = ScriptOf(titleTag);
            if (!string.IsNullOrEmpty(titleTag) && CountOf(titleKind, zh, ja, latin) > 0) return titleKind;
            if (CountOf(firstKind, zh, ja, latin) * 4 >= total) return firstKind;
            if (zh > ja && zh >= latin) return "zh";
            if (ja > zh && ja >= latin) return "ja";
            if (latin > zh && latin > ja) return "latin";
            return firstKind;
        }

        /// <summary>「歌名 - 歌手」这种自动生成的行（网易云导出的 lrc 常见），不算歌词。</summary>
        private static bool IsAutoTitleLine(string text, string titleTag, string artistTag)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(titleTag)) return false;
            string t = text.Trim();
            string title = titleTag.Trim();
            string core = CoreTitle(title);     // 去掉「(中文译名)」这类括注后再比
            // 「歌名」和「歌名 (中文译名)」两种写法都试一遍
            for (int attempt = 0; attempt < 2; attempt++)
            {
                string prefix = attempt == 0 ? title : core;
                if (attempt == 1 && prefix == title) break;
                if (t == prefix) return true;
                if (!t.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string rest = t.Substring(prefix.Length).TrimStart().TrimStart('-', '–', '—', '－').Trim();
                if (rest.Length == 0) return true;          //「歌名 -」这种残行
                // 后半段应当就是歌手名（可能带括号补充，也可能被截断）
                if (string.IsNullOrEmpty(artistTag)) return true;
                string artist = artistTag.Trim();
                string artistCore = CoreTitle(artist);
                if (artist.StartsWith(rest, StringComparison.Ordinal)
                    || rest.StartsWith(artist, StringComparison.Ordinal)
                    || artistCore.StartsWith(rest, StringComparison.Ordinal)
                    || rest.StartsWith(artistCore, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>去掉「(…)/(中文译名)」这类括注，方便比对「歌名 - 歌手」行。</summary>
        private static string CoreTitle(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string t = text.Trim();
            int cut = -1;
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] == '(' || t[i] == '（') { cut = i; break; }
            }
            if (cut > 0) t = t.Substring(0, cut).Trim();
            return t.Length > 0 ? t : text.Trim();
        }

        private static int CountOf(string kind, int zh, int ja, int latin)
        {
            if (kind == "ja") return ja;
            if (kind == "zh") return zh;
            return latin;
        }

        private static readonly string[] MetadataPrefixes = new string[]
        {
            "词:", "曲:", "编曲:", "作词:", "作曲:", "制作:", "制作人:", "混音:", "母带:", "录音:",
            "吉他:", "贝斯:", "鼓:", "键盘:", "和声:", "演唱:", "出品:", "监制:", "op:", "sp:",
            "lyrics by", "composed by", "music by", "written by", "produced by", "arranged by",
            "mixed by", "mastered by", "vocals by", "guitar by", "bass by"
        };

        /// <summary>「词：/曲：/Lyrics by」这类信息行：不参与双语配对，单独成行。</summary>
        private static bool IsMetadataLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            // 冒号前后的空格一起吃掉：「作词 : 某某」也算信息行
            string t = text.Trim().Replace('：', ':').ToLowerInvariant();
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\s*:\s*", ":");
            for (int i = 0; i < MetadataPrefixes.Length; i++)
            {
                if (t.StartsWith(MetadataPrefixes[i], StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static string ScriptOf(string text)
        {
            if (string.IsNullOrEmpty(text)) return "latin";
            bool hasKana = false, hasIdeograph = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= 0x3040 && c <= 0x30FF) || (c >= 0x31F0 && c <= 0x31FF)) hasKana = true;
                else if (c >= 0x4E00 && c <= 0x9FFF) hasIdeograph = true;
                if (hasKana) break;
            }
            if (hasKana) return "ja";
            return hasIdeograph ? "zh" : "latin";
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
        /// <summary>被跳过的、不支持的音频文件数量（例如 ape / dsf / amr）。</summary>
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
            new string[]
            {
                // Windows 10/11 自带内核直接能放的
                ".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac", ".aiff", ".aif",
                // 需要 ffmpeg 转码才能放的（设置里可以指定 ffmpeg 路径，没装就给提示）
                ".ogg", ".oga", ".opus", ".ape", ".wv"
            };

        /// <summary>认识的音频格式，但两端都不支持播放（扫描时跳过并计数）。</summary>
        public static readonly string[] UnsupportedExtensions =
            new string[] { ".mpc", ".tta", ".dsf", ".dff", ".amr" };

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
                // 本地这棵树里，子文件夹名就是歌单名（与云盘同一套规则）
                string relative = file.Substring(dir.Length).TrimStart('\\', '/');
                int sep = relative.IndexOfAny(new char[] { '\\', '/' });
                song.Playlist = sep > 0 ? relative.Substring(0, sep) : "";

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
                    if (s.LyricPageFontSize < 12 || s.LyricPageFontSize > 40) s.LyricPageFontSize = 16;
                    if (s.LyricShadowMode < 0 || s.LyricShadowMode > 2) s.LyricShadowMode = 1;
                    if (s.LyricOpacity < 0.2 || s.LyricOpacity > 1) s.LyricOpacity = 1.0;
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
