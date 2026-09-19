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

public abstract class LibraryScreen extends AppShell {

    // ---------------------------------------------------------------- 调色板与控件工厂

    protected void palette() {
        if (dark) {
            cBg = 0xFF0E1014; cSurface = 0xFF191C22; cAlt = 0xFF232833;
            cText = 0xFFF1F4F9; cDim = 0xFF8B94A7; cAccent = 0xFF5B9DFF;
            cDivider = 0xFF2A2F3A; cDanger = 0xFFFF7A7A; cTrack = 0xFF2C313B;
        } else {
            cBg = 0xFFF4F6F9; cSurface = 0xFFFFFFFF; cAlt = 0xFFEDF1F7;
            cText = 0xFF15181D; cDim = 0xFF6B7280; cAccent = 0xFF2F6FED;
            cDivider = 0xFFE1E6EE; cDanger = 0xFFD24444; cTrack = 0xFFDCE2EC;
        }
        getWindow().setBackgroundDrawable(new android.graphics.drawable.ColorDrawable(cBg));
        getWindow().setStatusBarColor(cSurface);
        getWindow().setNavigationBarColor(cSurface);
        if (Build.VERSION.SDK_INT >= 23) {
            View decor = getWindow().getDecorView();
            int flags = decor.getSystemUiVisibility();
            if (dark) flags &= ~View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR;
            else flags |= View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR;
            decor.setSystemUiVisibility(flags);
        }
    }


    protected void selectTab(int index) {
        if (index < 0 || index > 3) index = 0;
        tabIndex = index;
        lastTab = index;
        for (int i = 0; i < 4; i++) {
            tabs[i].setTextColor(i == index ? cAccent : cDim);
            tabs[i].setTypeface(null, i == index ? Typeface.BOLD : Typeface.NORMAL);
            tabLines[i].setVisibility(i == index ? View.VISIBLE : View.INVISIBLE);
            pages[i].setVisibility(i == index ? View.VISIBLE : View.GONE);
        }
        if (index == 2) refreshLyrics(false);
        if (index == 3) refreshSettings();
        hideKeyboard();
    }


    /** 按「歌名 + 歌手」去重后的曲目数：一首歌放进两个歌单只算一首。 */
    protected static int distinctCount() {
        Set<String> seen = new java.util.HashSet<String>();
        for (int i = 0; i < Store.songs.size(); i++) {
            Song s = Store.songs.get(i);
            seen.add((s.title + "\u0001" + s.artist).toLowerCase(Locale.ROOT));
        }
        return seen.size();
    }


    /**
     * 进 / 出多选：把「标题行 + 搜索行」换成「已选 + 全选」和三个批量操作，
     * 行数不变，所以进多选不会少看几首歌。
     */
    protected void setLibSelecting(boolean on) {
        if (libAdapter == null) return;
        libAdapter.setSelecting(on);
        if (libHeadRow != null) libHeadRow.setVisibility(on ? View.GONE : View.VISIBLE);
        if (libSearchRow != null) libSearchRow.setVisibility(on ? View.GONE : View.VISIBLE);
        if (libSelHead != null) libSelHead.setVisibility(on ? View.VISIBLE : View.GONE);
        if (libSelActions != null) libSelActions.setVisibility(on ? View.VISIBLE : View.GONE);
        updateSelectionBars();
    }


    /** 列出某个歌单文件夹里的全部文件（重命名时用）。 */
    protected List<Cloud.Entry> listOfPlaylist(String endpoint, String name) throws Exception {
        List<Cloud.Entry> all = Cloud.listAll(endpoint);
        List<Cloud.Entry> out = new ArrayList<Cloud.Entry>();
        for (int i = 0; i < all.size(); i++) {
            if (name.equals(Cloud.playlistOf(all.get(i).path))) out.add(all.get(i));
        }
        return out;
    }


    /** 播放队列的多选：同样是「换个内容」而不是「多加两行」。 */
    protected void setQueueSelecting(boolean on) {
        if (queueAdapter == null) return;
        queueAdapter.setSelecting(on);
        if (queueHeadRow != null) queueHeadRow.setVisibility(on ? View.GONE : View.VISIBLE);
        if (queueTipsRow != null) queueTipsRow.setVisibility(on ? View.GONE : View.VISIBLE);
        if (queueSelHead != null) queueSelHead.setVisibility(on ? View.VISIBLE : View.GONE);
        if (queueSelActions != null) queueSelActions.setVisibility(on ? View.VISIBLE : View.GONE);
        updateSelectionBars();
    }


