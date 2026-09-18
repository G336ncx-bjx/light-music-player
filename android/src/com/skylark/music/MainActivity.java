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
public class MainActivity extends Activity {

    private static final int REQ_PICK = 101;
    private static final int REQ_NOTIFY = 102;
    private static final int REQ_STORAGE = 103;

    /** 与 AndroidManifest.xml 的 versionName 保持一致。 */
    public static final String VERSION = "3.3.21";

    /** 系统播放器（MediaPlayer）原生支持的格式：mp3 / m4a / aac / wav / wma / flac / ogg / opus。 */
    private static final String[] AUDIO_EXT = { "mp3", "m4a", "aac", "wav", "wma", "flac", "ogg", "oga", "opus" };
    private static final String[] IGNORED_EXT = { "ape", "wv", "aif", "aiff", "mp4", "mkv", "avi", "m4v" };

    /** 重建界面（换主题）后回到原来的标签页。 */
    private static int lastTab = 0;

    // 调色板
    private boolean dark;
    private int cBg, cSurface, cAlt, cText, cDim, cAccent, cDivider, cDanger, cTrack;

    private final Handler ui = new Handler(Looper.getMainLooper());

    private final TextView[] tabs = new TextView[4];
    private final View[] tabLines = new View[4];
    private final View[] pages = new View[4];
    private int tabIndex = 0;

    // 音乐库
    private EditText search;
    private ListView libList;
    private TextView libInfo, libEmpty;
    private Button sortButton;
    private SongAdapter libAdapter;
    private final List<Song> shown = new ArrayList<Song>();
    /** 歌单筛选：空串＝全部歌单。 */
    private String playlistFilter = "";
    /** 音乐库分两层：false＝歌单（文件夹）列表，true＝某个歌单里的歌曲列表。 */
    private boolean inPlaylist;
    private LinearLayout libHome, libFolders, libListPanel, libHeadRow, libSelHead, libSelActions, libSearchRow;
    private TextView libTitle, libSelCount, libHomeInfo;
    private Button libSelAll;

    // 播放队列
    private ListView queueList;
    private TextView queueInfo, queueEmpty;
    private SongAdapter queueAdapter;
    private LinearLayout queueHeadRow, queueSelHead, queueSelActions;
    private View queueTipsRow;
    private TextView queueSelCount;
    private Button queueSelAll;

    // 歌词
    private TextView lyricTitle, lyricArtist, lyricState, offsetLabel;
    private LinearLayout lyricBox;
    private ScrollView lyricScroll;
    private final List<LinearLayout> lyricRows = new ArrayList<LinearLayout>();
    private final List<Lrc.Line> lyricLines = new ArrayList<Lrc.Line>();
    private boolean lyricsSynced;
    private int lyricIndex = -1;
    private String lyricLoadedPath = "";
    private String lyricPendingPath = "";
    private long manualScrollUntil = 0;
    /**
     * 正在做「自动滚动到当前句」的动画。这期间不更新「回到当前歌词」按钮的显隐，
     * 否则动画途中取景短暂偏离当前句，按钮会闪一下。
     */
    private boolean autoScrolling;
    /** 歌词页浮动的「回到当前歌词」按钮（滚离当前句时才出现）。 */
    private TextView backToCurrent;

    // 设置
    private EditText linkInput, tokenInput;
    private Switch cacheSwitch;
    private TextView connInfo, cacheInfo, hiddenInfo, updateStatus;
    private final TextView[] themeButtons = new TextView[3];
    private TextView cacheModeHint;

    // 底部播放条
    private TextView nowTitle, nowArtist, timeNow, timeTotal, modeText;
    private SeekBar seek;
    private ImageView playIcon;
    private boolean dragging;

    private int scanToken = 0;
    private boolean uploading;
    private boolean starting;

    // ---------------------------------------------------------------- 生命周期

    @Override
    protected void onCreate(Bundle state) {
        dark = Prefs.isDark(this);
        setTheme(dark ? android.R.style.Theme_Material_NoActionBar
                : android.R.style.Theme_Material_Light_NoActionBar);
        super.onCreate(state);
        palette();
        Store.load(this);

        // 更新完（或放弃更新）后把没用的安装包删掉
        Update.cleanUp(this);

        setContentView(buildRoot());
        selectTab(lastTab);
        refreshQueue();
        refreshSettings();
        updateCacheButtons();

        startPlayerService();
        requestNotificationPermission();
        handleIntent(getIntent());

        if (Store.endpoint.length() == 0) {
            selectTab(3);
            connInfo.setText("第一次使用：把云盘分享链接或 API 令牌粘到上面，然后点「保存并刷新」。");
        } else {
            startScan(false);
        }
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        handleIntent(intent);
    }

    @Override
    protected void onResume() {
        super.onResume();
        PlayerService.listener = new PlayerService.Listener() {
            public void onPlayerChanged() {
                ui.post(new Runnable() {
                    public void run() {
                        refreshNowPlaying();
                        refreshQueue();
                    }
                });
            }
        };
        refreshNowPlaying();
        refreshQueue();
        refreshSettings();
        ui.post(ticker);
        ui.post(lyricTicker);
    }

    @Override
    protected void onPause() {
        super.onPause();
        PlayerService.listener = null;
        ui.removeCallbacks(ticker);
        ui.removeCallbacks(lyricTicker);
    }

    @Override
    public void onConfigurationChanged(Configuration config) {
        super.onConfigurationChanged(config);
        if (Prefs.themeMode(this) == 0 && Prefs.isDark(this) != dark) recreate();
    }

    @Override
    public void onBackPressed() {
        if (tabIndex != 0) {
            selectTab(0);
            return;
        }
        if (inPlaylist) {
            backToFolders();
            return;
        }
        super.onBackPressed();
    }

    // ---------------------------------------------------------------- 调色板与控件工厂

    private void palette() {
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

    private int dp(float value) {
        return (int) (value * getResources().getDisplayMetrics().density + 0.5f);
    }

    private Drawable round(int color, float radius) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(color);
        g.setCornerRadius(dp(radius));
        return g;
    }

