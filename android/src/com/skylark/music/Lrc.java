package com.skylark.music;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

/** LRC 歌词解析（支持一行多时间戳、offset、同时间戳的第二行作为翻译）。 */
public class Lrc {

    public static class Line {
        public double time;
        public String text;
        public String translation = "";

        public boolean hasTranslation() {
            return translation != null && translation.length() > 0;
        }
    }

    public List<Line> lines = new ArrayList<Line>();
    public boolean synced;
    public String message = "";

    public static Lrc parse(String text) {
        Lrc lrc = new Lrc();
        if (text == null || text.length() == 0) return lrc;

        List<Line> raw = new ArrayList<Line>();
        double offset = 0;
        String[] rows = text.replace("\r\n", "\n").replace('\r', '\n').split("\n");
        for (int i = 0; i < rows.length; i++) {
            String row = rows[i].trim();
            if (row.length() == 0) continue;

            List<Double> times = new ArrayList<Double>();
            int pos = 0;
            while (pos < row.length() && row.charAt(pos) == '[') {
                int end = row.indexOf(']', pos);
                if (end < 0) break;
                String tag = row.substring(pos + 1, end);
                Double t = parseTimeTag(tag);
                if (t != null) {
                    times.add(t);
                    lrc.synced = true;
                } else if (tag.toLowerCase().startsWith("offset:")) {
                    try {
                        offset = Double.parseDouble(tag.substring(7).trim()) / 1000.0;
                    } catch (Exception e) {
                        // 忽略非法 offset
                    }
                }
                pos = end + 1;
            }

            String content = row.substring(Math.min(pos, row.length())).trim();
            if (content.length() == 0) continue;
            if (times.isEmpty()) {
                Line plain = new Line();
                plain.time = -1;
                plain.text = content;
                raw.add(plain);
                continue;
            }
            for (int k = 0; k < times.size(); k++) {
                Line line = new Line();
                line.time = times.get(k) + offset;
                line.text = content;
                raw.add(line);
            }
        }

        if (!lrc.synced) {
            lrc.lines = raw;
            return lrc;
        }

        // 先按文件顺序把译文并到它自己的原文上（细节见 mergeTranslations），再按时间排序
        List<Line> merged = mergeTranslations(raw);
        Collections.sort(merged, new Comparator<Line>() {
            public int compare(Line a, Line b) {
                return Double.compare(a.time, b.time);
            }
        });
        lrc.lines = merged;
        return lrc;
    }

    /**
     * 双语歌词配对。两种常见写法都要照顾：
     *   A) 原文与译文同一时间戳（原文在前、译文在后）；
     *   B) 译文紧跟在原文之后，但时间戳被标成了下一句的时间
     *      （例如 [00:59.99]In my dreams / [01:01.66]我的梦里 / [01:01.66]I feel your light）。
     * 两种写法里「译文都紧跟在它自己的原文之后」，所以按文件顺序配对、沿用原文的时间戳。
     * 判断哪行是译文：整篇里含假名算日文、只有汉字算中文、都不含算拉丁/其它，
     * 出现最多的那种语言当作原文；数量相同时以第一行为原文。
     */
    private static List<Line> mergeTranslations(List<Line> lines) {
        if (lines.isEmpty()) return lines;
        int zh = 0, ja = 0, latin = 0;
        for (int i = 0; i < lines.size(); i++) {
            String kind = scriptOf(lines.get(i).text);
            if ("ja".equals(kind)) ja++;
            else if ("zh".equals(kind)) zh++;
            else latin++;
        }
        String original;
        if (zh > ja && zh >= latin) original = "zh";
        else if (ja > zh && ja >= latin) original = "ja";
        else if (latin > zh && latin > ja) original = "latin";
        else original = scriptOf(lines.get(0).text);

        List<Line> result = new ArrayList<Line>();
        Line pending = null;
        for (int i = 0; i < lines.size(); i++) {
            Line line = lines.get(i);
            if (scriptOf(line.text).equals(original)) {
                result.add(line);
                pending = line;
                continue;
            }
            if (pending != null && pending.translation.length() == 0
                    && line.time - pending.time <= 15.0) {
                pending.translation = line.text;
                pending = null;
                continue;
            }
            result.add(line);
        }
        return result;
    }

    /** 粗略判断一行歌词属于哪种文字：ja 含假名 / zh 只有汉字 / latin 其它。 */
    private static String scriptOf(String text) {
        if (text == null) return "latin";
        boolean hasKana = false, hasIdeograph = false;
        for (int i = 0; i < text.length(); i++) {
            char c = text.charAt(i);
            if ((c >= 0x3040 && c <= 0x30FF) || (c >= 0x31F0 && c <= 0x31FF)) {
                hasKana = true;
                break;
            }
            if (c >= 0x4E00 && c <= 0x9FFF) hasIdeograph = true;
        }
        if (hasKana) return "ja";
        return hasIdeograph ? "zh" : "latin";
    }

    private static Double parseTimeTag(String tag) {
        int colon = tag.indexOf(':');
        if (colon <= 0) return null;
        String mm = tag.substring(0, colon).trim();
        String rest = tag.substring(colon + 1).trim();
        if (mm.length() == 0 || rest.length() == 0) return null;
        try {
            long minutes = Long.parseLong(mm);
            String ss = rest;
            String frac = "";
            int dot = rest.indexOf('.');
            if (dot < 0) dot = rest.indexOf(':');
            if (dot >= 0) {
                ss = rest.substring(0, dot);
                frac = rest.substring(dot + 1);
            }
            double sec = Double.parseDouble(ss);
            double f = 0;
            if (frac.length() > 0 && frac.length() <= 3) {
                f = Double.parseDouble(frac) / Math.pow(10, frac.length());
            }
            return minutes * 60.0 + sec + f;
        } catch (Exception e) {
            return null;
        }
    }

    /** 当前位置对应的歌词行下标，找不到返回 -1。 */
    public int indexAt(double seconds) {
        if (!synced || lines.isEmpty()) return -1;
        int lo = 0, hi = lines.size() - 1, result = -1;
        while (lo <= hi) {
            int mid = (lo + hi) / 2;
            if (lines.get(mid).time <= seconds) {
                result = mid;
                lo = mid + 1;
            } else {
                hi = mid - 1;
            }
        }
        return result;
    }
}