    /** 全选 / 取消全选：已经全勾上了就再点一次全部取消。 */
    protected void toggleSelectAll(SongAdapter adapter, Button button) {
        if (adapter == null) return;
        if (adapter.allPicked()) adapter.clearPicked();
        else adapter.selectAll();
        updateSelectionBars();
    }


    protected void cancelSelection() {
        if (libAdapter != null && libAdapter.isSelecting()) setLibSelecting(false);
        if (queueAdapter != null && queueAdapter.isSelecting()) setQueueSelecting(false);
    }


    /** 下载选中项：先问下音频 / 歌词 / 两者，再后台逐个下到系统「下载」目录。 */
    protected void askDownloadScope(final List<Song> songs) {
        if (songs == null || songs.isEmpty()) {
            toast("先选中歌曲");
            return;
        }
        final String[] items = { "音频 + 歌词", "只下音频", "只下歌词" };
        new AlertDialog.Builder(this)
                .setTitle("下载 " + songs.size() + " 首")
                .setItems(items, new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        startDownload(songs, which != 2, which != 1);
                    }
                })
                .show();
    }


    protected void startDownload(final List<Song> songs, final boolean audio, final boolean lyrics) {
        if (!ensureStoragePermission()) return;
        final String endpoint = Store.endpoint;
        toast("开始下载 " + songs.size() + " 首到 " + Saver.folderHint());
        new Thread(new Runnable() {
            public void run() {
                int ok = 0;
                int failed = 0;
                int noLyric = 0;
                for (int i = 0; i < songs.size(); i++) {
                    Song song = songs.get(i);
                    try {
                        if (audio) {
                            File temp = new File(getCacheDir(), "dl-audio.tmp");
                            Cloud.download(endpoint, song.cloudPath, temp, null);
                            Saver.save(LibraryScreen.this, temp, song.fileName, mimeOf(song.fileName));
                            temp.delete();
                        }
                        if (lyrics) {
                            if (song.hasLyrics()) {
                                File temp = new File(getCacheDir(), "dl-lyric.tmp");
                                Cloud.download(endpoint, song.lyricPath, temp, null);
                                Saver.save(LibraryScreen.this, temp, lrcName(song), "text/plain");
                                temp.delete();
                            } else {
                                noLyric++;
                            }
                        }
                        ok++;
                    } catch (Exception e) {
                        failed++;
                    }
                }
                final int okCount = ok;
                final int badCount = failed;
                final int skipCount = noLyric;
                ui.post(new Runnable() {
                    public void run() {
                        cancelSelection();
                        String message = "下载完成：成功 " + okCount + " 首";
                        if (badCount > 0) message += "，失败 " + badCount + " 首";
                        if (lyrics && skipCount > 0) message += "（" + skipCount + " 首没有歌词）";
                        toast(message + "\n位置：" + Saver.folderHint());
                    }
                });
            }
        }).start();
    }


    /** 老系统写公共下载目录需要存储权限；Android 10 及以上走 MediaStore，不需要。 */
    protected boolean ensureStoragePermission() {
        if (Build.VERSION.SDK_INT >= 29) return true;
        if (checkSelfPermission(android.Manifest.permission.WRITE_EXTERNAL_STORAGE)
                == android.content.pm.PackageManager.PERMISSION_GRANTED) return true;
        requestPermissions(new String[] { android.Manifest.permission.WRITE_EXTERNAL_STORAGE }, REQ_STORAGE);
        toast("给云雀「存储」权限后，再点一次下载");
        return false;
    }


    protected List<String> playlistNames() {
        // 跟音乐库首页一样：扫描到的文件夹（空歌单也在）+ 歌曲里出现过的歌单
        Set<String> all = new java.util.LinkedHashSet<String>();
        for (int i = 0; i < Store.playlists.size(); i++) {
            String folder = Store.playlists.get(i);
            if (folder.length() > 0) all.add(folder);
        }
        for (int i = 0; i < Store.songs.size(); i++) {
            String name = Store.songs.get(i).playlist();
            if (name.length() > 0) all.add(name);
        }
        List<String> names = new ArrayList<String>(all);
        Collections.sort(names, new Comparator<String>() {
            public int compare(String a, String b) {
                return a.compareToIgnoreCase(b);
            }
        });
        return names;
    }


    protected void sortSongs(List<Song> list) {
        final int mode = Prefs.sortMode(this);
        if (mode == 3) return;
        Collections.sort(list, new Comparator<Song>() {
            public int compare(Song a, Song b) {
                if (mode == 1) return safe(a.artist).compareToIgnoreCase(safe(b.artist));
                if (mode == 2) return Double.compare(b.duration, a.duration);
                return safe(a.title).compareToIgnoreCase(safe(b.title));
            }
        });
    }


    protected void playFromLibrary(int position) {
        if (position < 0 || position >= shown.size()) return;
        PlayerService service = PlayerService.instance;
        if (service == null) {
            toast("播放服务还在启动，稍后再点一次");
            startPlayerService();
            return;
        }
        Song song = shown.get(position);
        int at = indexInQueue(song.cloudPath);
        if (at < 0) {
            Store.queue.add(song);
            at = Store.queue.size() - 1;
        }
        service.playAt(at);
        refreshQueue();
    }


    /**
     * 播放全部：按「当前播放模式」来播。
     * 下面的模式选随机 → 先把整张列表打乱一次再顺序播；其它模式就按列表原顺序播。
     * 这样「播放顺序」只有一个地方说了算（底部那个模式），不会再出现两处随机互相打架。
     */
    protected void playAll() {
        List<Song> list = new ArrayList<Song>(shown);
        if (list.isEmpty()) {
            toast("音乐库是空的");
            return;
        }
        if (Store.mode == 3) Collections.shuffle(list);
        PlayerService service = PlayerService.instance;
        if (service == null) {
            startPlayerService();
            toast("播放服务还在启动，稍后再点一次");
            return;
        }
        service.playQueue(list, 0);
        refreshQueue();
    }


    protected int indexInQueue(String cloudPath) {
        for (int i = 0; i < Store.queue.size(); i++) {
            if (Store.queue.get(i).cloudPath.equals(cloudPath)) return i;
        }
        return -1;
    }


    protected void enqueueToService(Song song, boolean next) {
        PlayerService service = PlayerService.instance;
        if (service == null) {
            startPlayerService();
            toast("播放服务还在启动，稍后再试");
            return;
        }
        service.enqueue(song, next);
        Store.status = next ? "已设为下一首播放" : "已加到队列末尾";
        refreshQueue();
    }


    protected void showQueueMenu(final int position) {
        if (position < 0 || position >= Store.queue.size()) return;
        final Song song = Store.queue.get(position);
        final String[] items = { "立即播放", "移除", "上移", "下移", "设为下一首" };
        new AlertDialog.Builder(this)
                .setTitle(song.title + " · " + song.artistText())
                .setItems(items, new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        PlayerService service = PlayerService.instance;
                        if (service == null) {
                            toast("播放服务还在启动，稍后再试");
                            return;
                        }
                        if (which == 0) {
                            service.playAt(position);
                        } else if (which == 1) {
                            service.removeAt(position);
                        } else if (which == 2) {
                            service.move(position, position - 1);
                        } else if (which == 3) {
                            service.move(position, position + 1);
                        } else {
                            int target = Math.min(Store.index + 1, Store.queue.size() - 1);
                            if (target != position) service.move(position, target);
                        }
                        refreshQueue();
                    }
                })
                .show();
    }


    protected void nudgeOffset(double delta) {
        Song song = Store.current();
        if (song == null) {
            toast("还没有播放歌曲");
            return;
        }
        double value = Store.lyricOffset(this, song) + delta;
        Store.setLyricOffset(this, song, value);
        updateOffsetLabel(song);
        lyricIndex = -1;
        applyLyricHighlight(PlayerService.instance == null ? 0 : PlayerService.instance.position());
    }


    protected void refreshSettings() {
        if (hiddenInfo == null) return;
        Set<String> hidden = Store.hiddenSet(this);
        hiddenInfo.setText(hidden.isEmpty() ? "没有隐藏的歌曲。"
                : "音乐库里隐藏了 " + hidden.size() + " 首（文件还在云盘上）。");
        updateCacheInfo();
        updateCacheButtons();
        updateConnInfo();
        updateThemeButtons();
    }


    protected void applyCacheMode(int mode) {
        Prefs.setCacheMode(this, mode);
        Store.cacheMode = mode;
        updateCacheButtons();
        toast(mode == Store.CACHE_ALL
                ? "已开启缓存：听过的歌都会留在本机"
                : "已关闭缓存：只在线播放，硬盘上不留音频文件");
        PlayerService service = PlayerService.instance;
        if (service != null) service.pruneNow();
        updateCacheInfo();
    }


    protected void updateCacheButtons() {
        boolean caching = Store.cacheAll();
        if (cacheSwitch != null) cacheSwitch.setChecked(caching);
        if (cacheModeHint != null) {
            cacheModeHint.setText(caching
                    ? "已开启：听过的歌都留在本机，可以离线播放；占的空间会随听过的歌增加，"
                            + "随时可以点「清除缓存」清掉。"
                    : "默认关闭：本机只留正在听的那一首和下一首（切歌几乎不用等），"
                            + "更早的会自动删掉，占的空间很小。");
        }
    }


    protected void updateCacheInfo() {
        if (cacheInfo == null) return;
        long size = Store.cacheSize(this);
        String text = String.format(Locale.ROOT, "缓存占用 %.1f MB · %d 个文件",
                size / 1024.0 / 1024.0, Store.cacheCount(this));
        cacheInfo.setText(text);
    }


    protected void updateConnInfo() {
        if (connInfo == null) return;
        String endpoint = Store.endpoint;
        if (endpoint.length() == 0) {
            connInfo.setText("还没连接云盘。");
            return;
        }
        String mode = Cloud.isToken(endpoint) ? "资料库令牌" : "分享链接";
        String label = Store.repoLabel.length() > 0 ? " · " + Store.repoLabel : "";
        connInfo.setText("当前：" + mode + label + " · 曲库 " + Store.songs.size() + " 首");
    }


    protected void updateThemeButtons() {
        int mode = Prefs.themeMode(this);
        for (int i = 0; i < 3; i++) {
            if (themeButtons[i] == null) continue;
            boolean active = i == mode;
            themeButtons[i].setTextColor(active ? 0xFFFFFFFF : cText);
            themeButtons[i].setBackground(round(active ? cAccent : cAlt, 10));
        }
    }


    protected void applyThemeMode(int mode) {
        if (Prefs.themeMode(this) == mode) return;
        Prefs.setThemeMode(this, mode);
        if (Prefs.isDark(this) != dark) recreate();
        else updateThemeButtons();
    }


    protected void pasteFromClipboard() {
        ClipboardManager manager = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
        if (manager == null || manager.getPrimaryClip() == null
                || manager.getPrimaryClip().getItemCount() == 0) {
            toast("剪贴板是空的");
            return;
        }
        CharSequence value = manager.getPrimaryClip().getItemAt(0).coerceToText(this);
        String text = value == null ? "" : value.toString().trim();
        if (text.length() == 0) {
            toast("剪贴板是空的");
            return;
        }
        if (text.length() == 40 && Cloud.isToken(text) && text.indexOf('/') < 0) {
            tokenInput.setText(text);
            toast("已填入 API 令牌");
        } else {
            linkInput.setText(text);
            toast("已填入分享链接");
        }
    }


    protected void testConnection() {
        final String link = linkInput.getText().toString().trim();
        final String token = tokenInput.getText().toString().trim();
        final String endpoint = (token.length() == 40) ? token : link;
        if (endpoint.length() == 0) {
            connInfo.setText("先填分享链接或令牌再测试。");
            return;
        }
        connInfo.setText("正在测试连接…");
        new Thread(new Runnable() {
            public void run() {
                try {
                    final List<Cloud.Entry> entries = Cloud.listAll(endpoint);
                    int audio = 0;
                    for (int i = 0; i < entries.size(); i++) {
                        if (!entries.get(i).dir && isAudio(extOf(entries.get(i).name))) audio++;
                    }
                    final int count = audio;
                    final String repo = Cloud.isToken(endpoint) ? Cloud.repoName(endpoint) : "";
                    ui.post(new Runnable() {
                        public void run() {
                            connInfo.setText("连接正常：云端有 " + count + " 首可播放的歌曲"
                                    + (repo.length() > 0 ? " · 资料库 " + repo : ""));
                        }
                    });
                } catch (final Exception e) {
                    ui.post(new Runnable() {
                        public void run() {
                            connInfo.setText("连接失败：" + Util.shorten(e.getMessage()));
                        }
                    });
                }
            }
        }).start();
    }


    protected void confirmClearCache() {
        new AlertDialog.Builder(this)
                .setTitle("清除缓存？")
                .setMessage("只是删掉本机缓存，云端文件不受影响。")
                .setNegativeButton("取消", null)
                .setPositiveButton("清除", new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        Store.clearCache(LibraryScreen.this);
                        updateCacheInfo();
                        toast("缓存已清除");
                    }
                })
                .show();
    }


    // ---------------------------------------------------------------- 应用内更新

    /**
     * 检查更新：只在用户点「检查更新」时执行（不自动检查、不后台轮询）。
     * 更新包放在云盘上那个专门的 apk 仓库里，和歌曲库分开。
     */
    protected void checkUpdate() {
        updateStatus.setText("正在检查…");
        new Thread(new Runnable() {
            public void run() {
                Update.Found found = null;
                String error = null;
                try {
                    found = Update.check();
                } catch (Exception e) {
                    error = e.getMessage();
                }
                final Update.Found result = found;
                final String message = error;
                ui.post(new Runnable() {
                    public void run() {
                        if (result == null) {
                            if (message != null && message.length() > 0) {
                                updateStatus.setText("检查失败：" + Util.shorten(message));
                                toast("检查失败：" + Util.shorten(message));
                            } else {
                                updateStatus.setText("当前版本 " + VERSION + "（已是最新）");
                                toast("已是最新版本");
                            }
                            return;
                        }
                        if (Update.compare(result.version, VERSION) <= 0) {
                            updateStatus.setText("当前版本 " + VERSION + "（已是最新）");
                            toast("已是最新版本");
                            return;
                        }
                        updateStatus.setText("发现新版本 " + result.version);
                        showUpdateDialog(result);
                    }
                });
            }
        }).start();
    }


    protected void showUpdateDialog(final Update.Found found) {
        new AlertDialog.Builder(this)
                .setTitle("发现新版本 " + found.version)
                .setMessage("当前版本 " + VERSION + "。\n\n"
                        + "在应用里直接下载安装包，装完会自动删掉安装包。")
                .setNegativeButton("以后再说", null)
                .setPositiveButton("立即更新", new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        startUpdate(found);
                    }
                })
                .show();
    }


    protected void startUpdate(final Update.Found found) {
        if (!Update.canInstall(this)) {
            toast("请先允许「安装未知应用」，然后再点一次更新");
            Update.openInstallSettings(this);
            return;
        }
        final ProgressDialog dialog = new ProgressDialog(this);
        dialog.setTitle("正在更新到 " + found.version);
        dialog.setMessage("正在从云盘下载…");
        dialog.setProgressStyle(ProgressDialog.STYLE_HORIZONTAL);
        dialog.setMax(100);
        dialog.setCancelable(false);
        dialog.show();

        final File target = Update.apkFile(this);
        new Thread(new Runnable() {
            public void run() {
                try {
                    Util.Progress progress = new Util.Progress() {
                        public void onProgress(final long done, final long total) {
                            final int percent = total > 0 ? (int) (done * 100 / total) : 0;
                            ui.post(new Runnable() {
                                public void run() {
                                    dialog.setProgress(percent);
                                }
                            });
                        }
                    };
                    Cloud.download(Update.UPDATE_ENDPOINT, found.path, target, progress);
                    ui.post(new Runnable() {
                        public void run() {
                            dialog.dismiss();
                            if (!Update.verify(LibraryScreen.this, target, found.version)) {
                                if (target.exists()) target.delete();
                                updateStatus.setText("安装包校验没通过，已删除");
                                toast("安装包校验没通过（版本或签名不符），已删除");
                                return;
                            }
                            toast("下载完成，请在系统提示里确认安装");
                            Update.install(LibraryScreen.this, target);
                        }
                    });
                } catch (final Exception e) {
                    if (target.exists()) target.delete();
                    ui.post(new Runnable() {
                        public void run() {
                            dialog.dismiss();
                            updateStatus.setText("更新失败：" + Util.shorten(e.getMessage()));
                            toast("更新失败：" + Util.shorten(e.getMessage()));
                        }
                    });
                }
            }
        }).start();
    }


    /** 后台补齐 MP3 时长（取每个文件前 64 KB 解析帧头）。 */
    protected void probeDurations(final int token) {
        final String endpoint = Store.endpoint;
        new Thread(new Runnable() {
            public void run() {
                List<Song> pending = new ArrayList<Song>();
                for (int i = 0; i < Store.songs.size(); i++) {
                    Song s = Store.songs.get(i);
                    if (s.duration <= 0 && s.fileName.toLowerCase(Locale.ROOT).endsWith(".mp3")) {
                        pending.add(s);
                    }
                }
                if (pending.isEmpty()) return;
                int done = 0;
                boolean changed = false;
                for (int i = 0; i < pending.size(); i++) {
                    if (token != scanToken) return;
                    Song s = pending.get(i);
                    try {
                        int span = (int) Math.min(65536, s.size > 0 ? s.size : 65536);
                        byte[] head = Cloud.head(endpoint, s.cloudPath, span);
                        double duration = Util.mp3Duration(head, s.size);
                        if (duration > 0) {
                            s.duration = duration;
                            Store.setDuration(LibraryScreen.this, s, duration);
                            changed = true;
                        }
                    } catch (Exception e) {
                        // 单首失败不影响其它
                    }
                    done++;
                    if (done % 5 == 0) {
                        final int finished = done;
                        final int total = pending.size();
                        ui.post(new Runnable() {
                            public void run() {
                                if (token != scanToken) return;
                                updateLibraryInfo("正在读取时长… " + finished + "/" + total);
                            }
                        });
                    }
                }
                if (changed) {
                    ui.post(new Runnable() {
                        public void run() {
                            if (token != scanToken) return;
                            libAdapter.notifyDataSetChanged();
                            updateLibraryInfo(null);
                        }
                    });
                }
            }
        }).start();
    }


    /** 上传目标：当前选中的歌单；没选歌单就用「默认歌单」；都没有才传根目录。 */
    protected String uploadTargetDir() {
        if (playlistFilter.length() > 0) return "/" + playlistFilter;
        List<String> names = playlistNames();
        if (names.contains("默认歌单")) return "/默认歌单";
        return "/";
    }


    protected String displayName(Uri uri) {
        String name = null;
        Cursor cursor = null;
        try {
            cursor = getContentResolver().query(uri, null, null, null, null);
            if (cursor != null && cursor.moveToFirst()) {
                int index = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                if (index >= 0) name = cursor.getString(index);
            }
        } catch (Exception e) {
            name = null;
        } finally {
            if (cursor != null) cursor.close();
        }
        if (name == null || name.length() == 0) {
            String path = uri.getLastPathSegment();
            name = path == null ? "upload-" + System.currentTimeMillis() : path;
            int slash = name.lastIndexOf('/');
            if (slash >= 0) name = name.substring(slash + 1);
        }
        return name;
    }


    protected File copyToUploadDir(Uri uri) throws Exception {
        String name = displayName(uri).replace('/', '_').replace('\\', '_');
        File dir = new File(getCacheDir(), "upload");
        if (!dir.exists()) dir.mkdirs();
        File target = new File(dir, name);
        InputStream in = getContentResolver().openInputStream(uri);
        if (in == null) throw new Exception("读不到这个文件");
        FileOutputStream out = new FileOutputStream(target);
        byte[] buf = new byte[65536];
        int n;
        try {
            while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
        } finally {
            out.close();
            in.close();
        }
        return target;
    }


    protected void requestNotificationPermission() {
        if (Build.VERSION.SDK_INT < 33) return;
        try {
            if (checkSelfPermission("android.permission.POST_NOTIFICATIONS")
                    != android.content.pm.PackageManager.PERMISSION_GRANTED) {
                requestPermissions(new String[] { "android.permission.POST_NOTIFICATIONS" }, REQ_NOTIFY);
            }
        } catch (Exception e) {
            // 忽略：拿不到通知权限只是看不到通知栏控制
        }
    }

}
