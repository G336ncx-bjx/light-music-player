package com.skylark.music;

import android.app.Activity;
import android.app.AlertDialog;
import android.app.ProgressDialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.res.Configuration;
import android.database.Cursor;
import android.graphics.Typeface;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.LayerDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.OpenableColumns;
import android.text.Editable;
import android.text.TextWatcher;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.view.inputmethod.InputMethodManager;
import android.widget.AdapterView;
import android.widget.BaseAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.HorizontalScrollView;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ListView;
import android.widget.ScrollView;
import android.widget.SeekBar;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;

/** 主界面：音乐库、播放队列、歌词、设置，共用一个底部播放条。 */

public abstract class PlayerScreen extends LibraryScreen {

    protected View buildTabs() {
        LinearLayout bar = row();
        bar.setBackgroundColor(cSurface);
        String[] names = { "音乐库", "队列", "歌词", "设置" };
        for (int i = 0; i < 4; i++) {
            LinearLayout item = column();
            item.setGravity(Gravity.CENTER_HORIZONTAL);
            item.setLayoutParams(new LinearLayout.LayoutParams(
                    0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
            TextView t = text(names[i], 14, cDim);
            t.setGravity(Gravity.CENTER);
            t.setPadding(0, dp(12), 0, dp(10));
            item.addView(t, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
            View line = new View(this);
            line.setBackgroundColor(cAccent);
            item.addView(line, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, dp(2)));
            final int index = i;
            item.setOnClickListener(new View.OnClickListener() {
                public void onClick(View v) {
                    selectTab(index);
                }
            });
            tabs[i] = t;
            tabLines[i] = line;
            bar.addView(item);
        }
        return bar;
    }


    /** 重建第一层的歌单（文件夹）列表。 */
    protected void refreshFolderRows() {
        if (libFolders == null) return;
        libFolders.removeAllViews();

        Map<String, Integer> counts = new java.util.LinkedHashMap<String, Integer>();
        for (int i = 0; i < Store.songs.size(); i++) {
            String name = Store.songs.get(i).playlist();
            if (name.length() == 0) continue;
            Integer have = counts.get(name);
            counts.put(name, have == null ? 1 : have + 1);
        }
        // 歌单来源＝扫描到的文件夹（空歌单也在）+ 歌曲里出现过的歌单
        Set<String> all = new java.util.LinkedHashSet<String>();
        for (int i = 0; i < Store.playlists.size(); i++) {
            String name = Store.playlists.get(i);
            if (name.length() > 0) all.add(name);
        }
        all.addAll(counts.keySet());
        List<String> names = new ArrayList<String>(all);
        Collections.sort(names, new Comparator<String>() {
            public int compare(String a, String b) {
                return a.compareToIgnoreCase(b);
            }
        });

        libFolders.addView(folderRow("全部歌曲", distinctCount() + " 首", "", false));
        for (int i = 0; i < names.size(); i++) {
            String name = names.get(i);
            Integer count = counts.get(name);
            libFolders.addView(folderRow(name, (count == null ? 0 : count) + " 首", name, false));
        }
        libFolders.addView(folderRow("＋ 新建歌单", "云盘上会新建一个同名文件夹", null, true));
    }


    /** 一行「文件夹」：图标 + 名字 + 说明 + 右箭头；value 为 null 表示「新建歌单」那一行。 */
    protected View folderRow(final String label, String subtitle, final String value, boolean create) {
        LinearLayout r = row();
        r.setBackground(round(cSurface, 12));
        r.setPadding(dp(12), dp(14), dp(12), dp(14));
        LinearLayout.LayoutParams rowP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        rowP.bottomMargin = dp(8);
        r.setLayoutParams(rowP);

        ImageView icon = new ImageView(this);
        icon.setImageResource(R.drawable.ic_folder);
        r.addView(icon, new LinearLayout.LayoutParams(dp(22), dp(22)));

        LinearLayout middle = column();
        LinearLayout.LayoutParams middleP = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        middleP.leftMargin = dp(12);
        TextView name = text(label, 15, create ? cAccent : cText);
        name.setSingleLine(true);
        name.setEllipsize(android.text.TextUtils.TruncateAt.END);
        middle.addView(name);
        if (subtitle != null) middle.addView(text(subtitle, 12, cDim));
        r.addView(middle, middleP);

        if (!create) {
            ImageView chevron = new ImageView(this);
            chevron.setImageResource(R.drawable.ic_chevron);
            r.addView(chevron, new LinearLayout.LayoutParams(dp(16), dp(16)));
        }

        r.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                if (value == null) newPlaylist();
                else openPlaylist(value);
            }
        });
        if (value != null && value.length() > 0) {
            r.setOnLongClickListener(new View.OnLongClickListener() {
                public boolean onLongClick(View v) {
                    showPlaylistMenu(value);
                    return true;
                }
            });
        }
        return r;
    }


    /** 进某个歌单看歌；value 为空串＝「全部歌曲」。 */
    protected void openPlaylist(String value) {
        playlistFilter = value;
        inPlaylist = true;
        if (libHome != null) libHome.setVisibility(View.GONE);
        if (libListPanel != null) libListPanel.setVisibility(View.VISIBLE);
        if (libTitle != null) libTitle.setText(value.length() == 0 ? "全部歌曲" : value);
        if (search != null) search.setText("");
        setLibSelecting(false);
        applyFilter();
    }


    /** 回到第一层的歌单列表。 */
    protected void backToFolders() {
        setLibSelecting(false);
        inPlaylist = false;
        playlistFilter = "";
        if (libListPanel != null) libListPanel.setVisibility(View.GONE);
        if (libHome != null) libHome.setVisibility(View.VISIBLE);
        if (search != null) search.setText("");
        refreshFolderRows();
    }


    protected void showPlaylistMenu(final String name) {
        final String[] items = { "重命名歌单", "删除歌单" };
        new AlertDialog.Builder(this)
                .setTitle("歌单：" + name)
                .setItems(items, new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        if (which == 0) renamePlaylist(name);
                        else confirmDeletePlaylist(name);
                    }
                })
                .show();
    }


    protected void newPlaylist() {
        final EditText field = input("歌单名", "");
        new AlertDialog.Builder(this)
                .setTitle("新建歌单")
                .setMessage("云盘上会新建一个同名文件夹，上传的歌就放进这个文件夹里。")
                .setView(field)
                .setNegativeButton("取消", null)
                .setPositiveButton("创建", new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        final String name = field.getText().toString().trim();
                        if (Cloud.badName(name)) {
                            toast("这个名字不能用在文件夹上");
                            return;
                        }
                        runCloud("新建歌单", new CloudTask() {
                            public void run(String endpoint) throws Exception {
                                Cloud.ensureDir(endpoint, "/" + name, getCacheDir());
                            }
                        });
                    }
                })
                .show();
    }


    protected void renamePlaylist(final String name) {
        final EditText field = input("新的歌单名", name);
        new AlertDialog.Builder(this)
                .setTitle("重命名歌单")
                .setMessage("会把歌单文件夹里的歌整体搬到新文件夹（服务端直接搬，不重新上传）。")
                .setView(field)
                .setNegativeButton("取消", null)
                .setPositiveButton("改名", new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        final String target = field.getText().toString().trim();
                        if (Cloud.badName(target)) {
                            toast("这个名字不能用在文件夹上");
                            return;
                        }
                        if (target.equals(name)) return;
                        runCloud("重命名歌单", new CloudTask() {
                            public void run(String endpoint) throws Exception {
                                Cloud.ensureDir(endpoint, "/" + target, getCacheDir());
                                List<Cloud.Entry> entries = listOfPlaylist(endpoint, name);
                                List<String> moving = new ArrayList<String>();
                                for (int i = 0; i < entries.size(); i++) {
                                    if (!entries.get(i).dir) moving.add(entries.get(i).name);
                                }
                                Cloud.moveItems(endpoint, "/" + name, moving, "/" + target);
                                Cloud.delete(endpoint, "/" + name);
                            }
                        });
                        if (playlistFilter.equals(name)) playlistFilter = target;
                    }
                })
                .show();
    }


    protected void confirmDeletePlaylist(final String name) {
        new AlertDialog.Builder(this)
                .setTitle("删除歌单「" + name + "」？")
                .setMessage("歌单就是云盘上的一个文件夹，删除会把里面的音频和歌词一起删掉，\n"
                        + "恢复只能自己去云盘的历史记录里找。")
                .setNegativeButton("取消", null)
                .setPositiveButton("删除", new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        runCloud("删除歌单", new CloudTask() {
                            public void run(String endpoint) throws Exception {
                                Cloud.delete(endpoint, "/" + name);
                            }
                        });
                        if (playlistFilter.equals(name)) playlistFilter = "";
                    }
                })
                .show();
    }


    protected void runCloud(final String label, final CloudTask task) {
        final String endpoint = Store.endpoint;
        if (endpoint.length() == 0) {
            toast("先到「设置」里连接云盘");
            return;
        }
        toast("正在" + label + "…");
        new Thread(new Runnable() {
            public void run() {
                try {
                    task.run(endpoint);
                    toastAsync(label + "完成");
                    ui.post(new Runnable() {
                        public void run() {
                            startScan(false);
                        }
                    });
                } catch (final Exception e) {
                    toastAsync(label + "失败：" + Util.shorten(e.getMessage()));
                }
            }
        }).start();
    }


    protected void copyToPlaylist(final List<Song> songs, final String target) {
        runCloud("加入歌单", new CloudTask() {
            public void run(String endpoint) throws Exception {
                Cloud.ensureDir(endpoint, "/" + target, getCacheDir());
                for (int i = 0; i < songs.size(); i++) {
                    Song song = songs.get(i);
                    String dir = song.playlist();
                    List<String> items = new ArrayList<String>();
                    items.add(song.fileName);
                    if (song.hasLyrics()) items.add(lrcName(song));
                    Cloud.copyItems(endpoint, dir.length() == 0 ? "/" : "/" + dir, items, "/" + target);
                }
            }
        });
        cancelSelection();
    }


    /** 从云盘删除选中的歌（连同同名歌词），会二次确认。 */
    protected void confirmDeleteMany(final List<Song> songs) {
        if (songs == null || songs.isEmpty()) {
            toast("先选中歌曲");
            return;
        }
        if (!Cloud.canDelete(Store.endpoint)) {
            toast("删除云端文件需要「资料库 API 令牌」，到设置里填上即可");
            return;
        }
        new AlertDialog.Builder(this)
                .setTitle("从云盘删除 " + songs.size() + " 首？")
                .setMessage("这是直接从云盘上删，连同同名歌词一起删掉；\n"
                        + "删了就找不回来了，只能自己去云盘的历史记录里翻。")
                .setNegativeButton("取消", null)
                .setPositiveButton("删除", new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        final List<String> paths = new ArrayList<String>();
                        for (int i = 0; i < songs.size(); i++) {
                            paths.add(songs.get(i).cloudPath);
                            if (songs.get(i).hasLyrics()) paths.add(songs.get(i).lyricPath);
                        }
                        runCloud("删除 " + songs.size() + " 首", new CloudTask() {
                            public void run(String endpoint) throws Exception {
                                Cloud.deleteAll(endpoint, paths);
                            }
                        });
                        cancelSelection();
                    }
                })
                .show();
    }


    protected void removeSelectedFromQueue(List<Song> songs) {
        if (songs == null || songs.isEmpty()) {
            toast("先选中歌曲");
            return;
        }
        PlayerService service = PlayerService.instance;
        if (service != null) service.removeMany(songs);
        else {
            Store.queue.removeAll(songs);
            Store.saveQueue(this);
        }
        cancelSelection();
        refreshQueue();
        refreshNowPlaying();
    }


    protected void showSortDialog() {
        final String[] names = { "歌名", "歌手", "时长", "云盘顺序", "重新扫描云盘" };
        new AlertDialog.Builder(this)
                .setTitle("排序方式")
                .setItems(names, new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        if (which == 4) {
                            startScan(false);
                            return;
                        }
                        Prefs.setSortMode(PlayerScreen.this, which);
                        updateSortButton();
                        applyFilter();
                    }
                })
                .show();
    }


    protected void applyFilter() {
        String query = search == null ? "" : search.getText().toString().trim().toLowerCase(Locale.ROOT);
        shown.clear();
        Set<String> seen = new java.util.HashSet<String>();
        for (int i = 0; i < Store.songs.size(); i++) {
            Song s = Store.songs.get(i);
            if (playlistFilter.length() > 0 && !playlistFilter.equals(s.playlist())) continue;
            if (query.length() > 0) {
                String hay = (s.title + " " + s.artist + " " + s.fileName).toLowerCase(Locale.ROOT);
                if (!hay.contains(query)) continue;
            }
            // 「全部歌曲」里，同一首歌出现在多个歌单时只显示一次（进具体歌单能看到那份拷贝）
            if (playlistFilter.length() == 0
                    && !seen.add((s.title + "\u0001" + s.artist).toLowerCase(Locale.ROOT))) continue;
            shown.add(s);
        }
        sortSongs(shown);
        libAdapter.setData(shown);
        refreshFolderRows();
        if (libEmpty != null) {
            boolean empty = shown.isEmpty();
            libEmpty.setVisibility(empty ? View.VISIBLE : View.GONE);
            libList.setVisibility(empty ? View.GONE : View.VISIBLE);
            if (empty) {
                libEmpty.setText(Store.songs.isEmpty()
                        ? "还没有歌曲。先在「设置」里连接云盘，再回来点「刷新」。"
                        : playlistFilter.length() == 0
                            ? "还没有歌。"
                            : "这个歌单里还没有歌。长按列表里的歌进入多选，可以「加入歌单」。");
            }
        }
        updateSortButton();
        updateLibraryInfo(null);
    }


    protected void hideFromLibrary(Song song) {
        Set<String> hidden = Store.hiddenSet(this);
        hidden.add(song.cloudPath);
        Store.setHiddenSet(this, hidden);
        for (int i = Store.songs.size() - 1; i >= 0; i--) {
            if (Store.songs.get(i).cloudPath.equals(song.cloudPath)) Store.songs.remove(i);
        }
        applyFilter();
        toast("已从音乐库移除（可在设置里恢复）");
    }


    protected void confirmCloudDelete(final Song song) {
        if (!Cloud.canDelete(Store.endpoint)) {
            toast("删除云端文件需要「资料库 API 令牌」，到设置里填上即可");
            return;
        }
        new AlertDialog.Builder(this)
                .setTitle("从云盘删除？")
                .setMessage(song.fileName + "\n\n同名歌词会一起删除，删掉的文件会进云盘回收站。")
                .setNegativeButton("取消", null)
                .setPositiveButton("删除", new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        final String endpoint = Store.endpoint;
                        new Thread(new Runnable() {
                            public void run() {
                                try {
                                    Cloud.delete(endpoint, song.cloudPath);
                                    if (song.hasLyrics()) {
                                        try {
                                            Cloud.delete(endpoint, song.lyricPath);
                                        } catch (Exception ignored) {
                                            // 歌词可能不存在，忽略
                                        }
                                    }
                                    toastAsync("已删除：" + song.title);
                                    ui.post(new Runnable() {
                                        public void run() {
                                            startScan(false);
                                        }
                                    });
                                } catch (final Exception e) {
                                    toastAsync("删除失败：" + Util.shorten(e.getMessage()));
                                }
                            }
                        }).start();
                    }
                })
                .show();
    }


    protected void confirmClearQueue() {
        if (Store.queue.isEmpty()) {
            toast("队列已经是空的");
            return;
        }
        new AlertDialog.Builder(this)
                .setTitle("清空播放队列？")
                .setMessage("会停止当前播放并清空整个队列。")
                .setNegativeButton("取消", null)
                .setPositiveButton("清空", new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        PlayerService service = PlayerService.instance;
                        if (service != null) service.clearQueue();
                        else {
                            Store.queue.clear();
                            Store.index = -1;
                            Store.saveQueue(PlayerScreen.this);
                        }
                        refreshQueue();
                        refreshNowPlaying();
                    }
                })
                .show();
    }


    // ---------------------------------------------------------------- 歌词页

    protected View buildLyricsPage() {
        LinearLayout page = column();
        page.setPadding(dp(12), dp(12), dp(12), 0);

        LinearLayout head = card();
        lyricTitle = text("还没有播放歌曲", 17, cText);
        lyricTitle.setTypeface(null, Typeface.BOLD);
        lyricTitle.setSingleLine(true);
        lyricTitle.setEllipsize(android.text.TextUtils.TruncateAt.END);
        head.addView(lyricTitle);
        lyricArtist = text("", 12, cDim);
        lyricArtist.setPadding(0, dp(4), 0, dp(10));
        head.addView(lyricArtist);

        LinearLayout buttons = row();
        buttons.addView(button("−0.5 秒", false, new View.OnClickListener() {
            public void onClick(View v) {
                nudgeOffset(-0.5);
            }
        }));
        buttons.addView(button("+0.5 秒", false, new View.OnClickListener() {
            public void onClick(View v) {
                nudgeOffset(0.5);
            }
        }));
        buttons.addView(button("重新获取歌词", false, new View.OnClickListener() {
            public void onClick(View v) {
                refreshLyrics(true);
            }
        }));
        head.addView(buttons);

        LinearLayout labels = row();
        offsetLabel = text("偏移 0.0 秒", 12, cAccent);
        labels.addView(offsetLabel, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        lyricState = text("", 12, cDim);
        labels.addView(lyricState);
        LinearLayout.LayoutParams labelsP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        labelsP.topMargin = dp(8);
        labels.setLayoutParams(labelsP);
        head.addView(labels);
        page.addView(head);

        lyricScroll = new ScrollView(this);
        lyricScroll.setBackground(round(cSurface, 14));
        // 歌词滚动时不要冒出滚动条（观感很脏，手机上也没必要）
        lyricScroll.setVerticalScrollBarEnabled(false);
        lyricScroll.setHorizontalScrollBarEnabled(false);
        lyricScroll.setLayoutParams(new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        lyricScroll.setOnTouchListener(new View.OnTouchListener() {
            public boolean onTouch(View v, MotionEvent event) {
                if (event.getAction() == MotionEvent.ACTION_DOWN
                        || event.getAction() == MotionEvent.ACTION_UP
                        || event.getAction() == MotionEvent.ACTION_CANCEL) {
                    autoScrolling = false;   // 用户接管，动画作废
                    manualScrollUntil = System.currentTimeMillis() + 4000;
                }
                return false;
            }
        });
        lyricBox = column();
        lyricBox.setPadding(dp(16), dp(120), dp(16), dp(120));
        lyricScroll.addView(lyricBox, new android.widget.FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        // 歌词区 + 浮在上面的「回到当前歌词」按钮
        android.widget.FrameLayout lyricWrap = new android.widget.FrameLayout(this);
        lyricWrap.setLayoutParams(new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        lyricScroll.setLayoutParams(new android.widget.FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        lyricWrap.addView(lyricScroll);

        backToCurrent = text("回到当前歌词", 12, cText);
        backToCurrent.setPadding(dp(14), dp(7), dp(14), dp(7));
        backToCurrent.setBackground(round(cAlt, 18));
        backToCurrent.setVisibility(View.GONE);
        backToCurrent.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                manualScrollUntil = 0;
                backToCurrent.setVisibility(View.GONE);
                scrollToCurrentLine(true);
            }
        });
        backToCurrent.setClickable(true);
        backToCurrent.setFocusable(true);
        backToCurrent.bringToFront();
        android.widget.FrameLayout.LayoutParams backP = new android.widget.FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        backP.gravity = Gravity.BOTTOM | Gravity.CENTER_HORIZONTAL;
        backP.bottomMargin = dp(14);
        lyricWrap.addView(backToCurrent, backP);

        page.addView(lyricWrap);
        return page;
    }


    protected void saveEndpoint() {
        Prefs.setLink(this, linkInput.getText().toString());
        Prefs.setToken(this, tokenInput.getText().toString());
        Store.endpoint = Prefs.endpoint(this);
        Store.skippedUnsupported = 0;
        if (Store.endpoint.length() == 0) {
            connInfo.setText("分享链接和令牌都是空的，先填一个。");
            return;
        }
        connInfo.setText("已保存，正在读取云盘…");
        startScan(true);
    }


    // ---------------------------------------------------------------- 扫描云盘

    protected void startScan(final boolean goToLibrary) {
        final String endpoint = Store.endpoint;
        if (endpoint.length() == 0) {
            updateLibraryInfo(null);
            updateConnInfo();
            return;
        }
        final int token = ++scanToken;
        Store.scanning = true;
        updateLibraryInfo("正在读取云端目录…");
        if (goToLibrary) selectTab(0);
        new Thread(new Runnable() {
            public void run() {
                try {
                    List<Cloud.Entry> entries = Cloud.listAll(endpoint);
                    final List<Song> found = new ArrayList<Song>();
                    final List<String> folders = new ArrayList<String>();
                    Map<String, String> lyrics = new HashMap<String, String>();
                    Map<String, String> lyricStamps = new HashMap<String, String>();
                    for (int i = 0; i < entries.size(); i++) {
                        Cloud.Entry entry = entries.get(i);
                        if (entry.dir) {
                            // 歌单＝文件夹：空文件夹也要记下来，建完立刻能看到
                            String p = entry.path.startsWith("/") ? entry.path.substring(1) : entry.path;
                            int slash = p.indexOf('/');
                            String top = slash > 0 ? p.substring(0, slash) : p;
                            if (top.length() > 0 && !folders.contains(top)) folders.add(top);
                            continue;
                        }
                        String ext = extOf(entry.name);
                        if ("lrc".equals(ext)) {
                            lyrics.put(baseOf(entry.name).toLowerCase(Locale.ROOT), entry.path);
                            lyricStamps.put(baseOf(entry.name).toLowerCase(Locale.ROOT), entry.modified);
                        }
                    }
                    int skipped = 0;
                    Set<String> hidden = Store.hiddenSet(PlayerScreen.this);
                    for (int i = 0; i < entries.size(); i++) {
                        Cloud.Entry entry = entries.get(i);
                        if (entry.dir) continue;
                        String ext = extOf(entry.name);
                        if (isIgnored(ext)) {
                            skipped++;
                            continue;
                        }
                        if (!isAudio(ext)) continue;
                        if (hidden.contains(entry.path)) continue;
                        Song song = new Song();
                        song.cloudPath = entry.path;
                        song.fileName = entry.name;
                        String[] parts = Util.parseSongName(baseOf(entry.name));
                        song.title = parts[0];
                        song.artist = parts[1];
                        song.size = entry.size;
                        String lyric = lyrics.get(baseOf(entry.name).toLowerCase(Locale.ROOT));
                        song.lyricPath = lyric == null ? "" : lyric;
                        String stamp = lyricStamps.get(baseOf(entry.name).toLowerCase(Locale.ROOT));
                        song.lyricModified = stamp == null ? "" : stamp;
                        song.duration = Store.durationOf(PlayerScreen.this, song);
                        found.add(song);
                    }
                    final int skippedCount = skipped;
                    final String repo = Cloud.isToken(endpoint) ? Cloud.repoName(endpoint) : "";
                    ui.post(new Runnable() {
                        public void run() {
                            if (token != scanToken) return;
                            Store.songs.clear();
                            Store.songs.addAll(found);
                            Store.playlists.clear();
                            Store.playlists.addAll(folders);
                            Store.skippedUnsupported = skippedCount;
                            Store.scanning = false;
                            Store.repoLabel = repo;
                            Store.status = "";
                            applyFilter();
                            updateConnInfo();
                            updateLibraryInfo(null);
                            refreshQueue();
                        }
                    });
                    probeDurations(token);
                } catch (final Exception e) {
                    ui.post(new Runnable() {
                        public void run() {
                            if (token != scanToken) return;
                            Store.scanning = false;
                            updateLibraryInfo("读取云盘失败：" + Util.shorten(e.getMessage()));
                            updateConnInfo();
                        }
                    });
                }
            }
        }).start();
    }


    // ---------------------------------------------------------------- 上传

    protected void pickFiles() {
        if (Store.endpoint.length() == 0) {
            toast("先在「设置」里连接云盘，才能上传");
            selectTab(3);
            return;
        }
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("*/*");
        intent.putExtra(Intent.EXTRA_MIME_TYPES, new String[] { "audio/*", "application/octet-stream" });
        intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        try {
            startActivityForResult(intent, REQ_PICK);
        } catch (Exception e) {
            toast("打不开文件选择器");
        }
    }

}
