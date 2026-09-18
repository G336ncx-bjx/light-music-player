package com.skylark.music;

import android.content.Context;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/** 全局状态：音乐库、播放队列、播放模式、缓存目录。 */
public class Store {
    /** 本地占用策略：默认只留两首，打开缓存才把听过的歌都留在本机。 */
    public static final int CACHE_WINDOW = 1;  // 留「正在听的那一首 + 下一首」（默认，和电脑版一致）
    public static final int CACHE_ALL = 2;     // 听过的歌都留在本机（可离线播放）

    public static final List<Song> songs = new ArrayList<Song>();
    public static final List<Song> queue = new ArrayList<Song>();
    /** 云盘上的歌单（＝文件夹名，含还没有歌的空歌单）。 */
    public static final List<String> playlists = new ArrayList<String>();
    public static int index = -1;
    public static int mode = 1;          // 0 顺序 1 列表循环 2 单曲循环 3 随机
    public static String endpoint = "";
    public static int cacheMode = CACHE_WINDOW;
    public static String repoLabel = "";
    public static int skippedUnsupported = 0;
    public static boolean scanning = false;
    public static String status = "";

    /** 时长与歌词偏移放在内存里，避免列表每画一行都去解析一次 JSON。 */
    private static final Map<String, Double> durations = new HashMap<String, Double>();
    private static final Map<String, Double> lyricOffsets = new HashMap<String, Double>();

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

    /** 听过的歌都留着（可离线播放）。 */
    public static boolean cacheAll() {
        return cacheMode == CACHE_ALL;
    }

    /** 预取下一首：默认和缓存模式下都预取，切歌才不用等下载。 */
    public static boolean prefetchEnabled() {
        return true;
    }

    /** 只留「正在听的那一首 + 下一首」。 */
    public static boolean keepWindow() {
        return cacheMode == CACHE_WINDOW;
    }

    public static void load(Context c) {
        endpoint = Prefs.endpoint(c);
        mode = Prefs.mode(c);
        cacheMode = Prefs.cacheMode(c);
        durations.clear();
        lyricOffsets.clear();
        loadTable(c, "durations", durations);
        loadTable(c, "lyricOffsets", lyricOffsets);
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
        loadShuffleRestore(c);
    }

    public static void saveQueue(Context c) {
        JSONArray arr = new JSONArray();
        for (int i = 0; i < queue.size(); i++) arr.put(queue.get(i).toJson());
        Prefs.setQueue(c, arr.toString());
        Prefs.setQueueIndex(c, index);
    }

    // ---------- 随机播放：点的时候打乱一次，之后按顺序往下放 ----------

    private static List<String> shuffleRestore;

    private static void loadShuffleRestore(Context c) {
        shuffleRestore = null;
        String text = Prefs.shuffleRestore(c);
        if (text == null || text.length() == 0) return;
        try {
            JSONArray arr = new JSONArray(text);
            List<String> list = new ArrayList<String>();
            for (int i = 0; i < arr.length(); i++) {
                String path = arr.optString(i, "");
                if (path.length() > 0) list.add(path);
            }
            if (!list.isEmpty()) shuffleRestore = list;
        } catch (Exception e) {
            // 解析失败就当没有保存过
        }
    }

    private static void saveShuffleRestore(Context c) {
        if (shuffleRestore == null) {
            Prefs.setShuffleRestore(c, "");
            return;
        }
        JSONArray arr = new JSONArray();
        for (int i = 0; i < shuffleRestore.size(); i++) arr.put(shuffleRestore.get(i));
        Prefs.setShuffleRestore(c, arr.toString());
    }

    /**
     * 点「随机播放」：把播放队列真的打乱一次（当前这首放在最前，不打断），
     * 之后就和列表循环一样按这个顺序往下放。抽签只发生在这一刻。
     */
    public static void enterShuffle(Context c) {
        if (queue.size() <= 1) return;
        if (shuffleRestore == null) {
            shuffleRestore = new ArrayList<String>();
            for (int i = 0; i < queue.size(); i++) shuffleRestore.add(queue.get(i).cloudPath);
            saveShuffleRestore(c);
        }
        Song current = (index >= 0 && index < queue.size()) ? queue.get(index) : null;
        List<Song> rest = new ArrayList<Song>();
        for (int i = 0; i < queue.size(); i++) {
            if (queue.get(i) != current) rest.add(queue.get(i));
        }
        Collections.shuffle(rest);
        queue.clear();
        if (current != null) queue.add(current);
        queue.addAll(rest);
        index = queue.isEmpty() ? -1 : 0;
        saveQueue(c);
    }

    /** 关掉随机播放：按进入随机前记下的顺序还原（当前这首继续放）。 */
    public static void exitShuffle(Context c) {
        if (shuffleRestore == null || shuffleRestore.isEmpty()) return;
        Song current = (index >= 0 && index < queue.size()) ? queue.get(index) : null;
        List<Song> ordered = new ArrayList<Song>();
        for (int i = 0; i < shuffleRestore.size(); i++) {
            String path = shuffleRestore.get(i);
            for (int k = 0; k < queue.size(); k++) {
                Song s = queue.get(k);
                if (path.equals(s.cloudPath) && !ordered.contains(s)) {
                    ordered.add(s);
                    break;
                }
            }
        }
        // 随机之后新加进来的歌，按添加顺序接在后面
        for (int i = 0; i < queue.size(); i++) {
            if (!ordered.contains(queue.get(i))) ordered.add(queue.get(i));
        }
        queue.clear();
        queue.addAll(ordered);
        index = current == null ? -1 : queue.indexOf(current);
        shuffleRestore = null;
        saveShuffleRestore(c);
        saveQueue(c);
    }

