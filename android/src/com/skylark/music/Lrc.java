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

        Collections.sort(raw, new Comparator<Line>() {
            public int compare(Line a, Line b) {
                return Double.compare(a.time, b.time);
            }
        });

        List<Line> merged = new ArrayList<Line>();
        for (int i = 0; i < raw.size(); i++) {
            Line cur = raw.get(i);
            if (!merged.isEmpty()) {
                Line prev = merged.get(merged.size() - 1);
                if (Math.abs(prev.time - cur.time) < 0.05 && prev.translation.length() == 0) {
                    prev.translation = cur.text;
                    continue;
                }
            }
            merged.add(cur);
        }
        lrc.lines = merged;
        return lrc;
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
