using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace LightMusic
{
    /// <summary>云盘上的一个条目（文件或目录）。</summary>
    public class CloudEntry
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }

        public CloudEntry()
        {
            Name = string.Empty;
            Path = string.Empty;
        }
    }

    [DataContract]
    internal class DirentDto
    {
        [DataMember(Name = "file_name")]
        public string Name { get; set; }

        [DataMember(Name = "file_path")]
        public string Path { get; set; }

        [DataMember(Name = "is_dir")]
        public bool IsDirectory { get; set; }

        [DataMember(Name = "size")]
        public long Size { get; set; }

        [DataMember(Name = "last_modified")]
        public string Modified { get; set; }
    }

    [DataContract]
    internal class DirentListDto
    {
        [DataMember(Name = "dirent_list")]
        public List<DirentDto> Items { get; set; }
    }

    /// <summary>
    /// 云盘（Seafile 分享链接）访问：列目录、读取文件头、下载、读取歌词。
    /// 服务端要求带 User-Agent，否则返回 403。
    /// </summary>
    public static class CloudClient
    {
        public const string DefaultHost = "https://cloud.tsinghua.edu.cn";
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) LightMusic/1.2 (+https://github.com/G336ncx-bjx/light-music-player)";

        static CloudClient()
        {
            // .NET Framework 默认只启用老协议，这里显式打开 TLS 1.2
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12
                    | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                ServicePointManager.DefaultConnectionLimit = 8;
            }
            catch (Exception)
            {
            }
        }

        /// <summary>从分享链接（或裸 token）中取出 token。</summary>
        public static string ParseToken(string urlOrToken)
        {
            if (string.IsNullOrEmpty(urlOrToken)) return string.Empty;
            string text = urlOrToken.Trim();
            if (text.IndexOf('/') < 0) return text.Trim('/');

            string[] parts = text.Split('/');
            for (int i = 0; i + 1 < parts.Length; i++)
            {
                if (parts[i] == "d" || parts[i] == "upload") return parts[i + 1];
            }
            return parts[parts.Length - 1];
        }

        /// <summary>取分享链接所在的主机（默认清华云盘）。</summary>
        public static string ParseHost(string urlOrToken)
        {
            if (string.IsNullOrEmpty(urlOrToken)) return DefaultHost;
            string text = urlOrToken.Trim();
            int scheme = text.IndexOf("://", StringComparison.Ordinal);
            if (scheme < 0) return DefaultHost;
            int start = scheme + 3;
            int slash = text.IndexOf('/', start);
            string host = slash < 0 ? text : text.Substring(0, slash);
            return host.Length > 8 ? host : DefaultHost;
        }

        public static string ShareUrl(string urlOrToken)
        {
            return ParseHost(urlOrToken) + "/d/" + ParseToken(urlOrToken);
        }

        public static string EscapePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string[] parts = path.Split('/');
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                if (sb.Length > 0) sb.Append('/');
                sb.Append(Uri.EscapeDataString(parts[i]));
            }
            return sb.ToString();
        }

        /// <summary>列出某个目录下的条目。</summary>
        public static List<CloudEntry> List(string urlOrToken, string path)
        {
            string token = ParseToken(urlOrToken);
            string host = ParseHost(urlOrToken);
            if (token.Length == 0) throw new InvalidOperationException("分享链接无效");

            string url = host + "/api/v2.1/share-links/" + token + "/dirents/?path=%2F"
                       + EscapePath(path);
            string json = GetString(url);

            DirentListDto dto;
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(DirentListDto));
                dto = (DirentListDto)ser.ReadObject(ms);
            }

            List<CloudEntry> list = new List<CloudEntry>();
            if (dto == null || dto.Items == null) return list;
            foreach (DirentDto item in dto.Items)
            {
                if (item == null || string.IsNullOrEmpty(item.Name)) continue;
                CloudEntry entry = new CloudEntry();
                entry.Name = item.Name;
                entry.Path = string.IsNullOrEmpty(item.Path) ? "/" + item.Name : item.Path;
                entry.IsDirectory = item.IsDirectory;
                entry.Size = item.Size;
                DateTime modified;
                if (!string.IsNullOrEmpty(item.Modified) &&
                    DateTime.TryParse(item.Modified, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal, out modified))
                    entry.Modified = modified;
                else
                    entry.Modified = DateTime.UtcNow;
                list.Add(entry);
            }
            return list;
        }

        /// <summary>递归列出所有文件（最多 5 层深）。</summary>
        public static List<CloudEntry> ListAllFiles(string urlOrToken, int maxDepth)
        {
            List<CloudEntry> files = new List<CloudEntry>();
            List<string> dirs = new List<string>();
            dirs.Add(string.Empty);
            int depth = 0;
            while (dirs.Count > 0 && depth <= maxDepth)
            {
                List<string> next = new List<string>();
                foreach (string dir in dirs)
                {
                    List<CloudEntry> entries = List(urlOrToken, dir);
                    foreach (CloudEntry entry in entries)
                    {
                        if (entry.IsDirectory) next.Add(Trim(entry.Path));
                        else files.Add(entry);
                    }
                }
                dirs = next;
                depth++;
            }
            return files;
        }

        private static string Trim(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return path.Trim('/');
        }

        public static string BuildFileUrl(string urlOrToken, string filePath)
        {
            string token = ParseToken(urlOrToken);
            string host = ParseHost(urlOrToken);
            string encoded = filePath.StartsWith("/") ? filePath.Substring(1) : filePath;
            return host + "/d/" + token + "/files/?p=%2F" + EscapePath(encoded) + "&dl=1";
        }

        /// <summary>只取文件前若干字节（利用 Range），用于解析时长等信息。</summary>
        public static byte[] DownloadHead(string urlOrToken, string filePath, int bytes)
        {
            HttpWebRequest request = CreateRequest(BuildFileUrl(urlOrToken, filePath));
            request.AddRange(0, bytes - 1);
            using (WebResponse response = request.GetResponse())
            {
                using (Stream stream = response.GetResponseStream())
                {
                    MemoryStream buffer = new MemoryStream();
                    byte[] chunk = new byte[16384];
                    int total = 0;
                    while (total < bytes)
                    {
                        int read = stream.Read(chunk, 0, Math.Min(chunk.Length, bytes - total));
                        if (read <= 0) break;
                        buffer.Write(chunk, 0, read);
                        total += read;
                    }
                    return buffer.ToArray();
                }
            }
        }

        public static string GetText(string urlOrToken, string filePath)
        {
            byte[] data = DownloadAll(urlOrToken, filePath);
            return TextUtil.DecodeBytes(data);
        }

        public static byte[] DownloadAll(string urlOrToken, string filePath)
        {
            HttpWebRequest request = CreateRequest(BuildFileUrl(urlOrToken, filePath));
            using (WebResponse response = request.GetResponse())
            {
                using (Stream stream = response.GetResponseStream())
                {
                    MemoryStream buffer = new MemoryStream();
                    byte[] chunk = new byte[65536];
                    int read;
                    while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        buffer.Write(chunk, 0, read);
                    }
                    return buffer.ToArray();
                }
            }
        }

        /// <summary>下载到本地文件，progress 回调参数为 (已下载字节, 总字节)。</summary>
        public static void DownloadTo(string urlOrToken, string filePath, string targetPath,
            Action<long, long> progress)
        {
            HttpWebRequest request = CreateRequest(BuildFileUrl(urlOrToken, filePath));
            request.Timeout = 30000;
            string temp = targetPath + ".part";
            string dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            using (WebResponse response = request.GetResponse())
            {
                long total = response.ContentLength;
                using (Stream stream = response.GetResponseStream())
                {
                    using (FileStream file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] chunk = new byte[131072];
                        long done = 0;
                        int read;
                        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                        {
                            file.Write(chunk, 0, read);
                            done += read;
                            if (progress != null) progress(done, total);
                        }
                    }
                }
            }

            if (File.Exists(targetPath)) File.Delete(targetPath);
            File.Move(temp, targetPath);
        }

        /// <summary>取得某个目录的上传地址（分享链接需要开启上传权限）。</summary>
        public static string GetUploadUrl(string urlOrToken, string dirPath)
        {
            string token = ParseToken(urlOrToken);
            string host = ParseHost(urlOrToken);
            string path = string.IsNullOrEmpty(dirPath) ? "/" : dirPath;
            string url = host + "/api/v2.1/share-links/" + token + "/upload/?path="
                       + Uri.EscapeDataString(path);
            string json = GetString(url);

            int at = json.IndexOf("\"upload_link\"", StringComparison.Ordinal);
            if (at < 0) throw new InvalidOperationException("该分享链接没有开启上传权限");
            int start = json.IndexOf('"', json.IndexOf(':', at) + 1);
            int end = json.IndexOf('"', start + 1);
            if (start < 0 || end < 0) throw new InvalidOperationException("上传地址解析失败");
            return json.Substring(start + 1, end - start - 1);
        }

        /// <summary>
        /// 上传本地文件到云盘的指定目录。
        /// progress 回调参数为 (已上传字节, 总字节)；同名文件会自动重试覆盖。
        /// </summary>
        public static bool Upload(string urlOrToken, string localFilePath, string dirPath,
            Action<long, long> progress, out bool replaced)
        {
            replaced = false;
            string link = GetUploadUrl(urlOrToken, dirPath);
            string result;
            bool ok = TryUpload(link, localFilePath, dirPath, progress, false, out result);
            if (ok) return true;

            if (result != null && (result.IndexOf("exist", StringComparison.OrdinalIgnoreCase) >= 0
                || result.IndexOf("already", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                ok = TryUpload(link, localFilePath, dirPath, progress, true, out result);
                replaced = ok;
            }
            if (!ok) throw new InvalidOperationException(Shorten(result));
            return true;
        }

        private static bool TryUpload(string uploadLink, string localFilePath, string dirPath,
            Action<long, long> progress, bool replace, out string error)
        {
            error = null;
            string url = uploadLink + "?ret-json=1";
            if (replace) url += "&replace=1";

            string boundary = "----LightMusic" + Guid.NewGuid().ToString("N");
            string fileName = Path.GetFileName(localFilePath);
            string dir = string.IsNullOrEmpty(dirPath) ? "/" : dirPath;
            if (!dir.StartsWith("/")) dir = "/" + dir;

            byte[] dirPart = FormField(boundary, "parent_dir", dir);
            byte[] relPart = FormField(boundary, "relative_path", string.Empty);
            byte[] filePart = FileFieldHeader(boundary, fileName);
            byte[] tail = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
            long fileLength = new FileInfo(localFilePath).Length;

            HttpWebRequest request = CreateRequest(url);
            request.Method = "POST";
            request.ContentType = "multipart/form-data; boundary=" + boundary;
            request.ContentLength = dirPart.Length + relPart.Length + filePart.Length + fileLength + tail.Length;
            request.Timeout = 60000;

            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(dirPart, 0, dirPart.Length);
                stream.Write(relPart, 0, relPart.Length);
                stream.Write(filePart, 0, filePart.Length);

                using (FileStream file = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] buffer = new byte[131072];
                    long done = 0;
                    int read;
                    while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        stream.Write(buffer, 0, read);
                        done += read;
                        if (progress != null) progress(done, fileLength);
                    }
                }
                stream.Write(tail, 0, tail.Length);
            }

            try
            {
                using (WebResponse response = request.GetResponse())
                {
                    using (Stream stream = response.GetResponseStream())
                    {
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            string body = reader.ReadToEnd();
                            if (body.IndexOf("\"error\"", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                error = body;
                                return false;
                            }
                            return true;
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                error = ReadError(ex);
                return false;
            }
        }

        private static string ReadError(WebException ex)
        {
            try
            {
                if (ex.Response != null)
                {
                    using (Stream stream = ex.Response.GetResponseStream())
                    {
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            return reader.ReadToEnd();
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return ex.Message;
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text)) return "上传失败";
            string clean = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return clean.Length > 160 ? clean.Substring(0, 160) : clean;
        }

        private static byte[] FormField(string boundary, string name, string value)
        {
            string text = "--" + boundary + "\r\n"
                + "Content-Disposition: form-data; name=\"" + name + "\"\r\n\r\n"
                + value + "\r\n";
            return Encoding.UTF8.GetBytes(text);
        }

        private static byte[] FileFieldHeader(string boundary, string fileName)
        {
            string text = "--" + boundary + "\r\n"
                + "Content-Disposition: form-data; name=\"file\"; filename=\""
                + fileName.Replace("\"", "_") + "\"\r\n"
                + "Content-Type: application/octet-stream\r\n\r\n";
            return Encoding.UTF8.GetBytes(text);
        }

        private static HttpWebRequest CreateRequest(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent;
            request.Timeout = 20000;
            request.ReadWriteTimeout = 30000;
            request.AllowAutoRedirect = true;
            request.KeepAlive = true;
            return request;
        }

        private static string GetString(string url)
        {
            HttpWebRequest request = CreateRequest(url);
            using (WebResponse response = request.GetResponse())
            {
                using (Stream stream = response.GetResponseStream())
                {
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
        }
    }
}
