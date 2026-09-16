using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Skylark
{
    /// <summary>
    /// 可选依赖：系统里如果有 ffmpeg，就能把 Windows 播放内核放不了的格式
    /// （FLAC / OGG / OPUS 等）自动转成 MP3 再播放。
    /// </summary>
    public static class Ffmpeg
    {
        private static string cachedPath;
        private static bool probed;

        /// <summary>这些扩展名 Windows 自带内核放不了，需要转码。</summary>
        public static bool NeedsTranscode(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".flac" || ext == ".ogg" || ext == ".opus" || ext == ".ape" || ext == ".wv";
        }

        /// <summary>查找 ffmpeg：先看设置，再看 PATH，再看几个常见安装位置。</summary>
        public static string Locate(string configured)
        {
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
            if (probed) return cachedPath;
            probed = true;

            try
            {
                string pathVariable = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathVariable))
                {
                    foreach (string dir in pathVariable.Split(';'))
                    {
                        if (dir.Trim().Length == 0) continue;
                        try
                        {
                            string candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                            if (File.Exists(candidate))
                            {
                                cachedPath = candidate;
                                return cachedPath;
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            string[] guesses = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "scoop\\shims\\ffmpeg.exe"),
                @"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
                @"C:\ffmpeg\bin\ffmpeg.exe"
            };
            foreach (string guess in guesses)
            {
                try
                {
                    if (File.Exists(guess))
                    {
                        cachedPath = guess;
                        return cachedPath;
                    }
                }
                catch (Exception)
                {
                }
            }
            return null;
        }

        /// <summary>
        /// 把音频转成 MP3（320kbps）。成功返回 true，失败通过 error 返回原因。
        /// 转换在工作线程里执行，onProgress 收到 0-100 的估算进度（可能为 -1）。
        /// </summary>
        public static bool ToMp3(string ffmpegPath, string source, string target,
            double totalSeconds, Action<int> onProgress, out string error)
        {
            error = null;
            string temp = target + ".tmp.mp3";
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (Exception)
            {
            }

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = ffmpegPath;
            info.Arguments = "-hide_banner -nostdin -y -i \"" + source + "\" -vn -map_metadata 0 "
                + "-c:a libmp3lame -b:a 320k -id3v2_version 3 \"" + temp + "\"";
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardError = true;
            info.RedirectStandardOutput = true;

            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = info;
                    process.Start();

                    // ffmpeg 把进度写在 stderr 里（time=00:00:12.34）
                    string line;
                    while ((line = process.StandardError.ReadLine()) != null)
                    {
                        if (onProgress == null || totalSeconds <= 0) continue;
                        int at = line.IndexOf("time=", StringComparison.Ordinal);
                        if (at < 0) continue;
                        string stamp = line.Substring(at + 5).Trim().Split(' ')[0];
                        double seconds;
                        if (!TryParseStamp(stamp, out seconds)) continue;
                        int percent = (int)Math.Min(100, seconds * 100 / totalSeconds);
                        onProgress(percent);
                    }
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        error = "ffmpeg 退出码 " + process.ExitCode;
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            try
            {
                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryParseStamp(string stamp, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrEmpty(stamp)) return false;
            string[] parts = stamp.Split(':');
            if (parts.Length == 3)
            {
                double h, m, s;
                if (double.TryParse(parts[0], out h) && double.TryParse(parts[1], out m)
                    && double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out s))
                {
                    seconds = h * 3600 + m * 60 + s;
                    return true;
                }
            }
            return false;
        }
    }
}