    private Drawable roundStroke(int color, float radius, int strokeColor, float width) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(color);
        g.setCornerRadius(dp(radius));
        g.setStroke(Math.max(1, dp(width)), strokeColor);
        return g;
    }

    private LinearLayout row() {
        LinearLayout l = new LinearLayout(this);
        l.setOrientation(LinearLayout.HORIZONTAL);
        l.setGravity(Gravity.CENTER_VERTICAL);
        return l;
    }

    private LinearLayout column() {
        LinearLayout l = new LinearLayout(this);
        l.setOrientation(LinearLayout.VERTICAL);
        return l;
    }

    private TextView text(String value, float size, int color) {
        TextView v = new TextView(this);
        v.setText(value);
        v.setTextSize(size);
        v.setTextColor(color);
        v.setLineSpacing(dp(2), 1f);
        return v;
    }

    private Button button(String label, boolean primary, View.OnClickListener click) {
        Button b = new Button(this);
        b.setText(label);
        b.setAllCaps(false);
        b.setTextSize(13);
        b.setTextColor(primary ? 0xFFFFFFFF : cText);
        b.setBackground(round(primary ? cAccent : cAlt, 10));
        b.setPadding(dp(12), 0, dp(12), 0);
        b.setMinWidth(0);
        b.setMinimumWidth(0);
        b.setMinimumHeight(0);
        b.setMinHeight(0);
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, dp(36));
        p.rightMargin = dp(8);
        b.setLayoutParams(p);
        if (Build.VERSION.SDK_INT >= 21) b.setStateListAnimator(null);
        if (click != null) b.setOnClickListener(click);
        return b;
    }

    private Button wideButton(String label, boolean primary, View.OnClickListener click) {
        Button b = button(label, primary, click);
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(0, dp(38), 1f);
        p.rightMargin = dp(8);
        b.setLayoutParams(p);
        return b;
    }

    private EditText input(String hint, String value) {
        EditText e = new EditText(this);
        e.setHint(hint);
        e.setHintTextColor(cDim);
        e.setTextColor(cText);
        e.setTextSize(14);
        e.setSingleLine(true);
        e.setText(value == null ? "" : value);
        e.setBackground(round(cAlt, 10));
        e.setPadding(dp(12), dp(10), dp(12), dp(10));
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        p.bottomMargin = dp(8);
        e.setLayoutParams(p);
        return e;
    }

    private LinearLayout card() {
        LinearLayout c = column();
        c.setBackground(round(cSurface, 14));
        c.setPadding(dp(14), dp(14), dp(14), dp(14));
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        p.bottomMargin = dp(12);
        c.setLayoutParams(p);
        return c;
    }

    private TextView cardTitle(String value) {
        TextView t = text(value, 15, cText);
        t.setTypeface(null, Typeface.BOLD);
        t.setPadding(0, 0, 0, dp(8));
        return t;
    }

    private TextView hint(String value) {
        TextView t = text(value, 12, cDim);
        t.setPadding(0, 0, 0, dp(10));
        return t;
    }

    // ---------------------------------------------------------------- 主框架

    private View buildRoot() {
        LinearLayout root = column();
        root.setBackgroundColor(cBg);

        LinearLayout header = row();
        header.setPadding(dp(16), dp(14), dp(16), dp(6));
        TextView title = text("云雀", 20, cText);
        title.setTypeface(null, Typeface.BOLD);
        header.addView(title, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        TextView sub = text("云端音乐库", 12, cDim);
        LinearLayout.LayoutParams subP = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        subP.leftMargin = dp(10);
        header.addView(sub, subP);
        root.addView(header);

        root.addView(buildTabs());

        android.widget.FrameLayout content = new android.widget.FrameLayout(this);
        LinearLayout.LayoutParams contentP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
        content.setLayoutParams(contentP);
        pages[0] = buildLibraryPage();
        pages[1] = buildQueuePage();
        pages[2] = buildLyricsPage();
        pages[3] = buildSettingsPage();
        for (int i = 0; i < 4; i++) content.addView(pages[i]);
        root.addView(content);

        root.addView(buildPlayerBar());
        return root;
    }

    private View buildTabs() {
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

    private void selectTab(int index) {
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

    private void hideKeyboard() {
        View focus = getCurrentFocus();
        if (focus == null) return;
        InputMethodManager imm = (InputMethodManager) getSystemService(Context.INPUT_METHOD_SERVICE);
        if (imm != null) imm.hideSoftInputFromWindow(focus.getWindowToken(), 0);
    }

    // ---------------------------------------------------------------- 音乐库页

    private View buildLibraryPage() {
        LinearLayout page = column();
        page.setPadding(dp(12), dp(12), dp(12), 0);

        // ================= 第一层：歌单（云盘上的文件夹） =================
        libHome = column();
        LinearLayout homeHead = row();
        TextView homeTitle = text("音乐库", 18, cText);
        homeTitle.setTypeface(null, Typeface.BOLD);
        homeHead.addView(homeTitle, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        homeHead.addView(button("刷新", false, new View.OnClickListener() {
            public void onClick(View v) {
                startScan(false);
            }
        }));
        libHome.addView(homeHead);
        TextView homeHint = text("点歌单进去看歌；长按歌单可以改名 / 删除。", 12, cDim);
        LinearLayout.LayoutParams homeHintP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        homeHintP.topMargin = dp(6);
        homeHint.setLayoutParams(homeHintP);
        libHome.addView(homeHint);

        libFolders = column();
        ScrollView folderScroll = new ScrollView(this);
        folderScroll.setVerticalScrollBarEnabled(false);
        folderScroll.addView(libFolders);
        LinearLayout.LayoutParams folderP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
        folderP.topMargin = dp(12);
        libHome.addView(folderScroll, folderP);

        libHomeInfo = text("", 12, cDim);
        LinearLayout.LayoutParams homeInfoP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        homeInfoP.topMargin = dp(10);
        libHomeInfo.setLayoutParams(homeInfoP);
        libHome.addView(libHomeInfo);
        page.addView(libHome, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        // ================= 第二层：某个歌单里的歌曲 =================
        libListPanel = column();
        libListPanel.setVisibility(View.GONE);

        // 第一行：返回 + 歌单名 + 播放 + 上传（多选时换成：完成 + 已选 + 全选）
        libHeadRow = row();
        libHeadRow.addView(button("返回", false, new View.OnClickListener() {
            public void onClick(View v) {
                backToFolders();
            }
        }));
        libTitle = text("全部歌曲", 15, cText);
        libTitle.setTypeface(null, Typeface.BOLD);
        libTitle.setSingleLine(true);
        libTitle.setEllipsize(android.text.TextUtils.TruncateAt.END);
        libHeadRow.addView(libTitle, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        libHeadRow.addView(button("播放", false, new View.OnClickListener() {
            public void onClick(View v) {
                playAll();
            }
        }));
        libHeadRow.addView(button("上传", true, new View.OnClickListener() {
            public void onClick(View v) {
                pickFiles();
            }
        }));
        libListPanel.addView(libHeadRow);

        libSelHead = row();
        libSelHead.setVisibility(View.GONE);
        libSelHead.addView(button("完成", false, new View.OnClickListener() {
            public void onClick(View v) {
                setLibSelecting(false);
            }
        }));
        libSelCount = text("已选 0 首", 13, cText);
        libSelHead.addView(libSelCount, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        libSelAll = button("全选", false, new View.OnClickListener() {
            public void onClick(View v) {
                toggleSelectAll(libAdapter, libSelAll);
            }
        });
        libSelHead.addView(libSelAll);
        libListPanel.addView(libSelHead);

        // 第二行：搜索 + 排序（多选时换成：下载 / 加入歌单 / 删除）
        libSearchRow = row();
        LinearLayout.LayoutParams searchRowP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        searchRowP.topMargin = dp(8);
        libSearchRow.setLayoutParams(searchRowP);
        search = new EditText(this);
        search.setHint("搜索歌名、歌手");
        search.setHintTextColor(cDim);
        search.setTextColor(cText);
        search.setTextSize(14);
        search.setSingleLine(true);
        search.setBackground(round(cAlt, 12));
        search.setPadding(dp(14), dp(8), dp(14), dp(8));
        LinearLayout.LayoutParams sp = new LinearLayout.LayoutParams(0, dp(42), 1f);
        sp.rightMargin = dp(8);
        search.setLayoutParams(sp);
        search.addTextChangedListener(new TextWatcher() {
            public void beforeTextChanged(CharSequence s, int a, int b, int c) { }
            public void onTextChanged(CharSequence s, int a, int b, int c) { }
            public void afterTextChanged(Editable s) {
                applyFilter();
            }
        });
        libSearchRow.addView(search);
        sortButton = button("排序", false, new View.OnClickListener() {
            public void onClick(View v) {
                showSortDialog();
            }
        });
        libSearchRow.addView(sortButton);
        libListPanel.addView(libSearchRow);

        libSelActions = row();
        libSelActions.setVisibility(View.GONE);
        LinearLayout.LayoutParams selActionsP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        selActionsP.topMargin = dp(8);
        libSelActions.setLayoutParams(selActionsP);
        libSelActions.addView(growButton("下载", true, new View.OnClickListener() {
            public void onClick(View v) {
                askDownloadScope(libAdapter.pickedSongs());
            }
        }));
        libSelActions.addView(growButton("加入歌单", false, new View.OnClickListener() {
            public void onClick(View v) {
                addSelectionToPlaylist(libAdapter.pickedSongs());
            }
        }));
        libSelActions.addView(growButton("删除", false, new View.OnClickListener() {
            public void onClick(View v) {
                confirmDeleteMany(libAdapter.pickedSongs());
            }
        }));
        libListPanel.addView(libSelActions);

        libInfo = text("", 12, cDim);
        LinearLayout.LayoutParams infoP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        infoP.topMargin = dp(8);
        libInfo.setLayoutParams(infoP);
        libListPanel.addView(libInfo);

        libEmpty = text("还没有歌曲。先在「设置」里连接云盘，再回来点「刷新」。", 13, cDim);
        libEmpty.setGravity(Gravity.CENTER);
        libEmpty.setPadding(dp(20), dp(40), dp(20), dp(40));
        libListPanel.addView(libEmpty, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        libList = new ListView(this);
        libList.setDivider(new android.graphics.drawable.ColorDrawable(cDivider));
        libList.setDividerHeight(1);
        libList.setBackgroundColor(cSurface);
        libList.setSelector(new android.graphics.drawable.ColorDrawable(cAlt));
        LinearLayout.LayoutParams listP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
        listP.topMargin = dp(8);
        libList.setLayoutParams(listP);
        libList.setOnItemClickListener(new AdapterView.OnItemClickListener() {
            public void onItemClick(AdapterView<?> parent, View view, int position, long id) {
                if (libAdapter.isSelecting()) {
                    libAdapter.toggle(songAt(shown, position));
                    updateSelectionBars();
                    return;
                }
                playFromLibrary(position);
            }
        });
        libList.setOnItemLongClickListener(new AdapterView.OnItemLongClickListener() {
            public boolean onItemLongClick(AdapterView<?> parent, View view, int position, long id) {
                if (libAdapter.isSelecting()) {
                    showLibraryMenu(position);
                    return true;
                }
                setLibSelecting(true);
                libAdapter.toggle(songAt(shown, position));
                updateSelectionBars();
                return true;
            }
        });
        libAdapter = new SongAdapter(false);
        libList.setAdapter(libAdapter);
        libListPanel.addView(libList);
        page.addView(libListPanel, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        return page;
    }

    private static Song songAt(List<Song> list, int position) {
        if (position < 0 || position >= list.size()) return null;
        return list.get(position);
    }

    /** 重建第一层的歌单（文件夹）列表。 */
    private void refreshFolderRows() {
        if (libFolders == null) return;
        libFolders.removeAllViews();

        Map<String, Integer> counts = new java.util.LinkedHashMap<String, Integer>();
        for (int i = 0; i < Store.songs.size(); i++) {
            String name = Store.songs.get(i).playlist();
            if (name.length() == 0) continue;
            Integer have = counts.get(name);
            counts.put(name, have == null ? 1 : have + 1);
        }
        List<String> names = new ArrayList<String>(counts.keySet());
        Collections.sort(names, new Comparator<String>() {
            public int compare(String a, String b) {
                return a.compareToIgnoreCase(b);
            }
        });

        libFolders.addView(folderRow("全部歌曲", Store.songs.size() + " 首", "", false));
        for (int i = 0; i < names.size(); i++) {
            String name = names.get(i);
            libFolders.addView(folderRow(name, counts.get(name) + " 首", name, false));
        }
        libFolders.addView(folderRow("＋ 新建歌单", "云盘上会新建一个同名文件夹", null, true));
    }

    /** 一行「文件夹」：图标 + 名字 + 说明 + 右箭头；value 为 null 表示「新建歌单」那一行。 */
    private View folderRow(final String label, String subtitle, final String value, boolean create) {
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
    private void openPlaylist(String value) {
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
    private void backToFolders() {
        setLibSelecting(false);
        inPlaylist = false;
        playlistFilter = "";
        if (libListPanel != null) libListPanel.setVisibility(View.GONE);
        if (libHome != null) libHome.setVisibility(View.VISIBLE);
        if (search != null) search.setText("");
        refreshFolderRows();
    }

    /**
     * 进 / 出多选：把「标题行 + 搜索行」换成「已选 + 全选」和三个批量操作，
     * 行数不变，所以进多选不会少看几首歌。
     */
    private void setLibSelecting(boolean on) {
        if (libAdapter == null) return;
        libAdapter.setSelecting(on);
        if (libHeadRow != null) libHeadRow.setVisibility(on ? View.GONE : View.VISIBLE);
        if (libSearchRow != null) libSearchRow.setVisibility(on ? View.GONE : View.VISIBLE);
        if (libSelHead != null) libSelHead.setVisibility(on ? View.VISIBLE : View.GONE);
        if (libSelActions != null) libSelActions.setVisibility(on ? View.VISIBLE : View.GONE);
        updateSelectionBars();
    }

    private void showPlaylistMenu(final String name) {
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

    private void newPlaylist() {
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

    private void renamePlaylist(final String name) {
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

    private void confirmDeletePlaylist(final String name) {
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

    /** 列出某个歌单文件夹里的全部文件（重命名时用）。 */
    private List<Cloud.Entry> listOfPlaylist(String endpoint, String name) throws Exception {
        List<Cloud.Entry> all = Cloud.listAll(endpoint);
        List<Cloud.Entry> out = new ArrayList<Cloud.Entry>();
        for (int i = 0; i < all.size(); i++) {
            if (name.equals(Cloud.playlistOf(all.get(i).path))) out.add(all.get(i));
        }
        return out;
    }

    /** 云盘操作统一入口：后台跑，完事重扫曲库。 */
    private interface CloudTask {
        void run(String endpoint) throws Exception;
    }

    private void runCloud(final String label, final CloudTask task) {
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

    // ---------------------------------------------------------------- 多选管理

    private Button growButton(String label, boolean primary, View.OnClickListener click) {
        Button b = button(label, primary, click);
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(0, dp(38), 1f);
        p.rightMargin = dp(8);
        b.setLayoutParams(p);
        return b;
    }

    /** 播放队列的多选：同样是「换个内容」而不是「多加两行」。 */
    private void setQueueSelecting(boolean on) {
        if (queueAdapter == null) return;
        queueAdapter.setSelecting(on);
        if (queueHeadRow != null) queueHeadRow.setVisibility(on ? View.GONE : View.VISIBLE);
        if (queueTipsRow != null) queueTipsRow.setVisibility(on ? View.GONE : View.VISIBLE);
        if (queueSelHead != null) queueSelHead.setVisibility(on ? View.VISIBLE : View.GONE);
        if (queueSelActions != null) queueSelActions.setVisibility(on ? View.VISIBLE : View.GONE);
        updateSelectionBars();
    }

    private void updateSelectionBars() {
        if (libSelCount != null && libAdapter != null) {
            libSelCount.setText("已选 " + libAdapter.pickedCount() + " 首");
            if (libSelAll != null)
                libSelAll.setText(libAdapter.allPicked() ? "取消全选" : "全选");
        }
        if (queueSelCount != null && queueAdapter != null) {
            queueSelCount.setText("已选 " + queueAdapter.pickedCount() + " 首");
            if (queueSelAll != null)
                queueSelAll.setText(queueAdapter.allPicked() ? "取消全选" : "全选");
        }
    }

    /** 全选 / 取消全选：已经全勾上了就再点一次全部取消。 */
    private void toggleSelectAll(SongAdapter adapter, Button button) {
        if (adapter == null) return;
        if (adapter.allPicked()) adapter.clearPicked();
        else adapter.selectAll();
        updateSelectionBars();
    }

    private void cancelSelection() {
        if (libAdapter != null && libAdapter.isSelecting()) setLibSelecting(false);
        if (queueAdapter != null && queueAdapter.isSelecting()) setQueueSelecting(false);
    }

    /** 下载选中项：先问下音频 / 歌词 / 两者，再后台逐个下到系统「下载」目录。 */
    private void askDownloadScope(final List<Song> songs) {
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

    private void startDownload(final List<Song> songs, final boolean audio, final boolean lyrics) {
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
                            Saver.save(MainActivity.this, temp, song.fileName, mimeOf(song.fileName));
                            temp.delete();
                        }
                        if (lyrics) {
                            if (song.hasLyrics()) {
                                File temp = new File(getCacheDir(), "dl-lyric.tmp");
                                Cloud.download(endpoint, song.lyricPath, temp, null);
                                Saver.save(MainActivity.this, temp, lrcName(song), "text/plain");
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
    private boolean ensureStoragePermission() {
        if (Build.VERSION.SDK_INT >= 29) return true;
        if (checkSelfPermission(android.Manifest.permission.WRITE_EXTERNAL_STORAGE)
                == android.content.pm.PackageManager.PERMISSION_GRANTED) return true;
        requestPermissions(new String[] { android.Manifest.permission.WRITE_EXTERNAL_STORAGE }, REQ_STORAGE);
        toast("给云雀「存储」权限后，再点一次下载");
        return false;
    }

    private static String lrcName(Song song) {
        String name = song.fileName;
        int dot = name.lastIndexOf('.');
        return (dot > 0 ? name.substring(0, dot) : name) + ".lrc";
    }

    private static String mimeOf(String name) {
        String lower = name.toLowerCase(Locale.ROOT);
        if (lower.endsWith(".m4a") || lower.endsWith(".mp4")) return "audio/mp4";
        if (lower.endsWith(".aac")) return "audio/aac";
        if (lower.endsWith(".wav")) return "audio/wav";
        if (lower.endsWith(".wma")) return "audio/x-ms-wma";
        if (lower.endsWith(".flac")) return "audio/flac";
        if (lower.endsWith(".ogg") || lower.endsWith(".oga")) return "audio/ogg";
        if (lower.endsWith(".opus")) return "audio/opus";
        return "audio/mpeg";
    }

    /** 把选中的歌复制进另一个歌单（一个歌单＝云盘上一个文件夹）。 */
    private void addSelectionToPlaylist(final List<Song> songs) {
        if (songs == null || songs.isEmpty()) {
            toast("先选中歌曲");
            return;
        }
        final List<String> names = playlistNames();
        names.add("＋ 新建歌单…");
        new AlertDialog.Builder(this)
                .setTitle("把 " + songs.size() + " 首加入歌单")
                .setItems(names.toArray(new String[0]), new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        if (which == names.size() - 1) newPlaylistFor(songs);
                        else copyToPlaylist(songs, names.get(which));
                    }
                })
                .show();
    }

    private List<String> playlistNames() {
        List<String> names = new ArrayList<String>();
        for (int i = 0; i < Store.songs.size(); i++) {
            String name = Store.songs.get(i).playlist();
            if (name.length() > 0 && !names.contains(name)) names.add(name);
        }
        Collections.sort(names, new Comparator<String>() {
            public int compare(String a, String b) {
                return a.compareToIgnoreCase(b);
            }
        });
        return names;
    }

    private void newPlaylistFor(final List<Song> songs) {
        final EditText field = input("歌单名", "");
        new AlertDialog.Builder(this)
                .setTitle("新建歌单")
                .setView(field)
                .setNegativeButton("取消", null)
                .setPositiveButton("创建并加入", new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        String name = field.getText().toString().trim();
                        if (Cloud.badName(name)) {
                            toast("这个名字不能用在文件夹上");
                            return;
                        }
                        copyToPlaylist(songs, name);
                    }
                })
                .show();
    }

    private void copyToPlaylist(final List<Song> songs, final String target) {
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
    private void confirmDeleteMany(final List<Song> songs) {
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

    private void removeSelectedFromQueue(List<Song> songs) {
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

    private void showSortDialog() {
        final String[] names = { "歌名", "歌手", "时长", "云盘顺序", "重新扫描云盘" };
        new AlertDialog.Builder(this)
                .setTitle("排序方式")
                .setItems(names, new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        if (which == 4) {
                            startScan(false);
                            return;
                        }
                        Prefs.setSortMode(MainActivity.this, which);
                        updateSortButton();
                        applyFilter();
                    }
                })
                .show();
    }

    private void updateSortButton() {
        if (sortButton == null) return;
        String[] names = { "歌名", "歌手", "时长", "云盘顺序" };
        int mode = Prefs.sortMode(this);
        sortButton.setText("排序：" + names[Math.max(0, Math.min(3, mode))]);
        sortButton.setTextSize(13);
    }

    private void applyFilter() {
        String query = search == null ? "" : search.getText().toString().trim().toLowerCase(Locale.ROOT);
        shown.clear();
        for (int i = 0; i < Store.songs.size(); i++) {
            Song s = Store.songs.get(i);
            if (playlistFilter.length() > 0 && !playlistFilter.equals(s.playlist())) continue;
            if (query.length() > 0) {
                String hay = (s.title + " " + s.artist + " " + s.fileName).toLowerCase(Locale.ROOT);
                if (!hay.contains(query)) continue;
            }
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

    private void sortSongs(List<Song> list) {
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

    private String safe(String text) {
        return text == null ? "" : text;
    }

    private void updateLibraryInfo(String message) {
        if (libInfo == null) return;
        if (message != null) {
            libInfo.setText(message);
            if (libHomeInfo != null) libHomeInfo.setText(message);
            return;
        }
        if (Store.songs.isEmpty()) {
            String empty = Store.scanning ? "正在读取云端目录…"
                    : (Store.endpoint.length() == 0 ? "还没有连接云盘" : "云盘里还没有音频文件");
            libInfo.setText(empty);
            if (libHomeInfo != null) libHomeInfo.setText(empty);
            return;
        }
        if (libHomeInfo != null) {
            libHomeInfo.setText(Store.scanning ? "正在读取云端目录…"
                    : "共 " + Store.songs.size() + " 首"
                        + (Store.skippedUnsupported > 0
                            ? "，已忽略 " + Store.skippedUnsupported + " 个不支持的文件" : ""));
        }
        int cached = 0;
        for (int i = 0; i < shown.size(); i++) {
            if (Store.isCached(this, shown.get(i))) cached++;
        }
        StringBuilder sb = new StringBuilder();
        sb.append("共 ").append(Store.songs.size()).append(" 首");
        if (shown.size() != Store.songs.size()) sb.append("，筛出 ").append(shown.size()).append(" 首");
        if (cached > 0) sb.append("，已缓存 ").append(cached).append(" 首");
        if (Store.skippedUnsupported > 0) {
            sb.append("，已忽略 ").append(Store.skippedUnsupported).append(" 个不支持的文件");
        }
        if (Store.status.length() > 0) sb.append(" · ").append(Store.status);
        libInfo.setText(sb.toString());
    }

    private void playFromLibrary(int position) {
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
    private void playAll() {
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

    private int indexInQueue(String cloudPath) {
        for (int i = 0; i < Store.queue.size(); i++) {
            if (Store.queue.get(i).cloudPath.equals(cloudPath)) return i;
        }
        return -1;
    }

    private void showLibraryMenu(final int position) {
        if (position < 0 || position >= shown.size()) return;
        final Song song = shown.get(position);
        final boolean canDelete = Cloud.canDelete(Store.endpoint);
        final List<String> items = new ArrayList<String>();
        items.add("立即播放");
        items.add("下一首播放");
        items.add("加到队列末尾");
        items.add("从音乐库移除");
        if (canDelete) items.add("从云盘删除");
        new AlertDialog.Builder(this)
                .setTitle(song.title + " · " + song.artistText())
                .setItems(items.toArray(new String[0]), new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        String action = items.get(which);
                        if ("立即播放".equals(action)) {
                            playFromLibrary(position);
                        } else if ("下一首播放".equals(action)) {
                            enqueueToService(song, true);
                        } else if ("加到队列末尾".equals(action)) {
                            enqueueToService(song, false);
                        } else if ("从音乐库移除".equals(action)) {
                            hideFromLibrary(song);
                        } else {
                            confirmCloudDelete(song);
                        }
                    }
                })
                .show();
    }

    private void enqueueToService(Song song, boolean next) {
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

    private void hideFromLibrary(Song song) {
        Set<String> hidden = Store.hiddenSet(this);
        hidden.add(song.cloudPath);
        Store.setHiddenSet(this, hidden);
        for (int i = Store.songs.size() - 1; i >= 0; i--) {
            if (Store.songs.get(i).cloudPath.equals(song.cloudPath)) Store.songs.remove(i);
        }
        applyFilter();
        toast("已从音乐库移除（可在设置里恢复）");
    }

    private void confirmCloudDelete(final Song song) {
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

    // ---------------------------------------------------------------- 播放队列页

    private View buildQueuePage() {
        LinearLayout page = column();
        page.setPadding(dp(12), dp(12), dp(12), 0);

        LinearLayout head = row();
        queueHeadRow = head;
        queueInfo = text("播放队列", 14, cText);
        head.addView(queueInfo, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        head.addView(button("清空", false, new View.OnClickListener() {
            public void onClick(View v) {
                confirmClearQueue();
            }
        }));
        page.addView(head);

        // 多选时，上面那一行换成「完成 + 已选 + 全选」
        queueSelHead = row();
        queueSelHead.setVisibility(View.GONE);
        queueSelHead.addView(button("完成", false, new View.OnClickListener() {
            public void onClick(View v) {
                setQueueSelecting(false);
            }
        }));
        queueSelCount = text("已选 0 首", 13, cText);
        queueSelHead.addView(queueSelCount, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        queueSelAll = button("全选", false, new View.OnClickListener() {
            public void onClick(View v) {
                toggleSelectAll(queueAdapter, queueSelAll);
            }
        });
        queueSelHead.addView(queueSelAll);
        page.addView(queueSelHead);

        TextView tips = text("点歌曲立即播放；长按进入多选，可以批量下载、加入歌单、移除。", 12, cDim);
        queueTipsRow = tips;
        LinearLayout.LayoutParams tipsP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        tipsP.topMargin = dp(8);
        tips.setLayoutParams(tipsP);
        page.addView(tips);

        // 多选时，提示行换成三个批量操作
        queueSelActions = row();
        queueSelActions.setVisibility(View.GONE);
        LinearLayout.LayoutParams queueSelP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        queueSelP.topMargin = dp(8);
        queueSelActions.setLayoutParams(queueSelP);
        queueSelActions.addView(growButton("下载", true, new View.OnClickListener() {
            public void onClick(View v) {
                askDownloadScope(queueAdapter.pickedSongs());
            }
        }));
        queueSelActions.addView(growButton("加入歌单", false, new View.OnClickListener() {
            public void onClick(View v) {
                addSelectionToPlaylist(queueAdapter.pickedSongs());
            }
        }));
        queueSelActions.addView(growButton("移除", false, new View.OnClickListener() {
            public void onClick(View v) {
                removeSelectedFromQueue(queueAdapter.pickedSongs());
            }
        }));
        page.addView(queueSelActions);

        queueEmpty = text("播放队列是空的。到「音乐库」点歌名就会开始播放并加入队列。", 13, cDim);
        queueEmpty.setGravity(Gravity.CENTER);
        queueEmpty.setPadding(dp(20), dp(40), dp(20), dp(40));
        page.addView(queueEmpty, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        queueList = new ListView(this);
        queueList.setDivider(new android.graphics.drawable.ColorDrawable(cDivider));
        queueList.setDividerHeight(1);
        queueList.setBackgroundColor(cSurface);
        queueList.setSelector(new android.graphics.drawable.ColorDrawable(cAlt));
        LinearLayout.LayoutParams listP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
        listP.topMargin = dp(10);
        queueList.setLayoutParams(listP);
        queueList.setOnItemClickListener(new AdapterView.OnItemClickListener() {
            public void onItemClick(AdapterView<?> parent, View view, int position, long id) {
                if (queueAdapter.isSelecting()) {
                    queueAdapter.toggle(songAt(Store.queue, position));
                    updateSelectionBars();
                    return;
                }
                PlayerService service = PlayerService.instance;
                if (service != null) service.playAt(position);
            }
        });
        queueList.setOnItemLongClickListener(new AdapterView.OnItemLongClickListener() {
            public boolean onItemLongClick(AdapterView<?> parent, View view, int position, long id) {
                if (queueAdapter.isSelecting()) {
                    showQueueMenu(position);
                    return true;
                }
                setQueueSelecting(true);
                queueAdapter.toggle(songAt(Store.queue, position));
                updateSelectionBars();
                return true;
            }
        });
        queueAdapter = new SongAdapter(true);
        queueList.setAdapter(queueAdapter);
        page.addView(queueList);
        return page;
    }

    private void refreshQueue() {
        if (queueAdapter == null) return;
        queueAdapter.setData(new ArrayList<Song>(Store.queue));
        if (queueInfo != null) {
            if (Store.queue.isEmpty()) {
                queueInfo.setText("播放队列");
            } else {
                queueInfo.setText("播放队列 · " + Store.queue.size() + " 首 · 第 "
                        + (Store.index + 1) + " 首");
            }
        }
        if (queueEmpty != null) {
            boolean empty = Store.queue.isEmpty();
            queueEmpty.setVisibility(empty ? View.VISIBLE : View.GONE);
            queueList.setVisibility(empty ? View.GONE : View.VISIBLE);
        }
    }

    private void confirmClearQueue() {
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
                            Store.saveQueue(MainActivity.this);
                        }
                        refreshQueue();
                        refreshNowPlaying();
                    }
                })
                .show();
    }

    private void showQueueMenu(final int position) {
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

    // ---------------------------------------------------------------- 歌词页

    private View buildLyricsPage() {
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

    private void nudgeOffset(double delta) {
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

    private void updateOffsetLabel(Song song) {
        if (offsetLabel == null) return;
        double value = Store.lyricOffset(this, song);
        offsetLabel.setText(String.format(Locale.ROOT, "偏移 %+.1f 秒", value));
    }

    private void refreshLyrics(boolean force) {
        final Song song = Store.current();
        if (lyricTitle == null) return;
        if (song == null) {
            lyricTitle.setText("还没有播放歌曲");
            lyricArtist.setText("");
            lyricState.setText("");
            clearLyricRows("点「音乐库」里任意一首歌开始播放，歌词会自动出现。");
            return;
        }
        lyricTitle.setText(song.title);
        lyricArtist.setText(song.artistText() + (song.hasLyrics() ? "" : " · 没有歌词文件"));
        updateOffsetLabel(song);

        if (song.cloudPath.equals(lyricLoadedPath) && !force) {
            applyLyricHighlight(PlayerService.instance == null ? 0 : PlayerService.instance.position());
            return;
        }
        if (!song.hasLyrics()) {
            lyricLoadedPath = song.cloudPath;
            clearLyricRows("这首歌旁边没有同名的 .lrc 歌词文件。\n把歌词文件按「歌名 - 歌手.lrc」命名后放到同一目录即可。");
            return;
        }
        if (song.cloudPath.equals(lyricPendingPath) && !force) return;

        if (force) {
            File cached = Store.lyricCacheFile(this, song);
            if (cached.exists()) cached.delete();
        }
        File cache = Store.lyricCacheFile(this, song);
        if (!force && cache.exists() && cache.length() > 0) {
            setLyrics(song, readFile(cache));
            return;
        }
        lyricPendingPath = song.cloudPath;
        lyricState.setText("正在获取歌词…");
        clearLyricRows("正在从云端取歌词…");
        final String endpoint = Store.endpoint;
        new Thread(new Runnable() {
            public void run() {
                try {
                    final String text = Cloud.downloadText(endpoint, song.lyricPath);
                    writeFile(Store.lyricCacheFile(MainActivity.this, song), text);
                    // 歌词也和音频一样：默认只留正在听的和下一首
                    Store.pruneLyricCache(MainActivity.this);
                    ui.post(new Runnable() {
                        public void run() {
                            lyricPendingPath = "";
                            setLyrics(song, text);
                        }
                    });
                } catch (final Exception e) {
                    ui.post(new Runnable() {
                        public void run() {
                            lyricPendingPath = "";
                            lyricLoadedPath = song.cloudPath;
                            lyricState.setText("");
                            clearLyricRows("歌词获取失败：" + Util.shorten(e.getMessage()));
                        }
                    });
                }
            }
        }).start();
    }

    private void setLyrics(Song song, String text) {
        Lrc parsed = Lrc.parse(text);
        lyricLoadedPath = song.cloudPath;
        lyricsSynced = parsed.synced;
        lyricLines.clear();
        lyricLines.addAll(parsed.lines);
        lyricIndex = -1;
        autoScrolling = false;
        manualScrollUntil = 0;
        if (backToCurrent != null) backToCurrent.setVisibility(View.GONE);
        lyricBox.removeAllViews();
        lyricRows.clear();
        if (lyricLines.isEmpty()) {
            lyricState.setText("");
            clearLyricRows("歌词文件是空的。");
            return;
        }
        lyricState.setText(parsed.synced ? "" : "这是纯文本歌词，不跟时间走");
        final int pad = Math.max(dp(60), lyricScroll.getHeight() / 2 - dp(40));
        lyricBox.setPadding(dp(16), pad, dp(16), pad);
        for (int i = 0; i < lyricLines.size(); i++) {
            final Lrc.Line line = lyricLines.get(i);
            LinearLayout item = column();
            item.setPadding(0, dp(9), 0, dp(9));
            TextView original = text(line.text, 16, cText);
            // 歌词行居中显示（和电脑版歌词页一致）
            original.setGravity(Gravity.CENTER);
            item.addView(original, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
            if (line.hasTranslation()) {
                TextView trans = text(line.translation, 14, cDim);
                trans.setGravity(Gravity.CENTER);
                trans.setPadding(0, dp(3), 0, 0);
                item.addView(trans, new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
            }
            final int index = i;
            item.setOnClickListener(new View.OnClickListener() {
                public void onClick(View v) {
                    PlayerService service = PlayerService.instance;
                    if (service == null || !lyricsSynced) return;
                    // 点某一句＝跳过去听，顺便恢复自动跟随
                    manualScrollUntil = 0;
                    backToCurrent.setVisibility(View.GONE);
                    double offset = Store.lyricOffset(MainActivity.this, Store.current());
                    service.seekTo(Math.max(0, line.time - offset));
                }
            });
            lyricBox.addView(item);
            lyricRows.add(item);
        }
        applyLyricHighlight(PlayerService.instance == null ? 0 : PlayerService.instance.position());
    }

    private void clearLyricRows(String message) {
        if (lyricBox == null) return;
        lyricBox.removeAllViews();
        lyricRows.clear();
        lyricLines.clear();
        lyricsSynced = false;
        TextView t = text(message, 14, cDim);
        t.setGravity(Gravity.CENTER);
        t.setPadding(0, dp(40), 0, dp(40));
        lyricBox.addView(t);
    }

    private void applyLyricHighlight(double position) {
        if (lyricRows.isEmpty() || !lyricsSynced) return;
        Song song = Store.current();
        if (song == null) return;
        double offset = Store.lyricOffset(this, song);
        int index = 0;
        double time = position - offset;
        for (int i = 0; i < lyricLines.size(); i++) {
            if (lyricLines.get(i).time <= time) index = i;
            else break;
        }
        if (index == lyricIndex) {
            updateBackToCurrent();
            return;
        }
        int previous = lyricIndex;
        lyricIndex = index;
        if (previous >= 0 && previous < lyricRows.size()) {
            setLineState(lyricRows.get(previous), false);
        }
        setLineState(lyricRows.get(index), true);
        if (System.currentTimeMillis() < manualScrollUntil) {
            updateBackToCurrent();
            return;
        }
        scrollToCurrentLine(true);
        updateBackToCurrent();
    }

    /** 平滑滚到当前正在唱的那一句（「回到当前歌词」按钮和自动跟随都用它）。 */
    private void scrollToCurrentLine(boolean smooth) {
        if (lyricRows.isEmpty() || lyricIndex < 0 || lyricIndex >= lyricRows.size()) return;
        View target = lyricRows.get(lyricIndex);
        int top = target.getTop() - (lyricScroll.getHeight() - target.getHeight()) / 2;
        if (top < 0) top = 0;
        autoScrolling = true;
        if (smooth) lyricScroll.smoothScrollTo(0, top);
        else lyricScroll.scrollTo(0, top);
        // 动画大概几百毫秒，结束后再允许按钮跟随滚动位置更新
        lyricScroll.postDelayed(new Runnable() {
            public void run() {
                autoScrolling = false;
                updateBackToCurrent();
            }
        }, 700);
    }

    /**
     * 滚动位置离当前句太远时，浮出「回到当前歌词」按钮；回到当前句附近就收起来。
     * 安卓这边手动滑动只是暂停 4 秒跟随，所以这个按钮主要是让用户随时能一键回到当前句。
     */
    private void updateBackToCurrent() {
        if (backToCurrent == null || lyricRows.isEmpty() || lyricIndex < 0
                || lyricIndex >= lyricRows.size()) return;
        View target = lyricRows.get(lyricIndex);
        int want = target.getTop() - (lyricScroll.getHeight() - target.getHeight()) / 2;
        if (want < 0) want = 0;
        // 自动滚动动画途中不更新，避免「换行的一瞬间闪一下」
        if (autoScrolling) return;
        boolean away = Math.abs(lyricScroll.getScrollY() - want) > dp(40);
        backToCurrent.setVisibility(away ? View.VISIBLE : View.GONE);
    }

    private void setLineState(LinearLayout item, boolean active) {
        for (int i = 0; i < item.getChildCount(); i++) {
            View child = item.getChildAt(i);
            if (!(child instanceof TextView)) continue;
            TextView t = (TextView) child;
            if (active) {
                t.setTextColor(cAccent);
                t.setTextSize(i == 0 ? 17 : 15);
            } else {
                t.setTextColor(i == 0 ? cText : cDim);
                t.setTextSize(i == 0 ? 16 : 14);
            }
        }
    }

    private String readFile(File file) {
        try {
            byte[] data = new byte[(int) file.length()];
            java.io.FileInputStream in = new java.io.FileInputStream(file);
            int read = 0;
            while (read < data.length) {
                int n = in.read(data, read, data.length - read);
                if (n <= 0) break;
                read += n;
            }
            in.close();
            return Util.decodeText(data);
        } catch (Exception e) {
            return "";
        }
    }

    private void writeFile(File file, String text) {
        try {
            File parent = file.getParentFile();
            if (parent != null && !parent.exists()) parent.mkdirs();
            FileOutputStream out = new FileOutputStream(file);
            out.write(text.getBytes("UTF-8"));
            out.close();
        } catch (Exception e) {
            // 写不进缓存不影响播放
        }
    }

    // ---------------------------------------------------------------- 设置页

    private View buildSettingsPage() {
        ScrollView scroll = new ScrollView(this);
        LinearLayout box = column();
        box.setPadding(dp(12), dp(12), dp(12), dp(20));
        scroll.addView(box, new android.widget.FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        // 云盘
        LinearLayout cloud = card();
        cloud.addView(cardTitle("云盘连接"));
        cloud.addView(hint("支持清华云盘等 Seafile 站点。填「分享链接」或「资料库 API 令牌」，两个都填时优先用令牌；"
                + "令牌还能删除云端文件。什么都不填只填令牌也可以。"));
        linkInput = input("分享链接，例如 https://cloud.tsinghua.edu.cn/d/xxxx/", Prefs.link(this));
        cloud.addView(linkInput);
        tokenInput = input("资料库 API 令牌（40 位十六进制）", Prefs.token(this));
        cloud.addView(tokenInput);
        LinearLayout cloudButtons = row();
        cloudButtons.setPadding(0, dp(4), 0, 0);
        cloudButtons.addView(button("粘贴", false, new View.OnClickListener() {
            public void onClick(View v) {
                pasteFromClipboard();
            }
        }));
        cloudButtons.addView(button("测试连接", false, new View.OnClickListener() {
            public void onClick(View v) {
                testConnection();
            }
        }));
        cloudButtons.addView(button("保存并刷新", true, new View.OnClickListener() {
            public void onClick(View v) {
                saveEndpoint();
            }
        }));
        cloud.addView(cloudButtons);
        connInfo = text("", 12, cDim);
        connInfo.setPadding(0, dp(8), 0, 0);
        cloud.addView(connInfo);
        box.addView(cloud);

        // 播放与缓存
        LinearLayout play = card();
        play.addView(cardTitle("本地占用与缓存"));
        cacheSwitch = new Switch(this);
        cacheSwitch.setText("把听过的歌缓存到本机（可离线播放）");
        cacheSwitch.setTextSize(14);
        cacheSwitch.setTextColor(cText);
        cacheSwitch.setChecked(Store.cacheAll());
        cacheSwitch.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                applyCacheMode(cacheSwitch.isChecked() ? Store.CACHE_ALL : Store.CACHE_WINDOW);
            }
        });
        play.addView(cacheSwitch);
        cacheModeHint = text("", 12, cDim);
        cacheModeHint.setPadding(0, dp(8), 0, 0);
        play.addView(cacheModeHint);
        LinearLayout cacheRow = row();
        LinearLayout.LayoutParams cacheRowP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        cacheRowP.topMargin = dp(8);
        cacheRow.setLayoutParams(cacheRowP);
        cacheInfo = text("", 13, cText);
        cacheRow.addView(cacheInfo, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        cacheRow.addView(button("清除缓存", false, new View.OnClickListener() {
            public void onClick(View v) {
                confirmClearCache();
            }
        }));
        play.addView(cacheRow);
        LinearLayout scanRow = row();
        scanRow.addView(button("重新扫描曲库", false, new View.OnClickListener() {
            public void onClick(View v) {
                startScan(true);
            }
        }));
        play.addView(scanRow);
        box.addView(play);

        // 外观
        LinearLayout appearance = card();
        appearance.addView(cardTitle("外观"));
        appearance.addView(hint("默认跟随系统，也可以手动固定。"));
        LinearLayout themeRow = row();
        String[] themeNames = { "跟随系统", "浅色", "深色" };
        for (int i = 0; i < 3; i++) {
            final int mode = i;
            TextView t = text(themeNames[i], 13, cText);
            t.setGravity(Gravity.CENTER);
            t.setPadding(0, dp(10), 0, dp(10));
            LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(0, dp(40), 1f);
            p.rightMargin = i == 2 ? 0 : dp(8);
            t.setLayoutParams(p);
            t.setOnClickListener(new View.OnClickListener() {
                public void onClick(View v) {
                    applyThemeMode(mode);
                }
            });
            themeButtons[i] = t;
            themeRow.addView(t);
        }
        appearance.addView(themeRow);
        box.addView(appearance);

        // 曲库维护
        LinearLayout library = card();
        library.addView(cardTitle("曲库维护"));
        hiddenInfo = text("", 12, cDim);
        hiddenInfo.setPadding(0, 0, 0, dp(8));
        library.addView(hiddenInfo);
        LinearLayout hiddenRow = row();
        hiddenRow.addView(button("恢复已隐藏的歌曲", false, new View.OnClickListener() {
            public void onClick(View v) {
                Store.setHiddenSet(MainActivity.this, new java.util.HashSet<String>());
                refreshSettings();
                startScan(false);
                toast("已恢复全部隐藏歌曲");
            }
        }));
        library.addView(hiddenRow);
        box.addView(library);

        // 关于
        LinearLayout about = card();
        about.addView(cardTitle("关于"));
        TextView version = text("云雀 · Android 版 v" + VERSION, 13, cText);
        version.setPadding(0, 0, 0, dp(6));
        about.addView(version);
        about.addView(hint("Windows 版与 Android 版共用同一个云盘曲库：歌名和歌词都放在云盘上，"
                + "这个 App 只负责取回来播放，换设备不用重新整理。\n"
                + "APK 没有上架应用商店、也没有购买签名证书，安装时系统会提示"
                + "「未知来源 / 未知开发者」，允许安装即可。"));
        TextView repo = text("项目主页：github.com/G336ncx-bjx/skylark-music", 13, cAccent);
        repo.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                try {
                    startActivity(new Intent(Intent.ACTION_VIEW,
                            Uri.parse("https://github.com/G336ncx-bjx/skylark-music")));
                } catch (Exception e) {
                    toast("没有可用的浏览器");
                }
            }
        });
        about.addView(repo);

        // 更新也放在「关于」这张卡片里，一起在最下面
        updateStatus = text("当前已是最新（" + VERSION + "）", 13, cText);
        updateStatus.setPadding(0, dp(10), 0, 0);
        about.addView(updateStatus);
        LinearLayout updateRow = row();
        updateRow.addView(button("检查更新", true, new View.OnClickListener() {
            public void onClick(View v) {
                checkUpdate();
            }
        }));
        about.addView(updateRow);
        about.addView(hint("点「检查更新」才会联网（平时不会自己检查）。发现新版本后"
                + "在应用里直接下载安装（更新包放在云盘上专门的 apk 仓库里），"
                + "装完会自动删掉安装包；下载下来的包会先校验版本和签名，不符就直接丢弃。"));
        box.addView(about);

        return scroll;
    }

    private void refreshSettings() {
        if (hiddenInfo == null) return;
        Set<String> hidden = Store.hiddenSet(this);
        hiddenInfo.setText(hidden.isEmpty() ? "没有隐藏的歌曲。"
                : "音乐库里隐藏了 " + hidden.size() + " 首（文件还在云盘上）。");
        updateCacheInfo();
        updateCacheButtons();
        updateConnInfo();
        updateThemeButtons();
    }

    private void applyCacheMode(int mode) {
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

    private void updateCacheButtons() {
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

    private void updateCacheInfo() {
        if (cacheInfo == null) return;
        long size = Store.cacheSize(this);
        String text = String.format(Locale.ROOT, "缓存占用 %.1f MB · %d 个文件",
                size / 1024.0 / 1024.0, Store.cacheCount(this));
        cacheInfo.setText(text);
    }

    private void updateConnInfo() {
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

    private void updateThemeButtons() {
        int mode = Prefs.themeMode(this);
        for (int i = 0; i < 3; i++) {
            if (themeButtons[i] == null) continue;
            boolean active = i == mode;
            themeButtons[i].setTextColor(active ? 0xFFFFFFFF : cText);
            themeButtons[i].setBackground(round(active ? cAccent : cAlt, 10));
        }
    }

    private void applyThemeMode(int mode) {
        if (Prefs.themeMode(this) == mode) return;
        Prefs.setThemeMode(this, mode);
        if (Prefs.isDark(this) != dark) recreate();
        else updateThemeButtons();
    }

    private void pasteFromClipboard() {
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

    private void saveEndpoint() {
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

    private void testConnection() {
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

    private void confirmClearCache() {
        new AlertDialog.Builder(this)
                .setTitle("清除缓存？")
                .setMessage("只是删掉本机缓存，云端文件不受影响。")
                .setNegativeButton("取消", null)
                .setPositiveButton("清除", new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        Store.clearCache(MainActivity.this);
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
    private void checkUpdate() {
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

    private void showUpdateDialog(final Update.Found found) {
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

    private void startUpdate(final Update.Found found) {
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
                            if (!Update.verify(MainActivity.this, target, found.version)) {
                                if (target.exists()) target.delete();
                                updateStatus.setText("安装包校验没通过，已删除");
                                toast("安装包校验没通过（版本或签名不符），已删除");
                                return;
                            }
                            toast("下载完成，请在系统提示里确认安装");
                            Update.install(MainActivity.this, target);
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

    // ---------------------------------------------------------------- 扫描云盘

    private void startScan(final boolean goToLibrary) {
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
                    Map<String, String> lyrics = new HashMap<String, String>();
                    Map<String, String> lyricStamps = new HashMap<String, String>();
                    for (int i = 0; i < entries.size(); i++) {
                        Cloud.Entry entry = entries.get(i);
                        if (entry.dir) continue;
                        String ext = extOf(entry.name);
                        if ("lrc".equals(ext)) {
                            lyrics.put(baseOf(entry.name).toLowerCase(Locale.ROOT), entry.path);
                            lyricStamps.put(baseOf(entry.name).toLowerCase(Locale.ROOT), entry.modified);
                        }
                    }
                    int skipped = 0;
                    Set<String> hidden = Store.hiddenSet(MainActivity.this);
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
                        song.duration = Store.durationOf(MainActivity.this, song);
                        found.add(song);
                    }
                    final int skippedCount = skipped;
                    final String repo = Cloud.isToken(endpoint) ? Cloud.repoName(endpoint) : "";
                    ui.post(new Runnable() {
                        public void run() {
                            if (token != scanToken) return;
                            Store.songs.clear();
                            Store.songs.addAll(found);
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

    /** 后台补齐 MP3 时长（取每个文件前 64 KB 解析帧头）。 */
    private void probeDurations(final int token) {
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
                            Store.setDuration(MainActivity.this, s, duration);
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

    private String extOf(String name) {
        if (name == null) return "";
        int dot = name.lastIndexOf('.');
        if (dot < 0 || dot == name.length() - 1) return "";
        return name.substring(dot + 1).toLowerCase(Locale.ROOT);
    }

    private String baseOf(String name) {
        if (name == null) return "";
        int dot = name.lastIndexOf('.');
        return dot < 0 ? name : name.substring(0, dot);
    }

    private boolean isAudio(String ext) {
        for (int i = 0; i < AUDIO_EXT.length; i++) {
            if (AUDIO_EXT[i].equals(ext)) return true;
        }
        return false;
    }

    private boolean isIgnored(String ext) {
        for (int i = 0; i < IGNORED_EXT.length; i++) {
            if (IGNORED_EXT[i].equals(ext)) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- 上传

    private void pickFiles() {
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

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQ_PICK || resultCode != RESULT_OK || data == null) return;
        List<Uri> uris = new ArrayList<Uri>();
        ClipData clip = data.getClipData();
        if (clip != null) {
            for (int i = 0; i < clip.getItemCount(); i++) uris.add(clip.getItemAt(i).getUri());
        } else if (data.getData() != null) {
            uris.add(data.getData());
        }
        uploadFiles(uris);
    }

    private void handleIntent(Intent intent) {
        if (intent == null) return;
        String action = intent.getAction();
        List<Uri> uris = new ArrayList<Uri>();
        if (Intent.ACTION_SEND.equals(action)) {
            Uri uri = intent.getParcelableExtra(Intent.EXTRA_STREAM);
            if (uri != null) uris.add(uri);
        } else if (Intent.ACTION_SEND_MULTIPLE.equals(action)) {
            List<Uri> list = intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM);
            if (list != null) uris.addAll(list);
        }
        if (!uris.isEmpty()) {
            selectTab(0);
            uploadFiles(uris);
        }
    }

    /** 上传目标：当前选中的歌单；没选歌单就用「默认歌单」；都没有才传根目录。 */
    private String uploadTargetDir() {
        if (playlistFilter.length() > 0) return "/" + playlistFilter;
        List<String> names = playlistNames();
        if (names.contains("默认歌单")) return "/默认歌单";
        return "/";
    }

    private void uploadFiles(final List<Uri> uris) {
        if (uris == null || uris.isEmpty()) return;
        final String endpoint = Store.endpoint;
        if (endpoint.length() == 0) {
            toast("先在「设置」里连接云盘，才能上传");
            return;
        }
        if (uploading) {
            toast("上一批还在上传，等它传完");
            return;
        }
        uploading = true;
        final String target = uploadTargetDir();
        toast("开始上传 " + uris.size() + " 个文件到"
                + ("/".equals(target) ? "云盘根目录" : "歌单「" + target.substring(1) + "」"));
        new Thread(new Runnable() {
            public void run() {
                int ok = 0, fail = 0;
                String lastError = "";
                boolean dirReady = "/".equals(target);
                final String targetDir = target;
                for (int i = 0; i < uris.size(); i++) {
                    final int index = i + 1;
                    final String name = displayName(uris.get(i));
                    postInfo("正在上传 " + index + "/" + uris.size() + "：" + name);
                    File temp = null;
                    try {
                        if (!dirReady) {
                            Cloud.ensureDir(endpoint, targetDir, getCacheDir());
                            dirReady = true;
                        }
                        temp = copyToUploadDir(uris.get(i));
                        final long total = temp.length();
                        Cloud.upload(endpoint, temp, targetDir, new Util.Progress() {
                            private long lastPercent = -5;

                            public void onProgress(long done, long totalBytes) {
                                long size = totalBytes > 0 ? totalBytes : total;
                                int percent = size > 0 ? (int) (done * 100 / size) : 0;
                                if (percent >= lastPercent + 5) {
                                    lastPercent = percent;
                                    postInfo("正在上传 " + index + "/" + uris.size() + "：" + name
                                            + " " + percent + "%");
                                }
                            }
                        });
                        ok++;
                    } catch (Exception e) {
                        fail++;
                        lastError = Util.shorten(e.getMessage());
                    } finally {
                        if (temp != null) temp.delete();
                    }
                }
                uploading = false;
                final int success = ok, failed = fail;
                final String error = lastError;
                ui.post(new Runnable() {
                    public void run() {
                        Store.status = "上传完成：成功 " + success + " 个"
                                + (failed > 0 ? "，失败 " + failed + " 个（" + error + "）" : "");
                        toast(Store.status);
                        startScan(true);
                    }
                });
            }
        }).start();
    }

    private String displayName(Uri uri) {
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

    private File copyToUploadDir(Uri uri) throws Exception {
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

    // ---------------------------------------------------------------- 底部播放条

    private View buildPlayerBar() {
        LinearLayout wrapper = column();
        View divider = new View(this);
        divider.setBackgroundColor(cDivider);
        wrapper.addView(divider, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 1));

        LinearLayout bar = column();
        bar.setBackgroundColor(cSurface);
        bar.setPadding(dp(14), dp(10), dp(14), dp(12));
        wrapper.addView(bar);

        LinearLayout titleRow = row();
        LinearLayout names = column();
        nowTitle = text("还没有播放歌曲", 15, cText);
        nowTitle.setSingleLine(true);
        nowTitle.setEllipsize(android.text.TextUtils.TruncateAt.END);
        nowArtist = text("到「音乐库」点歌名就会开始播放", 12, cDim);
        nowArtist.setSingleLine(true);
        nowArtist.setEllipsize(android.text.TextUtils.TruncateAt.END);
        names.addView(nowTitle);
        names.addView(nowArtist);
        titleRow.addView(names, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        modeText = text(Store.modeName(1), 12, cAccent);
        modeText.setPadding(dp(10), dp(6), dp(6), dp(6));
        modeText.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                PlayerService service = PlayerService.instance;
                if (service == null) return;
                int next = (Store.mode + 1) % 4;
                service.setMode(next);
                modeText.setText(Store.modeName(next));
                toast("播放模式：" + Store.modeName(next));
            }
        });
        titleRow.addView(modeText);
        bar.addView(titleRow);

        LinearLayout progressRow = row();
        LinearLayout.LayoutParams progressP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        progressP.topMargin = dp(6);
        progressRow.setLayoutParams(progressP);
        timeNow = text("0:00", 11, cDim);
        timeNow.setGravity(Gravity.CENTER);
        progressRow.addView(timeNow, new LinearLayout.LayoutParams(dp(40),
                ViewGroup.LayoutParams.WRAP_CONTENT));
        seek = new SeekBar(this);
        seek.setMax(1000);
        seek.setProgress(0);
        seek.setProgressDrawable(progressDrawable());
        seek.setThumb(thumbDrawable());
        seek.setSplitTrack(false);
        seek.setMinimumHeight(dp(34));
        LinearLayout.LayoutParams seekP = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        seekP.leftMargin = dp(6);
        seekP.rightMargin = dp(6);
        seek.setLayoutParams(seekP);
        // 左右留出圆点的位置，否则滑到两端会被切掉
        seek.setPadding(dp(9), dp(12), dp(9), dp(12));
        seek.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            public void onProgressChanged(SeekBar bar, int value, boolean fromUser) {
                if (!fromUser) return;
                double total = currentDuration();
                timeNow.setText(Util.formatTime(total * value / 1000.0));
            }

            public void onStartTrackingTouch(SeekBar bar) {
                dragging = true;
            }

            public void onStopTrackingTouch(SeekBar bar) {
                dragging = false;
                PlayerService service = PlayerService.instance;
                double total = currentDuration();
                if (service != null && total > 0) service.seekTo(total * bar.getProgress() / 1000.0);
            }
        });
        progressRow.addView(seek);
        timeTotal = text("0:00", 11, cDim);
        timeTotal.setGravity(Gravity.CENTER);
        progressRow.addView(timeTotal, new LinearLayout.LayoutParams(dp(40),
                ViewGroup.LayoutParams.WRAP_CONTENT));
        bar.addView(progressRow);

        LinearLayout controls = row();
        controls.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams controlsP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        controlsP.topMargin = dp(6);
        controls.setLayoutParams(controlsP);

        ImageView prev = iconButton(R.drawable.ic_prev, 40, false);
        prev.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                PlayerService service = PlayerService.instance;
                if (service != null) service.prev();
            }
        });
        controls.addView(prev);

        playIcon = iconButton(R.drawable.ic_play, 52, true);
        playIcon.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                PlayerService service = PlayerService.instance;
                if (service == null) {
                    startPlayerService();
                    toast("正在启动播放服务，再点一次");
                    return;
                }
                service.toggle();
                ui.postDelayed(new Runnable() {
                    public void run() {
                        refreshNowPlaying();
                    }
                }, 150);
            }
        });
        controls.addView(playIcon);

        ImageView next = iconButton(R.drawable.ic_next, 40, false);
        next.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                PlayerService service = PlayerService.instance;
                if (service != null) service.next(true);
            }
        });
        controls.addView(next);
        bar.addView(controls);
        return wrapper;
    }

    private Drawable progressDrawable() {
        GradientDrawable track = new GradientDrawable();
        track.setColor(cTrack);
        track.setCornerRadius(dp(4));
        track.setSize(-1, dp(8));
        GradientDrawable fill = new GradientDrawable();
        fill.setColor(cAccent);
        fill.setCornerRadius(dp(4));
        fill.setSize(-1, dp(8));
        LayerDrawable layer = new LayerDrawable(new Drawable[] { track, fill });
        layer.setId(0, android.R.id.background);
        layer.setId(1, android.R.id.progress);
        // 槽与已播放部分用同样的上下内缩，保证两端对齐（14 = 18dp 圆点 - 4dp 轨道）
        layer.setLayerInset(0, 0, dp(7), 0, dp(7));
        layer.setLayerInset(1, 0, dp(7), 0, dp(7));
        return layer;
    }

    private Drawable thumbDrawable() {
        GradientDrawable thumb = new GradientDrawable();
        thumb.setShape(GradientDrawable.OVAL);
        thumb.setColor(cAccent);
        thumb.setStroke(dp(2), cSurface);
        thumb.setSize(dp(18), dp(18));
        return thumb;
    }

    private ImageView iconButton(int res, int size, boolean filled) {
        ImageView v = new ImageView(this);
        v.setImageResource(res);
        int pad = dp(filled ? 12 : 8);
        v.setPadding(pad, pad, pad, pad);
        v.setColorFilter(filled ? 0xFFFFFFFF : cText);
        if (filled) v.setBackground(round(cAccent, 100));
        v.setClickable(true);
        v.setFocusable(true);
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(dp(size), dp(size));
        p.leftMargin = dp(10);
        p.rightMargin = dp(10);
        v.setLayoutParams(p);
        return v;
    }

    private double currentDuration() {
        Song song = Store.current();
        if (song != null && song.duration > 0) return song.duration;
        PlayerService service = PlayerService.instance;
        return service == null ? 0 : service.duration();
    }

    private void refreshNowPlaying() {
        if (nowTitle == null) return;
        Song song = Store.current();
        if (song == null) {
            nowTitle.setText("还没有播放歌曲");
            nowArtist.setText("到「音乐库」点歌名就会开始播放");
        } else {
            nowTitle.setText(song.title);
            PlayerService service = PlayerService.instance;
            if (service != null && service.isBuffering()) nowArtist.setText(service.bufferingText());
            else nowArtist.setText(song.artistText() + " · " + Store.modeName(Store.mode));
        }
        if (modeText != null) modeText.setText(Store.modeName(Store.mode));
        PlayerService service = PlayerService.instance;
        boolean playing = service != null && service.isPlaying();
        playIcon.setImageResource(playing ? R.drawable.ic_pause : R.drawable.ic_play);
        updateProgress();
    }

    private void updateProgress() {
        if (seek == null) return;
        PlayerService service = PlayerService.instance;
        double total = currentDuration();
        double position = service == null ? 0 : service.position();
        if (!dragging) {
            int value = total > 0 ? (int) Math.round(position / total * 1000) : 0;
            seek.setProgress(Math.max(0, Math.min(1000, value)));
            timeNow.setText(Util.formatTime(position));
        }
        timeTotal.setText(total > 0 ? Util.formatTime(total) : "--:--");
        if (tabIndex == 2) {
            Song song = Store.current();
            String path = song == null ? "" : song.cloudPath;
            if (!path.equals(lyricLoadedPath) && !path.equals(lyricPendingPath)) refreshLyrics(false);
            else applyLyricHighlight(position);
        }
    }

    private final Runnable ticker = new Runnable() {
        public void run() {
            updateProgress();
            ui.postDelayed(this, 500);
        }
    };

    /**
     * 歌词单独用更快的节拍刷新：进度条每 500ms 刷一次就够了，但歌词跟着这个节拍走
     * 会最多晚半秒才跳到下一句（听起来就是「唱到了才慢慢换句」）。
     */
    private final Runnable lyricTicker = new Runnable() {
        public void run() {
            if (PlayerService.instance != null && !lyricRows.isEmpty()) {
                applyLyricHighlight(PlayerService.instance.position());
            }
            ui.postDelayed(this, 120);
        }
    };

    // ---------------------------------------------------------------- 列表适配器

    private static class RowHolder {
        View bar;
        TextView index, title, artist, time;
    }

    private class SongAdapter extends BaseAdapter {
        private final List<Song> data = new ArrayList<Song>();
        /** 音乐库列表不高亮正在播放的那首（那是播放队列的事）。 */
        private final boolean highlightCurrent;
        /** 多选管理：长按列表进入，之后点一行切换选中。 */
        private boolean selecting;
        private final Set<String> picked = new java.util.HashSet<String>();

        SongAdapter(boolean highlightCurrent) {
            this.highlightCurrent = highlightCurrent;
        }

        void setData(List<Song> source) {
            data.clear();
            data.addAll(source);
            notifyDataSetChanged();
        }

        boolean isSelecting() { return selecting; }

        void setSelecting(boolean value) {
            selecting = value;
            picked.clear();
            notifyDataSetChanged();
        }

        int pickedCount() { return picked.size(); }

        void toggle(Song song) {
            if (song == null) return;
            if (!picked.remove(song.cloudPath)) picked.add(song.cloudPath);
            notifyDataSetChanged();
        }

        void selectAll() {
            for (int i = 0; i < data.size(); i++) picked.add(data.get(i).cloudPath);
            notifyDataSetChanged();
        }

        void clearPicked() {
            picked.clear();
            notifyDataSetChanged();
        }

        /** 当前列表是不是全勾上了（「取消全选」用）。 */
        boolean allPicked() {
            if (data.isEmpty()) return false;
            for (int i = 0; i < data.size(); i++) {
                if (!picked.contains(data.get(i).cloudPath)) return false;
            }
            return true;
        }

        List<Song> pickedSongs() {
            List<Song> out = new ArrayList<Song>();
            for (int i = 0; i < data.size(); i++) {
                Song s = data.get(i);
                if (picked.contains(s.cloudPath)) out.add(s);
            }
            return out;
        }

        public int getCount() { return data.size(); }

        public Object getItem(int position) { return data.get(position); }

        public long getItemId(int position) { return position; }

        public View getView(int position, View convertView, ViewGroup parent) {
            LinearLayout view;
            if (convertView instanceof LinearLayout) {
                view = (LinearLayout) convertView;
            } else {
                view = buildRowView();
            }
            RowHolder holder = (RowHolder) view.getTag();
            Song song = data.get(position);
            Song current = Store.current();
            boolean active = highlightCurrent && current != null && current.cloudPath.equals(song.cloudPath);
            boolean pickedRow = selecting && picked.contains(song.cloudPath);
            view.setBackgroundColor(pickedRow ? cAlt : cSurface);
            holder.index.setText(pickedRow ? "✓" : String.valueOf(position + 1));
            holder.index.setTextColor(pickedRow ? cAccent : (active ? cAccent : cDim));
            holder.title.setText(song.title);
            holder.title.setTextColor(active ? cAccent : cText);
            holder.bar.setVisibility(active ? View.VISIBLE : View.INVISIBLE);
            double duration = song.duration > 0 ? song.duration : Store.durationOf(MainActivity.this, song);
            holder.artist.setText(song.artistText());
            holder.time.setText(duration > 0 ? Util.formatTime(duration) : "--:--");
            return view;
        }
    }

    private LinearLayout buildRowView() {
        LinearLayout root = row();
        root.setBackgroundColor(cSurface);
        root.setPadding(dp(10), dp(10), dp(14), dp(10));

        View bar = new View(this);
        bar.setBackgroundColor(cAccent);
        LinearLayout.LayoutParams barP = new LinearLayout.LayoutParams(dp(3),
                ViewGroup.LayoutParams.MATCH_PARENT);
        barP.rightMargin = dp(10);
        root.addView(bar, barP);

        TextView index = text("1", 12, cDim);
        index.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams indexP = new LinearLayout.LayoutParams(dp(26),
                ViewGroup.LayoutParams.WRAP_CONTENT);
        indexP.rightMargin = dp(8);
        root.addView(index, indexP);

        LinearLayout middle = column();
        root.addView(middle, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        TextView title = text("", 15, cText);
        title.setSingleLine(true);
        title.setEllipsize(android.text.TextUtils.TruncateAt.END);
        TextView artist = text("", 12, cDim);
        artist.setSingleLine(true);
        artist.setEllipsize(android.text.TextUtils.TruncateAt.END);
        middle.addView(title);
        middle.addView(artist);

        TextView time = text("", 12, cDim);
        time.setGravity(Gravity.RIGHT);
        LinearLayout.LayoutParams timeP = new LinearLayout.LayoutParams(dp(44),
                ViewGroup.LayoutParams.WRAP_CONTENT);
        timeP.leftMargin = dp(8);
        root.addView(time, timeP);

        RowHolder holder = new RowHolder();
        holder.bar = bar;
        holder.index = index;
        holder.title = title;
        holder.artist = artist;
        holder.time = time;
        root.setTag(holder);
        return root;
    }

    // ---------------------------------------------------------------- 杂项

    private void startPlayerService() {
        if (starting) return;
        starting = true;
        try {
            Intent intent = new Intent(this, PlayerService.class);
            if (Build.VERSION.SDK_INT >= 26) startForegroundService(intent);
            else startService(intent);
        } catch (Exception e) {
            toast("启动播放服务失败：" + Util.shorten(e.getMessage()));
        }
        ui.postDelayed(new Runnable() {
            public void run() {
                starting = false;
            }
        }, 1500);
    }

    private void requestNotificationPermission() {
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

    private void toast(String message) {
        if (message == null || message.length() == 0) return;
        Toast.makeText(this, message, Toast.LENGTH_SHORT).show();
    }

    private void toastAsync(final String message) {
        ui.post(new Runnable() {
            public void run() {
                toast(message);
            }
        });
    }

    private void postInfo(final String message) {
        ui.post(new Runnable() {
            public void run() {
                updateLibraryInfo(message);
            }
        });
    }
}
