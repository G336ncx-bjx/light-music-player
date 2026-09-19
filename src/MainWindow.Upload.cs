using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Skylark
{
    public partial class MainWindow
    {
        #region 上传到云盘

        private void OnDropFiles(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0) return;
            UploadFiles(paths);
        }

        /// <summary>选择本地文件上传到云盘分享目录。</summary>
        public void PickAndUploadFiles()
        {
            if (!IsCloudSource)
            {
                ShowToast("请先在设置里填写云盘分享链接");
                return;
            }
            Forms.OpenFileDialog dialog = new Forms.OpenFileDialog();
            dialog.Title = "选择要上传到云盘的歌曲 / 歌词";
            dialog.Multiselect = true;
            dialog.Filter = "歌曲与歌词|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.lrc|所有文件 (*.*)|*.*";
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
            UploadFiles(dialog.FileNames);
        }

        /// <summary>把文件（或文件夹里的歌曲与歌词）上传到云盘分享目录。</summary>
        public void UploadFiles(string[] paths)
        {
            if (!IsCloudSource)
            {
                ShowToast("请先在设置里填写云盘分享链接");
                return;
            }
            if (uploading)
            {
                ShowToast("还有上传任务在进行中");
                return;
            }

            List<string> files = new List<string>();
            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                try
                {
                    if (Directory.Exists(path))
                    {
                        foreach (string file in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories))
                        {
                            if (IsUploadable(file)) files.Add(file);
                        }
                    }
                    else if (File.Exists(path) && IsUploadable(path))
                    {
                        files.Add(path);
                    }
                }
                catch (Exception)
                {
                }
            }

            if (files.Count == 0)
            {
                ShowToast("没有可上传的文件（支持音频与 .lrc 歌词）");
                return;
            }

            uploading = true;
            string url = CloudEndpoint;
            List<string> batch = files;
            // 歌单＝云盘文件夹：在哪里点的「上传」就传进哪个歌单；没选歌单就进「默认歌单」，
            // 都没有才落到云盘根目录。
            string targetDir = "/";
            string targetLabel = "云盘根目录";
            if (playlistFilter.Length > 0)
            {
                targetDir = PlaylistDir(playlistFilter);
                targetLabel = "歌单「" + playlistFilter + "」";
            }
            else
            {
                List<string> names = PlaylistNames();
                if (names.Contains("默认歌单"))
                {
                    targetDir = PlaylistDir("默认歌单");
                    targetLabel = "歌单「默认歌单」";
                }
            }
            if (statusText != null) statusText.Text = "正在上传 0/" + batch.Count + "…";
            ShowToast("正在上传 " + batch.Count + " 个文件到" + targetLabel);

            ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0;
                List<string> failed = new List<string>();
                bool dirReady = targetDir == "/";
                for (int i = 0; i < batch.Count; i++)
                {
                    string path = batch[i];
                    string name = Path.GetFileName(path);
                    int index = i + 1;
                    try
                    {
                        if (!dirReady)
                        {
                            CloudClient.EnsureDir(url, targetDir);
                            dirReady = true;
                        }
                        bool replaced;
                        CloudClient.Upload(url, path, targetDir, delegate(long done, long total)
                        {
                            int percent = total > 0 ? (int)(done * 100 / total) : 0;
                            Dispatcher.BeginInvoke((Action)delegate
                            {
                                if (statusText != null)
                                    statusText.Text = "正在上传 " + index + "/" + batch.Count + "："
                                        + name + " " + percent + "%";
                            });
                        }, out replaced);
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        failed.Add(name + "：" + ex.Message);
                    }
                }

                Dispatcher.BeginInvoke((Action)delegate
                {
                    uploading = false;
                    UpdateStatusText();
                    if (failed.Count == 0)
                    {
                        ShowToast("已上传 " + ok + " 个文件到" + targetLabel);
                    }
                    else
                    {
                        ShowToast("上传完成：" + ok + " 个成功，" + failed.Count + " 个失败（"
                            + failed[0] + "）");
                    }
                    SaveSettings();
                    Rescan();
                });
            });
        }

        private static bool IsUploadable(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".lrc") return true;
            return Array.IndexOf(LibraryScanner.Extensions, ext) >= 0;
        }

        #endregion
    }
}