    private static void loadTable(Context c, String key, Map<String, Double> target) {
        JSONObject table = Prefs.table(c, key);
        java.util.Iterator<String> keys = table.keys();
        while (keys.hasNext()) {
            String name = keys.next();
            double value = table.optDouble(name, 0);
            if (value != 0) target.put(name, Double.valueOf(value));
        }
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

    /** 歌词缓存目录。 */
    public static File lyricCacheDir(Context c) {
        File dir = new File(cacheDir(c), "lyrics");
        if (!dir.exists()) dir.mkdirs();
        return dir;
    }

    private static final String LYRIC_PREFIX = "l5";

    /** 歌词缓存文件。 */
    public static File lyricCacheFile(Context c, Song song) {
        // 文件名 = 前缀-歌曲哈希-修改时间哈希：带上云盘上歌词文件的修改时间，
        // 云端歌词一改名字就变了，会自动重新下载；歌曲哈希在前，方便按歌曲清理旧版本。
        String stamp = song.lyricModified == null ? "" : song.lyricModified;
        return new File(lyricCacheDir(c),
                LYRIC_PREFIX + "-" + hash(song.cloudPath) + "-" + hash(stamp) + ".lrc");
    }

    /**
     * 清理歌词缓存，和音频用同一套策略：
     *   - 关着缓存开关（默认）：只留正在听的那一首和下一首的歌词，其它全删；
     *   - 开了「听过的歌都留在本机」：都留着，但每首歌只留最新一版。
     * 顺便把换过前缀的老缓存（l1-…l4-）清掉。
     */
    public static void pruneLyricCache(Context c) {
        File dir = lyricCacheDir(c);
        File[] files = dir.listFiles();
        if (files == null) return;

        // 换过前缀的老缓存（l1-…l4-）一次清掉
        String[] oldPrefixes = { "l1-", "l2-", "l3-", "l4-" };
        for (int i = 0; i < files.length; i++) {
            File f = files[i];
            if (f.isDirectory()) continue;
            String name = f.getName();
            for (int k = 0; k < oldPrefixes.length; k++) {
                if (name.startsWith(oldPrefixes[k])) { f.delete(); break; }
            }
        }

        // 该留的文件：正在听的那一首 + 下一首
        Set<String> keep = new HashSet<String>();
        List<String> songHashes = new ArrayList<String>();
        Song current = current();
        if (current != null) {
            keep.add(lyricCacheFile(c, current).getName());
            songHashes.add(hash(current.cloudPath));
        }
        if (index >= 0 && index + 1 < queue.size()) {
            Song next = queue.get(index + 1);
            if (next != null) {
                keep.add(lyricCacheFile(c, next).getName());
                songHashes.add(hash(next.cloudPath));
            }
        }

        boolean cacheEverything = cacheAll();
        files = dir.listFiles();
        if (files == null) return;
        for (int i = 0; i < files.length; i++) {
            File f = files[i];
            if (f.isDirectory()) continue;
            String name = f.getName();
            if (keep.contains(name)) continue;
            if (cacheEverything) {
                // 开了「听过的歌都留在本机」：别的歌都留着，只删同一首歌的旧版本
                for (int k = 0; k < songHashes.size(); k++) {
                    if (name.startsWith(LYRIC_PREFIX + "-" + songHashes.get(k) + "-")) { f.delete(); break; }
                }
                continue;
            }
            f.delete();
        }
    }

    // ---------- 按歌曲记的小数据：时长、歌词偏移 ----------

    public static double durationOf(Context c, Song song) {
        if (song == null) return 0;
        if (durations.containsKey(song.cloudPath)) return durations.get(song.cloudPath).doubleValue();
        double value = Prefs.tableDouble(c, "durations", song.cloudPath, 0);
        if (value > 0) durations.put(song.cloudPath, Double.valueOf(value));
        return value;
    }

    public static void setDuration(Context c, Song song, double seconds) {
        if (seconds <= 0) return;
        song.duration = seconds;
        durations.put(song.cloudPath, Double.valueOf(seconds));
        Prefs.putTableDouble(c, "durations", song.cloudPath, seconds);
    }

    public static double lyricOffset(Context c, Song song) {
        if (song == null) return 0;
        if (lyricOffsets.containsKey(song.cloudPath)) return lyricOffsets.get(song.cloudPath).doubleValue();
        double value = Prefs.tableDouble(c, "lyricOffsets", song.cloudPath, 0);
        if (value != 0) lyricOffsets.put(song.cloudPath, Double.valueOf(value));
        return value;
    }

    public static void setLyricOffset(Context c, Song song, double seconds) {
        if (song == null) return;
        lyricOffsets.put(song.cloudPath, Double.valueOf(seconds));
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
        return sizeOf(cacheDir(c));
    }

    public static int cacheCount(Context c) {
        return countOf(cacheDir(c));
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

    private static long sizeOf(File dir) {
        File[] files = dir == null ? null : dir.listFiles();
        if (files == null) return 0;
        long total = 0;
        for (int i = 0; i < files.length; i++) {
            total += files[i].isDirectory() ? sizeOf(files[i]) : files[i].length();
        }
        return total;
    }

    private static int countOf(File dir) {
        File[] files = dir == null ? null : dir.listFiles();
        if (files == null) return 0;
        int total = 0;
        for (int i = 0; i < files.length; i++) {
            total += files[i].isDirectory() ? countOf(files[i]) : 1;
        }
        return total;
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
