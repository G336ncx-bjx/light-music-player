using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Skylark
{
    /// <summary>播放队列视图。</summary>
    public class QueueView : UserControl
    {
        private readonly MainWindow main;
        private readonly ListBox list = new ListBox();
        private readonly TextBlock summary = Ui.Text("", 12.5, "TextMuted");
        private readonly TextBlock emptyHint = Ui.Text("", 13, "TextMuted");
        /** 右键点在哪一行上（多选菜单要用它当「主行」）。 */
        private Song menuSong;

        public QueueView(MainWindow owner)
        {
            main = owner;
            Padding = new Thickness(22, 16, 22, 4);

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions[0].Height = GridLength.Auto;
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);

            TextBlock title = Ui.Text("播放队列", 21, "Text", FontWeights.SemiBold);
            summary.Margin = new Thickness(0, 4, 0, 0);
            StackPanel headText = Ui.Column(0, title, summary);
            headText.VerticalAlignment = VerticalAlignment.Center;

            Button downloadButton = Ui.Button("下载", "OutlineButton", delegate { main.DownloadSongs(GetSelectedSongs()); });
            downloadButton.ToolTip = "把选中的歌下载到本地：可选只下音频 / 只下歌词 / 两者都下";
            Button playlistButton = Ui.Button("加入歌单", "OutlineButton",
                delegate { main.AddSelectionToPlaylist(GetSelectedSongs()); });
            playlistButton.ToolTip = "把选中的歌复制进另一个歌单";
            Button removeButton = Ui.Button("移除选中", "OutlineButton", delegate { RemoveSelected(); });
            removeButton.ToolTip = "把选中的歌从播放队列里移除（不动云盘文件）";
            Button clearButton = Ui.Button("清空队列", "OutlineButton", delegate { main.ClearQueue(); });
            StackPanel actions = Ui.Row(8, downloadButton, playlistButton, removeButton, clearButton);
            actions.VerticalAlignment = VerticalAlignment.Center;

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions[1].Width = GridLength.Auto;
            header.Children.Add(headText);
            Grid.SetColumn(actions, 1);
            header.Children.Add(actions);
            header.Margin = new Thickness(2, 0, 0, 14);
            root.Children.Add(header);

            list.Style = (Style)Application.Current.Resources["SongList"];
            list.ItemContainerStyle = (Style)Application.Current.Resources["SongItem"];
            list.ItemTemplate = (DataTemplate)Application.Current.Resources["QueueRowTemplate"];
            list.Padding = new Thickness(0, 2, 0, 8);
            list.SelectionMode = SelectionMode.Extended;
            list.MouseDoubleClick += delegate(object sender, MouseButtonEventArgs e) { PlayAt(e); };
            list.AddHandler(UIElement.MouseLeftButtonUpEvent,
                new MouseButtonEventHandler(delegate(object sender, MouseButtonEventArgs e) { PlayAt(e); }), true);
            list.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.A || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                list.SelectAll();
                e.Handled = true;
            };
            list.PreviewMouseRightButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                ListBoxItem item = FindItem(e.OriginalSource as DependencyObject);
                if (item == null) return;
                menuSong = item.DataContext as Song;
                if (item.IsSelected) return;
                list.SelectedItems.Clear();
                item.IsSelected = true;
            };
            list.ContextMenuOpening += delegate(object sender, ContextMenuEventArgs e)
            {
                List<Song> songs = GetSelectedSongs();
                if (songs.Count == 0 && menuSong == null)
                {
                    e.Handled = true;
                    return;
                }
                if (songs.Count <= 1)
                {
                    list.ContextMenu = BuildMenu(songs.Count == 1 ? songs[0] : menuSong);
                    return;
                }
                if (menuSong != null && songs.IndexOf(menuSong) < 0) songs.Add(menuSong);
                list.ContextMenu = BuildMenu(songs, menuSong != null ? menuSong : songs[0]);
            };

            Grid listHost = new Grid();
            listHost.Children.Add(list);
            emptyHint.HorizontalAlignment = HorizontalAlignment.Center;
            emptyHint.VerticalAlignment = VerticalAlignment.Center;
            emptyHint.TextAlignment = TextAlignment.Center;
            listHost.Children.Add(emptyHint);
            Grid.SetRow(listHost, 1);
            root.Children.Add(listHost);

            Content = root;
        }

        /// <summary>单击某一行播放它；点右侧「✕」则是从队列移除。</summary>
        private void PlayAt(MouseButtonEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject;
            if (FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) != null) return;

            ListBoxItem item = FindItem(source);
            if (item == null) return;
            Song song = item.DataContext as Song;
            if (song == null) return;

            if (FindAction(source, "remove"))
            {
                main.RemoveFromQueue(song);
                return;
            }
            if (FindAction(source, "download"))
            {
                List<Song> one = new List<Song>();
                one.Add(song);
                main.DownloadSongs(one);
                return;
            }
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return;
            int idx = main.Queue.IndexOf(song);
            if (idx >= 0) main.PlayFrom(main.Queue, idx);
        }

        private static bool FindAction(DependencyObject source, string tag)
        {
            while (source != null)
            {
                FrameworkElement element = source as FrameworkElement;
                if (element != null && element.Tag != null && object.Equals(element.Tag, tag)) return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private static T FindAncestor<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null && !(source is T))
            {
                source = VisualTreeHelper.GetParent(source);
            }
            return source as T;
        }

        public void RefreshItems()
        {
            List<Song> songs = main.Queue;
            int i = 1;
            foreach (Song song in songs)
            {
                song.QueueIndexText = (i++).ToString(CultureInfo.InvariantCulture);
                song.IsCurrent = i - 1 == main.QueueIndex + 1;
            }
            list.ItemsSource = null;
            list.ItemsSource = songs;
            summary.Text = songs.Count == 0 ? "队列为空" : songs.Count + " 首歌曲";
            emptyHint.Text = songs.Count == 0
                ? "队列还是空的。\n在音乐库里单击歌名就开始播放，右键「添加到播放队列」可以把歌排进来。"
                : "";
            emptyHint.Visibility = songs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private ContextMenu BuildMenu(Song song)
        {
            if (song == null) return null;
            List<Song> one = new List<Song>();
            one.Add(song);
            ContextMenu menu = new ContextMenu();
            menu.Items.Add(MenuItemFor("立即播放", delegate { main.PlayFrom(main.Queue, main.Queue.IndexOf(song)); }));
            menu.Items.Add(MenuItemFor("下一首播放", delegate { main.PlayNextInQueue(song); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItemFor("下载…", delegate { main.DownloadSongs(one); }));
            menu.Items.Add(MenuItemFor("加入歌单…", delegate { main.AddSelectionToPlaylist(one); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItemFor("上移", delegate { main.MoveInQueue(main.Queue.IndexOf(song), main.Queue.IndexOf(song) - 1); }));
            menu.Items.Add(MenuItemFor("下移", delegate { main.MoveInQueue(main.Queue.IndexOf(song), main.Queue.IndexOf(song) + 1); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItemFor("在资源管理器中显示", delegate { main.RevealInExplorer(song); }));
            menu.Items.Add(MenuItemFor("从队列中移除", delegate { main.RemoveFromQueue(song); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItemFor("导出 M3U…", delegate { ExportM3u(); }));
            menu.Items.Add(MenuItemFor("导入 M3U…", delegate { ImportM3u(); }));
            return menu;
        }

        /// <summary>多选时的右键菜单。</summary>
        private ContextMenu BuildMenu(List<Song> songs, Song anchor)
        {
            ContextMenu menu = new ContextMenu();
            string count = songs.Count.ToString(CultureInfo.InvariantCulture);
            menu.Items.Add(MenuItemFor("播放选中的 " + count + " 首", delegate { PlaySelected(songs); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItemFor("下载选中的 " + count + " 首…", delegate { main.DownloadSongs(songs); }));
            menu.Items.Add(MenuItemFor("加入歌单…", delegate { main.AddSelectionToPlaylist(songs); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItemFor("从队列中移除选中的 " + count + " 首", delegate { main.RemoveFromQueue(songs); }));
            return menu;
        }

        private void PlaySelected(List<Song> songs)
        {
            if (songs == null || songs.Count == 0) return;
            // 队列是按顺序播的，所以从「选中的第一首」在队列里的位置往下播
            int at = main.Queue.IndexOf(songs[0]);
            if (at >= 0) main.PlayFrom(main.Queue, at);
        }

        /// <summary>移除选中的歌（多选菜单 / 顶部按钮都用它）。</summary>
        private void RemoveSelected()
        {
            List<Song> songs = GetSelectedSongs();
            if (songs.Count == 0)
            {
                main.ShowToast("先选中歌曲（Ctrl / Shift 可多选）");
                return;
            }
            main.RemoveFromQueue(songs);
        }

        /// <summary>当前选中的歌（Ctrl 点选、Shift 连选、Ctrl+A 全选）。</summary>
        public List<Song> GetSelectedSongs()
        {
            List<Song> result = new List<Song>();
            foreach (object entry in list.SelectedItems)
            {
                Song song = entry as Song;
                if (song != null) result.Add(song);
            }
            return result;
        }

        private static MenuItem MenuItemFor(string text, RoutedEventHandler handler)
        {
            MenuItem item = new MenuItem();
            item.Header = text;
            item.Click += handler;
            return item;
        }

        private void ExportM3u()
        {
            if (main.Queue.Count == 0)
            {
                main.ShowToast("播放队列是空的");
                return;
            }
            Forms.SaveFileDialog dialog = new Forms.SaveFileDialog();
            dialog.Filter = "播放列表 (*.m3u)|*.m3u";
            dialog.FileName = "播放队列.m3u";
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
            try
            {
                M3u.Save(dialog.FileName, main.Queue);
                main.ShowToast("已导出播放列表");
            }
            catch (Exception ex)
            {
                main.ShowToast("导出失败：" + ex.Message);
            }
        }

        private void ImportM3u()
        {
            Forms.OpenFileDialog dialog = new Forms.OpenFileDialog();
            dialog.Filter = "播放列表 (*.m3u;*.m3u8)|*.m3u;*.m3u8|所有文件 (*.*)|*.*";
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
            try
            {
                List<string> paths = M3u.Load(dialog.FileName);
                List<Song> songs = new List<Song>();
                foreach (string path in paths)
                {
                    Song found = null;
                    foreach (Song song in main.Library)
                    {
                        if (string.Equals(song.Path, path, StringComparison.OrdinalIgnoreCase)) { found = song; break; }
                    }
                    if (found != null) songs.Add(found);
                }
                if (songs.Count == 0)
                {
                    main.ShowToast("列表中的歌曲不在当前音乐库中");
                    return;
                }
                main.PlayFrom(songs, 0);
                main.ShowToast("已载入 " + songs.Count + " 首歌曲");
            }
            catch (Exception ex)
            {
                main.ShowToast("导入失败：" + ex.Message);
            }
        }

        private Song SongAt(DependencyObject source)
        {
            ListBoxItem item = FindItem(source);
            if (item == null) return null;
            return item.DataContext as Song;
        }

        private static ListBoxItem FindItem(DependencyObject source)
        {
            while (source != null && !(source is ListBoxItem))
            {
                source = VisualTreeHelper.GetParent(source);
            }
            return source as ListBoxItem;
        }
    }
}
