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

public class MainActivity extends SettingsScreen {

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


    // ---------------------------------------------------------------- 主框架

    protected View buildRoot() {
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


    protected void handleIntent(Intent intent) {
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

}
