package com.skylark.music;

import org.json.JSONException;
import org.json.JSONObject;

/** 一首歌（云盘上的一个音频文件）。 */
public class Song {
    public String cloudPath;   // 云盘路径，以 / 开头，作为唯一标识
    public String fileName;
    public String title;
    public String artist;
    public long size;
    public double duration;    // 秒，0 表示未知
    public String lyricPath;   // 同名 .lrc 的云盘路径（可能为空）
    public String lyricText;   // 歌词文本（取回后缓存）
    public String localFile;   // 已缓存到本地的完整路径（可能为空）

    public String artistText() {
        return artist == null || artist.length() == 0 ? "未知歌手" : artist;
    }

    public boolean hasLyrics() {
        return lyricPath != null && lyricPath.length() > 0;
    }

    public String durationText() {
        if (duration <= 0) return "--:--";
        int total = (int) Math.round(duration);
        return String.format("%d:%02d", total / 60, total % 60);
    }

    public JSONObject toJson() {
        JSONObject o = new JSONObject();
        try {
            o.put("p", cloudPath);
            o.put("n", fileName);
            o.put("t", title);
            o.put("a", artist);
            o.put("s", size);
            o.put("d", duration);
            o.put("l", lyricPath == null ? "" : lyricPath);
        } catch (JSONException e) {
            // 忽略：字段都是简单类型
        }
        return o;
    }

    public static Song fromJson(JSONObject o) {
        Song s = new Song();
        s.cloudPath = o.optString("p");
        s.fileName = o.optString("n");
        s.title = o.optString("t");
        s.artist = o.optString("a");
        s.size = o.optLong("s");
        s.duration = o.optDouble("d", 0);
        s.lyricPath = o.optString("l");
        return s;
    }
}
