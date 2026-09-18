package com.skylark.music;

import android.content.Context;
import android.content.SharedPreferences;
import android.content.res.Configuration;

import org.json.JSONObject;

/** 设置与播放状态的持久化。 */
public class Prefs {
    private static final String FILE = "skylark";

    private static SharedPreferences sp(Context c) {
        return c.getSharedPreferences(FILE, Context.MODE_PRIVATE);
    }

    // ---------- 云盘 ----------

    /** 令牌优先；只有 40 位十六进制令牌才算令牌，否则用分享链接。 */
    public static String endpoint(Context c) {
        String token = sp(c).getString("token", "");
        if (token != null && token.length() == 40) return token;
        String link = sp(c).getString("link", "");
        return link == null ? "" : link;
    }

    public static String link(Context c) { return sp(c).getString("link", ""); }
    public static String token(Context c) { return sp(c).getString("token", ""); }
    public static void setLink(Context c, String v) { sp(c).edit().putString("link", v == null ? "" : v.trim()).apply(); }
    public static void setToken(Context c, String v) { sp(c).edit().putString("token", v == null ? "" : v.trim()).apply(); }

    // ---------- 主题：0 跟随系统 / 1 浅色 / 2 深色 ----------

    public static int themeMode(Context c) { return sp(c).getInt("theme", 0); }
    public static void setThemeMode(Context c, int v) { sp(c).edit().putInt("theme", v).apply(); }

    public static boolean isDark(Context c) {
        int mode = themeMode(c);
        if (mode == 1) return false;
        if (mode == 2) return true;
        int night = c.getResources().getConfiguration().uiMode & Configuration.UI_MODE_NIGHT_MASK;
        return night == Configuration.UI_MODE_NIGHT_YES;
    }

    // ---------- 缓存 ----------

    /** 默认不缓存：直接从云端播放，不在手机里留文件。 */
    /** 本地占用策略：1 只留正在听的和下一首（默认） / 2 听过的歌都留在本机。 */
    public static int cacheMode(Context c) {
        int mode = sp(c).getInt("cacheMode", 1);
        return mode == 2 ? 2 : 1;
    }

    public static void setCacheMode(Context c, int v) { sp(c).edit().putInt("cacheMode", v).apply(); }

    // ---------- 播放 ----------

    public static int mode(Context c) { return sp(c).getInt("mode", 1); }  // 默认列表循环
    public static void setMode(Context c, int v) { sp(c).edit().putInt("mode", v).apply(); }

    public static String queue(Context c) { return sp(c).getString("queue", "[]"); }
    public static void setQueue(Context c, String v) { sp(c).edit().putString("queue", v).apply(); }

    public static int queueIndex(Context c) { return sp(c).getInt("qindex", 0); }
    public static void setQueueIndex(Context c, int v) { sp(c).edit().putInt("qindex", v).apply(); }
    /** 进入随机播放前的队列顺序（JSON 数组），关掉随机时用来还原。 */
    public static String shuffleRestore(Context c) { return sp(c).getString("shuffleRestore", ""); }
    public static void setShuffleRestore(Context c, String v) {
        sp(c).edit().putString("shuffleRestore", v == null ? "" : v).apply();
    }

    public static String lastSong(Context c) { return sp(c).getString("last", ""); }
    public static void setLastSong(Context c, String v) { sp(c).edit().putString("last", v == null ? "" : v).apply(); }

    public static int lastPosition(Context c) { return sp(c).getInt("lastPos", 0); }
    public static void setLastPosition(Context c, int v) { sp(c).edit().putInt("lastPos", v).apply(); }

    // ---------- 曲库 ----------

    /** 0 歌名 / 1 歌手 / 2 时长 / 3 云盘顺序。 */
    public static int sortMode(Context c) { return sp(c).getInt("sort", 0); }
    public static void setSortMode(Context c, int v) { sp(c).edit().putInt("sort", v).apply(); }

    /** 已被移出音乐库的云盘路径，每行一个。 */
    public static String hidden(Context c) { return sp(c).getString("hidden", ""); }
    public static void setHidden(Context c, String v) { sp(c).edit().putString("hidden", v == null ? "" : v).apply(); }


    // ---------- 小表：时长、歌词偏移等按歌曲记的数值 ----------

    public static JSONObject table(Context c, String key) {
        try {
            return new JSONObject(sp(c).getString(key, "{}"));
        } catch (Exception e) {
            return new JSONObject();
        }
    }

    public static void putTable(Context c, String key, JSONObject value) {
        sp(c).edit().putString(key, value == null ? "{}" : value.toString()).apply();
    }

    public static double tableDouble(Context c, String key, String id, double def) {
        return table(c, key).optDouble(id, def);
    }

    public static void putTableDouble(Context c, String key, String id, double value) {
        JSONObject o = table(c, key);
        try {
            o.put(id, value);
        } catch (Exception e) {
            return;
        }
        putTable(c, key, o);
    }
}
