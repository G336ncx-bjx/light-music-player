package com.skylark.music;

import android.content.Context;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/** 全局状态：音乐库、播放队列、播放模式、缓存目录。 */
public class Store {
    public static final List<Song> songs = new ArrayList<Song>();
    public static final List<Song> queue = new ArrayList<Song>();
    public static int index = -1;
    public static int mode = 1;          // 0 顺序 1 列表循环 2 单曲循环 3 随机
    public static String endpoint = "";
    public static boolean cacheEnabled = true;
    public static String repoLabel = "";
    public static int skippedUnsupported = 0;
    public static boolean scanning = false;
    public static String status = "";

    public static Song current() {
        if (index < 0 || index >= queue.size()) return null;
        return queue.get(index);
    }

    public static Song findByPath(String cloudPath) {
        for (int i = 0; i < songs.size(); i++) {
            if (songs.get(i).cloudPath.equals(cloudPath)) return songs.get(i);
        }
        for (int i = 0; i < queue.size(); i++) {
            if (queue.get(i).cloudPath.equals(cloudPath)) return queue.get(i);
        }
        return null;
    }

    public static String modeName(int m) {
        if (m == 0) return "顺序播放";
        if (m == 2) return "单曲循环";
        if (m == 3) return "随机播放";
        return "列表循环";
    }

    public static void load(Context c) {
        endpoint = Prefs.endpoint(c);
        mode = Prefs.mode(c);
        cacheEnabled = Prefs.cacheEnabled(c);
        queue.clear();
        try {
            JSONArray arr = new JSONArray(Prefs.queue(c));
            for (int i = 0; i < arr.length(); i++) {
                JSONObject o = arr.optJSONObject(i);
                if (o != null) queue.add(Song.fromJson(o));
            }
        } catch (Exception e) {
            // 队列损坏就当作空
        }
        index = Prefs.queueIndex(c);
        if (index < 0 || index >= queue.size()) index = queue.isEmpty() ? -1 : 0;
    }

    public static void saveQueue(Context c) {
        JSONArray arr = new JSONArray();
        for (int i = 0; i < queue.size(); i++) arr.put(queue.get(i).toJson());
        Prefs.setQueue(c, arr.toString());
        Prefs.setQueueIndex(c, index);
    }

    /** 缓存文件名：<歌名>.<路径哈希>.<扩展名>。 */
    public static File cacheFile(Context c, Song song) {
        File dir = cacheDir(c);
        String base = song.fileName;
        int dot = base.lastIndexOf('.');
        String ext = dot >= 0 ? base.substring(dot) : "";
        String name = dot >= 0 ? base.substring(0, dot) : base;
        name = name.replaceAll("[\\\\/:*?\"<>|]", "_");
        if (name.length() > 60) name = name.substring(0, 60);
        return new File(dir, name + "." + hash(song.cloudPath) + ext);
    }

    /** 歌词缓存文件。 */
    public static File lyricCacheFile(Context c, Song song) {
        File dir = new File(cacheDir(c), "lyrics");
        if (!dir.exists()) dir.mkdirs();
        return new File(dir, hash(song.cloudPath) + ".lrc");
    }

    // ---------- 按歌曲记的小数据：时长、歌词偏移 ----------

    public static double durationOf(Context c, Song song) {
        return Prefs.tableDouble(c, "durations", song.cloudPath, 0);
    }

    public static void setDuration(Context c, Song song, double seconds) {
        if (seconds <= 0) return;
        song.duration = seconds;
        Prefs.putTableDouble(c, "durations", song.cloudPath, seconds);
    }

    public static double lyricOffset(Context c, Song song) {
        return song == null ? 0 : Prefs.tableDouble(c, "lyricOffsets", song.cloudPath, 0);
    }

    public static void setLyricOffset(Context c, Song song, double seconds) {
        if (song == null) return;
        Prefs.putTableDouble(c, "lyricOffsets", song.cloudPath, seconds);
    }

    // ---------- 从音乐库隐藏 ----------

    public static Set<String> hiddenSet(Context c) {
        Set<String> out = new HashSet<String>();
        String[] parts = Prefs.hidden(c).replace("\r\n", "\n").split("\n");
        for (int i = 0; i < parts.length; i++) {
            String line = parts[i].trim();
            if (line.length() > 0) out.add(line);
        }
        return out;
    }

    public static void setHiddenSet(Context c, Set<String> set) {
        StringBuilder sb = new StringBuilder();
        for (String path : set) {
            if (sb.length() > 0) sb.append('\n');
            sb.append(path);
        }
        Prefs.setHidden(c, sb.toString());
    }

    public static File cacheDir(Context c) {
        File base = c.getExternalFilesDir(null);
        if (base == null) base = c.getFilesDir();
        File dir = new File(base, "cache");
        if (!dir.exists()) dir.mkdirs();
        return dir;
    }

    public static boolean isCached(Context c, Song song) {
        File f = cacheFile(c, song);
        return f.exists() && f.length() > 0 && (song.size <= 0 || f.length() == song.size);
    }

    public static long cacheSize(Context c) {
        long total = 0;
        File[] files = cacheDir(c).listFiles();
        if (files != null) {
            for (int i = 0; i < files.length; i++) total += files[i].length();
        }
        return total;
    }

    public static int cacheCount(Context c) {
        File[] files = cacheDir(c).listFiles();
        return files == null ? 0 : files.length;
    }

    public static void clearCache(Context c) {
        deleteInside(cacheDir(c));
    }

    private static void deleteInside(File dir) {
        File[] files = dir == null ? null : dir.listFiles();
        if (files == null) return;
        for (int i = 0; i < files.length; i++) {
            if (files[i].isDirectory()) deleteInside(files[i]);
            files[i].delete();
        }
    }

    private static String hash(String text) {
        int h = 0x811C9DC5;
        for (int i = 0; text != null && i < text.length(); i++) {
            h ^= text.charAt(i);
            h *= 0x01000193;
        }
        return String.format("%08x", h);
    }
}
