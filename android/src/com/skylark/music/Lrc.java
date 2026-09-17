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
        String titleTag = null;
        String artistTag = null;
        String[] rows = text.replace("\r\n", "\n").replace('\r', '\n').split("\n");
        for (int i = 0; i < rows.length; i++) {
            String row = trimSpace(rows[i]);
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
                } else if (tag.toLowerCase().startsWith("ti:")) {
                    titleTag = tag.substring(3).trim();
                } else if (tag.toLowerCase().startsWith("ar:")) {
                    artistTag = tag.substring(3).trim();
                }
                pos = end + 1;
            }

            String content = trimSpace(row.substring(Math.min(pos, row.length())));
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
        List<Line> merged = mergeTranslations(raw, titleTag, artistTag);
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
     * 判断哪行是译文：优先用 [ti:标题] 的语言（标题一般就是原文语言），
     * 其次用第一行的语言，最后才用出现最多的那种语言。
     * 不能只看谁多：日语歌的中文译文常多出「词：/曲：」这类信息行，可能比原文还多一行。
     */
    private static List<Line> mergeTranslations(List<Line> lines, String titleTag, String artistTag) {
        if (lines.isEmpty()) return lines;

        // 网易云导出的 lrc 第一行常常是「歌名 - 歌手」这种自动生成的行，它不是歌词。
        // 以前它会被当成原文，把真正的第一句歌词当成译文吃掉，整首歌错开一行。
        if (isAutoTitleLine(lines.get(0).text, titleTag, artistTag)) lines.remove(0);
        if (lines.isEmpty()) return lines;

        int zh = 0, ja = 0, latin = 0;
        for (int i = 0; i < lines.size(); i++) {
            String kind = scriptOf(lines.get(i).text);
            if ("ja".equals(kind)) ja++;
            else if ("zh".equals(kind)) zh++;
            else latin++;
        }
        // 第一行往往是「作词 : 某某」这类信息行，判断语言要用第一句真正的歌词
        String firstLyric = null;
        for (int i = 0; i < lines.size(); i++) {
            if (isMetadataLine(lines.get(i).text)) continue;
            firstLyric = lines.get(i).text;
            break;
        }
        if (firstLyric == null) firstLyric = lines.get(0).text;
        String original = pickOriginalLanguage(titleTag, scriptOf(firstLyric), zh, ja, latin, lines.size());

        // 标准排版（原文在上、译文在下、时间戳相同）就按时间戳分组配对，
        // 组内第一行一定是原文；这样「絶対徹夜」这类纯汉字日文原句也不会被当成译文。
        if (prefersGroupLayout(lines, original)) return pairByGroup(lines);

        List<Line> result = new ArrayList<Line>();
        Line pending = null;
        for (int i = 0; i < lines.size(); i++) {
            Line line = lines.get(i);
            // 「词：/曲：/Lyrics by」这类信息行不参与配对，否则会把整首错开一行
            if (isMetadataLine(line.text)) {
                result.add(line);
                continue;
            }
            if (scriptOf(line.text).equals(original)) {
                result.add(line);
                pending = line;
                continue;
            }
            // 依据是位置：译文永远紧跟在它自己的原文之后（同一首歌里译文可能中文、英文混着来）。
            // 时间差只做很宽松的保险：长间奏会让两者相隔十几秒。
            if (pending != null && pending.translation.length() == 0
                    && line.time - pending.time <= 60.0) {
                // 例外：日语原句里「絶対徹夜」这类纯汉字行会被判成中文，它不是上一句的译文，
                // 而是自己的原文（下一行「绝对要熬夜了」才是它的译文）。
                // 分辨方法：下一行也不是原文语言时，这一行更可能是原文。
                if (i + 1 < lines.size()) {
                    Line next = lines.get(i + 1);
                    // 下一行与本行时间戳相同 → 本行多半是旧排版里被标成下一句时间的译文
                    if (!scriptOf(next.text).equals(original) && next.time - line.time <= 60.0
                            && Math.abs(next.time - line.time) > 0.02) {
                        result.add(line);
                        pending = line;
                        continue;
                    }
                }
                pending.translation = line.text;
                pending = null;
                continue;
            }
            // 配不上就别当译文了，自己当原文（日语歌里「絶対徹夜」这类纯汉字行会被判成中文，
            // 但它其实是原文；当成原文后，紧跟的那句中文译文才能配到它）
            result.add(line);
            pending = line;
        }
        return result;
    }

    /** 这首歌是不是「标准排版」：同一时间戳的成对行里，原文语言出现在前的次数不少于在后。 */
    private static boolean prefersGroupLayout(List<Line> lines, String original) {
        int firstWins = 0, secondWins = 0;
        for (int i = 0; i + 1 < lines.size(); i++) {
            if (Math.abs(lines.get(i + 1).time - lines.get(i).time) > 0.02) continue;
            boolean a = scriptOf(lines.get(i).text).equals(original);
            boolean b = scriptOf(lines.get(i + 1).text).equals(original);
            if (a && !b) firstWins++;
            else if (b && !a) secondWins++;
        }
        return firstWins > 0 && firstWins >= secondWins;
    }

    /** 标准排版：同一时间戳的一组行合并成「第一行原文 + 其余作为译文」。 */
    private static List<Line> pairByGroup(List<Line> lines) {
        List<Line> result = new ArrayList<Line>();
        int i = 0;
        while (i < lines.size()) {
            Line line = lines.get(i);
            if (isMetadataLine(line.text)) {
                result.add(line);
                i++;
                continue;
            }
            StringBuilder extra = null;
            int j = i + 1;
            while (j < lines.size() && Math.abs(lines.get(j).time - line.time) <= 0.02
                    && !isMetadataLine(lines.get(j).text)) {
                if (extra == null) extra = new StringBuilder();
                if (extra.length() > 0) extra.append('\n');
                extra.append(lines.get(j).text);
                j++;
            }
            if (extra != null && extra.length() > 0) line.translation = extra.toString();
            result.add(line);
            i = j;
        }
        return result;
    }

    private static final String[] METADATA_PREFIXES = {
        "词:", "曲:", "编曲:", "作词:", "作曲:", "制作:", "制作人:", "混音:", "母带:", "录音:",
        "吉他:", "贝斯:", "鼓:", "键盘:", "和声:", "演唱:", "出品:", "监制:", "op:", "sp:",
        "lyrics by", "composed by", "music by", "written by", "produced by", "arranged by",
        "mixed by", "mastered by", "vocals by", "guitar by", "bass by"
    };

    /** 「词：/曲：/Lyrics by」这类信息行：不参与双语配对，单独成行。 */
    private static boolean isMetadataLine(String text) {
        if (text == null) return false;
        // 冒号前后的空格一起吃掉：「作词 : 某某」也算信息行
        String t = text.trim().replace('：', ':').toLowerCase(java.util.Locale.ROOT);
        t = t.replaceAll("\\s*:\\s*", ":");
        for (int i = 0; i < METADATA_PREFIXES.length; i++) {
            if (t.startsWith(METADATA_PREFIXES[i])) return true;
        }
        return false;
    }

    /** 哪种文字是原文：优先标题语言 → 第一行语言（占比 ≥ 1/4）→ 多数派。 */
    private static String pickOriginalLanguage(String titleTag, String firstKind,
                                               int zh, int ja, int latin, int total) {
        // 有假名的行只可能来自日文原文，中文译文里不会出现假名。这条要放在最前面：
        // 实测《summertime》的 [ti:] 是英文标题、歌词却是日文，而中文译文行数又可能比日文多，
        // 只看标题或只看行数都会判错。
        if (ja >= 1 && ja * 4 >= total) return "ja";
        String titleKind = scriptOf(titleTag);
        if (titleTag != null && titleTag.length() > 0 && countOf(titleKind, zh, ja, latin) > 0) {
            return titleKind;
        }
        if (countOf(firstKind, zh, ja, latin) * 4 >= total) return firstKind;
        if (zh > ja && zh >= latin) return "zh";
        if (ja > zh && ja >= latin) return "ja";
        if (latin > zh && latin > ja) return "latin";
        return firstKind;
    }

    /** 「歌名 - 歌手」这种自动生成的行（网易云导出的 lrc 常见），不算歌词。 */
    private static boolean isAutoTitleLine(String text, String titleTag, String artistTag) {
        if (text == null || titleTag == null || titleTag.length() == 0) return false;
        String t = trimSpace(text);
        String title = trimSpace(titleTag);
        String core = coreTitle(title);
        // 「歌名」和「歌名 (中文译名)」两种写法都试一遍
        for (int attempt = 0; attempt < 2; attempt++) {
            String prefix = attempt == 0 ? title : core;
            if (attempt == 1 && prefix.equals(title)) break;
            if (t.equals(prefix)) return true;
            if (!t.startsWith(prefix)) continue;
            String rest = trimSpace(dropLeadingDashes(t.substring(prefix.length())));
            if (rest.length() == 0) return true;
            if (artistTag == null || artistTag.length() == 0) return true;
            String artist = trimSpace(artistTag);
            String artistCore = coreTitle(artist);
            if (artist.startsWith(rest) || rest.startsWith(artist)
                    || artistCore.startsWith(rest) || rest.startsWith(artistCore)) return true;
        }
        return false;
    }

    /** 去掉「(…)/（中文译名）」这类括注，方便比对「歌名 - 歌手」行。 */
    private static String coreTitle(String text) {
        if (text == null || text.length() == 0) return text;
        String t = trimSpace(text);
        for (int i = 0; i < t.length(); i++) {
            char c = t.charAt(i);
            if (c == '(' || c == '\uFF08') {
                String cut = trimSpace(t.substring(0, i));
                return cut.length() > 0 ? cut : t;
            }
        }
        return t;
    }

    private static String dropLeadingDashes(String text) {
        int i = 0;
        while (i < text.length()) {
            char c = text.charAt(i);
            if (c == '-' || c == '\u2013' || c == '\u2014' || c == '\uFF0D' || Character.isWhitespace(c)) i++;
            else break;
        }
        return text.substring(i);
    }

    /** 会连全角空格（U+3000）一起去掉的 trim：String.trim() 只认 ASCII 空白。 */
    private static String trimSpace(String text) {
        if (text == null) return "";
        int start = 0, end = text.length();
        while (start < end && isSpace(text.charAt(start))) start++;
        while (end > start && isSpace(text.charAt(end - 1))) end--;
        return text.substring(start, end);
    }

    private static boolean isSpace(char c) {
        return Character.isWhitespace(c) || c == '\u3000' || c == '\uFEFF' || c == '\u00A0';
    }

    private static int countOf(String kind, int zh, int ja, int latin) {
        if ("ja".equals(kind)) return ja;
        if ("zh".equals(kind)) return zh;
        return latin;
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
