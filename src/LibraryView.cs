using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LightMusic
{
    /// <summary>音乐库视图（歌曲列表 + 排序 + 右键菜单）。</summary>
    public class LibraryView : UserControl
    {
        private readonly MainWindow main;
        private readonly ListBox list = new ListBox();
        private readonly TextBlock summary = Ui.Text("", 12.5, "TextMuted");
        private readonly Button headIndex = new Button();
        private readonly Button headTitle = new Button();
        private readonly Button headArtist = new Button();
        private readonly Button headDuration = new Button();
        private readonly Border emptyState = new Border();

        public LibraryView(MainWindow owner)
        {
            main = owner;
            Padding = new Thickness(22, 16, 22, 4);

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions[0].Height = GridLength.Auto;
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions[1].Height = GridLength.Auto;
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);

            // 标题
            TextBlock title = Ui.Text("音乐库", 21, "Text", FontWeights.SemiBold);
            summary.Margin = new Thickness(0, 4, 0, 0);
            StackPanel head = Ui.Column(0, title, summary);
            head.VerticalAlignment = VerticalAlignment.Center;
            head.Margin = new Thickness(2, 0, 0, 12);

            Button playAll = IconTextButton("play", "播放全部", "PrimaryButton", delegate { PlayAll(); });
            Button shuffleAll = IconTextButton("shuffle", "随机播放", "OutlineButton", delegate { ShuffleAll(); });
            StackPanel headActions = Ui.Row(8, playAll, shuffleAll);
            headActions.VerticalAlignment = VerticalAlignment.Center;

            Grid headRow = new Grid();
            headRow.ColumnDefinitions.Add(new ColumnDefinition());
            headRow.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            headRow.ColumnDefinitions.Add(new ColumnDefinition());
            headRow.ColumnDefinitions[1].Width = GridLength.Auto;
            headRow.Children.Add(head);
            Grid.SetColumn(headActions, 1);
            headRow.Children.Add(headActions);
            headRow.Margin = new Thickness(0, 0, 0, 12);
            root.Children.Add(headRow);

            // 列头
            Grid columns = new Grid();
            columns.Margin = new Thickness(10, 0, 22, 2);
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions[0].Width = Ui.Px(56);
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions[1].Width = Ui.Stars(1);
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions[2].Width = Ui.Px(190);
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions[3].Width = Ui.Px(58);
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions[4].Width = Ui.Px(36);

            Style headerStyle = (Style)Application.Current.Resources["ColumnHeader"];
            SetupHeader(headIndex, "序号", headerStyle, SortField.Default);
            SetupHeader(headTitle, "歌曲", headerStyle, SortField.Title);
            SetupHeader(headArtist, "歌手", headerStyle, SortField.Artist);
            SetupHeader(headDuration, "时长", headerStyle, SortField.Duration);
            headTitle.HorizontalContentAlignment = HorizontalAlignment.Left;
            headArtist.HorizontalContentAlignment = HorizontalAlignment.Left;
            headDuration.HorizontalContentAlignment = HorizontalAlignment.Right;
            headIndex.HorizontalContentAlignment = HorizontalAlignment.Center;

            columns.Children.Add(headIndex);
            Grid.SetColumn(headTitle, 1);
            columns.Children.Add(headTitle);
            Grid.SetColumn(headArtist, 2);
            columns.Children.Add(headArtist);
            Grid.SetColumn(headDuration, 3);
            columns.Children.Add(headDuration);

            Grid.SetRow(columns, 1);
            root.Children.Add(columns);

            // 列表
            list.Style = (Style)Application.Current.Resources["SongList"];
            list.ItemContainerStyle = (Style)Application.Current.Resources["SongItem"];
            list.ItemTemplate = (DataTemplate)Application.Current.Resources["SongRowTemplate"];
            list.Padding = new Thickness(0, 2, 0, 8);
            list.SelectionMode = SelectionMode.Single;
            list.MouseDoubleClick += OnDoubleClick;
            list.AddHandler(UIElement.MouseLeftButtonUpEvent,
                new MouseButtonEventHandler(OnItemClick), true);
            list.PreviewMouseRightButtonDown += OnRightDown;
            list.ContextMenuOpening += OnContextMenu;
            list.KeyDown += OnKeyDown;
            Grid.SetRow(list, 2);
            root.Children.Add(list);

            // 空状态
            emptyState.Visibility = Visibility.Collapsed;
            emptyState.HorizontalAlignment = HorizontalAlignment.Center;
            emptyState.VerticalAlignment = VerticalAlignment.Center;
            StackPanel emptyPanel = new StackPanel();
            emptyPanel.HorizontalAlignment = HorizontalAlignment.Center;
            Canvas emptyIcon = Icons.Create("music", 46, "TextMuted");
            emptyIcon.HorizontalAlignment = HorizontalAlignment.Center;
            TextBlock emptyTitle = Ui.Text("这里还没有歌曲", 16, "Text", FontWeights.SemiBold);
            emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
            emptyTitle.Margin = new Thickness(0, 12, 0, 0);
            TextBlock emptyHint = Ui.Text("把歌曲（歌名 - 歌手.mp3）和同名 .lrc 歌词放进音乐文件夹即可", 12.5, "TextMuted");
            emptyHint.HorizontalAlignment = HorizontalAlignment.Center;
            emptyHint.Margin = new Thickness(0, 6, 0, 0);
            Button choose = Ui.Button("选择音乐文件夹", "PrimaryButton", delegate { main.ChooseMusicDir(); });
            choose.HorizontalAlignment = HorizontalAlignment.Center;
            choose.Margin = new Thickness(0, 16, 0, 0);
            emptyPanel.Children.Add(emptyIcon);
            emptyPanel.Children.Add(emptyTitle);
            emptyPanel.Children.Add(emptyHint);
            emptyPanel.Children.Add(choose);
            emptyState.Child = emptyPanel;
            Grid.SetRow(emptyState, 2);
            root.Children.Add(emptyState);

            Content = root;
        }

        private void SetupHeader(Button button, string text, Style style, SortField field)
        {
            button.Style = style;
            button.Content = Ui.Text(text, 12, "TextMuted");
            button.Tag = field;
            button.Click += delegate { SortBy((SortField)button.Tag); };
        }

        private void SortBy(SortField field)
        {
            if (main.Settings.Sort == field)
                main.Settings.SortAscending = !main.Settings.SortAscending;
            else
            {
                main.Settings.Sort = field;
                main.Settings.SortAscending = true;
            }
            main.ApplyFilter();
            main.SaveSettings();
            UpdateHeaderArrows();
        }

        private void UpdateHeaderArrows()
        {
            string arrow = main.Settings.SortAscending ? " ▲" : " ▼";
            headIndex.Content = Ui.Text("序号" + (main.Settings.Sort == SortField.Default ? arrow : ""), 12, "TextMuted");
            headTitle.Content = Ui.Text("歌曲" + (main.Settings.Sort == SortField.Title ? arrow : ""), 12, "TextMuted");
            headArtist.Content = Ui.Text("歌手" + (main.Settings.Sort == SortField.Artist ? arrow : ""), 12, "TextMuted");
            headDuration.Content = Ui.Text("时长" + (main.Settings.Sort == SortField.Duration ? arrow : ""), 12, "TextMuted");
            int i = 1;
            foreach (Song song in main.VisibleSongs) song.IndexText = (i++).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>重新生成列表（过滤 / 排序 / 扫描后调用）。</summary>
        public void RefreshItems()
        {
            List<Song> songs = main.VisibleSongs;
            int i = 1;
            foreach (Song song in songs)
            {
                song.IndexText = (i++).ToString(CultureInfo.InvariantCulture);
                song.IsCurrent = song == main.CurrentSong;
            }
            list.ItemsSource = null;
            list.ItemsSource = songs;

            double total = 0;
            foreach (Song song in songs) total += song.Duration;
            string text = songs.Count.ToString(CultureInfo.InvariantCulture) + " 首";
            if (main.Library.Count != songs.Count)
                text += "（共 " + main.Library.Count + " 首）";
            if (total > 0) text += " · " + FormatTotal(total);
            summary.Text = text;

            bool empty = songs.Count == 0;
            emptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            if (empty)
            {
                TextBlock hint = null;
                StackPanel panel = emptyState.Child as StackPanel;
                if (panel != null && panel.Children.Count > 2) hint = panel.Children[2] as TextBlock;
                if (hint != null)
                {
                    hint.Text = main.Library.Count == 0
                        ? "把歌曲（歌名 - 歌手.mp3）和同名 .lrc 歌词放进音乐文件夹即可\n当前目录：" + main.Settings.MusicDir
                        : "没有匹配「" + main.SearchText + "」的歌曲";
                }
            }
            UpdateHeaderArrows();
        }

        private static string FormatTotal(double seconds)
        {
            int total = (int)Math.Round(seconds);
            int h = total / 3600;
            int m = (total % 3600) / 60;
            if (h > 0) return h + " 小时 " + m + " 分";
            return Math.Max(m, 1) + " 分钟";
        }

        public void FocusList()
        {
            if (list.Items.Count > 0) list.Focus();
        }

        private void OnDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Song song = SongAt(e.OriginalSource as DependencyObject);
            if (song != null) main.PlaySong(song);
        }

        /// <summary>单击整行即播放；点右侧「＋」则加入播放队列。</summary>
        private void OnItemClick(object sender, MouseButtonEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject;
            if (FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) != null) return;

            ListBoxItem item = FindItem(source);
            if (item == null) return;
            Song song = item.DataContext as Song;
            if (song == null) return;

            if (FindAction(source, "add"))
            {
                main.Enqueue(song, false);
                return;
            }
            main.PlaySong(song);
        }

        private void PlayAll()
        {
            List<Song> songs = main.VisibleSongs;
            if (songs.Count == 0)
            {
                main.ShowToast("列表里还没有歌曲");
                return;
            }
            main.PlayFrom(songs, 0);
        }

        private void ShuffleAll()
        {
            List<Song> songs = main.VisibleSongs;
            if (songs.Count == 0)
            {
                main.ShowToast("列表里还没有歌曲");
                return;
            }
            main.SetMode(PlayMode.Shuffle);
            Random random = new Random();
            main.PlayFrom(songs, random.Next(songs.Count));
        }

        private Button IconTextButton(string icon, string text, string styleKey, RoutedEventHandler click)
        {
            Button button = new Button();
            button.Style = (Style)Application.Current.Resources[styleKey];
            Canvas iconCanvas = Icons.Create(icon, 15, styleKey == "PrimaryButton" ? "OnAccent" : "TextDim");
            TextBlock label = Ui.Text(text, 13, styleKey == "PrimaryButton" ? "OnAccent" : "Text");
            label.Margin = new Thickness(7, 0, 0, 0);
            button.Content = Ui.Row(0, iconCanvas, label);
            button.Click += click;
            return button;
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

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            Song song = list.SelectedItem as Song;
            if (song != null) main.PlaySong(song);
        }

        private void OnRightDown(object sender, MouseButtonEventArgs e)
        {
            ListBoxItem item = FindItem(e.OriginalSource as DependencyObject);
            if (item == null) return;
            if (!item.IsSelected)
            {
                list.SelectedItems.Clear();
                item.IsSelected = true;
            }
        }

        private void OnContextMenu(object sender, ContextMenuEventArgs e)
        {
            Song song = list.SelectedItem as Song;
            if (song == null)
            {
                e.Handled = true;
                return;
            }
            list.ContextMenu = BuildMenu(song);
        }

        private ContextMenu BuildMenu(Song song)
        {
            ContextMenu menu = new ContextMenu();
            menu.Items.Add(MenuItemFor("立即播放", delegate { main.PlaySong(song); }));
            menu.Items.Add(MenuItemFor("下一首播放", delegate { main.PlayNextInQueue(song); }));
            menu.Items.Add(MenuItemFor("添加到播放队列", delegate { main.Enqueue(song, false); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItemFor("在资源管理器中显示", delegate { main.RevealInExplorer(song); }));
            menu.Items.Add(MenuItemFor("从音乐库移除", delegate { main.RemoveFromLibrary(song); }));
            return menu;
        }

        private static MenuItem MenuItemFor(string text, RoutedEventHandler handler)
        {
            MenuItem item = new MenuItem();
            item.Header = text;
            item.Click += handler;
            return item;
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
