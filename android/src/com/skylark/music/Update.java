package com.skylark.music;

import android.app.Activity;
import android.content.ClipData;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;

import java.io.File;
import java.io.IOException;
import java.util.List;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * 应用内更新：从云盘上的**更新专用仓库**里取安装包（和歌曲库是两个仓库）。
 * 安装包按 Skylark-android-&lt;版本&gt;.apk 命名，谁新就用谁。
 * 装完（或者放弃更新）会把下载下来的安装包删掉。
 */
public class Update {

    /**
     * 更新专用仓库（云盘里名字叫 apk 的那个资料库）的 API 令牌。
     * 它只对这个仓库有权限，和歌曲库无关；即使有人拿到它往仓库里塞别的包，
     * Android 也会因为签名不同拒绝安装，装不上来。
     */
    public static final String UPDATE_ENDPOINT = "ee8dab6377ad2ff15f871e45dc4b6500c003b49b";

    /** 云盘上更新包的名字：Skylark-android-3.3.8.apk */
    private static final Pattern NAME =
            Pattern.compile("^Skylark-android-(\\d+(?:\\.\\d+)*)\\.apk$", Pattern.CASE_INSENSITIVE);

    /** 下载下来的安装包放在应用自己的缓存目录里，装完就删。 */
    public static File apkFile(Context c) {
        return new File(Store.cacheDir(c), "update.apk");
    }

    public static class Found {
        public String version;
        public String path;   // 云盘里的路径（可选）
        public String url;    // GitHub 下载地址（可选）
        public long size;
    }

    /** 去更新仓库里看看有没有比当前版本新的安装包。 */
    public static Found check() throws IOException {
        return findNewer(Cloud.listAll(UPDATE_ENDPOINT), MainActivity.VERSION);
    }

    /** 在云盘文件列表里找比当前版本更新的安装包，有多个就取版本最高的那个。 */
    public static Found findNewer(List<Cloud.Entry> entries, String current) {
        Found best = null;
        for (int i = 0; entries != null && i < entries.size(); i++) {
            Cloud.Entry e = entries.get(i);
            if (e == null || e.dir) continue;
            Matcher m = NAME.matcher(e.name);
            if (!m.matches()) continue;
            String v = m.group(1);
            if (compare(v, current) <= 0) continue;
            if (best == null || compare(v, best.version) > 0) {
                best = new Found();
                best.version = v;
                best.path = e.path;
            }
        }
        return best;
    }

    /** 版本号比较：按数字段比大小（1.10 比 1.9 新）。 */
    public static int compare(String a, String b) {
        if (a == null) a = "";
        if (b == null) b = "";
        String[] x = a.split("\\.");
        String[] y = b.split("\\.");
        int n = Math.max(x.length, y.length);
        for (int i = 0; i < n; i++) {
            int xi = i < x.length ? parseInt(x[i]) : 0;
            int yi = i < y.length ? parseInt(y[i]) : 0;
            if (xi != yi) return xi < yi ? -1 : 1;
        }
        return 0;
    }

    private static int parseInt(String s) {
        try {
            return Integer.parseInt(s.trim());
        } catch (Exception e) {
            return 0;
        }
    }

    public static int currentVersionCode(Context c) {
        try {
            return c.getPackageManager().getPackageInfo(c.getPackageName(), 0).versionCode;
        } catch (Exception e) {
            return 0;
        }
    }

    /**
     * 启动时清理：如果手里这份安装包的版本已经不比当前安装的高（说明刚更新完，
     * 或者用户放弃了这次更新），就把它删掉，不留没用的安装包。
     */
    public static void cleanUp(Context c) {
        File f = apkFile(c);
        if (!f.exists()) return;
        try {
            PackageInfo info = c.getPackageManager().getPackageArchiveInfo(f.getAbsolutePath(), 0);
            if (info == null || info.versionCode <= currentVersionCode(c)) f.delete();
        } catch (Exception e) {
            f.delete();
        }
    }

    /** 拉起系统安装界面：APK 用自带的 ContentProvider 交出去（避免 file:// 在 Android 7+ 被拒）。 */
    public static void install(Context c, File apk) {
        Intent intent = new Intent(Intent.ACTION_VIEW);
        Uri uri = Uri.parse("content://" + UpdateProvider.AUTHORITY + "/update.apk");
        intent.setDataAndType(uri, "application/vnd.android.package-archive");
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        // 有些接收方只认 ClipData 里的 URI，一并带上
        intent.setClipData(ClipData.newRawUri("update.apk", uri));
        if (!(c instanceof Activity)) intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        c.startActivity(intent);
    }

    /** Android 8 起要用户允许「安装未知应用」；没有权限时给个提示并引导过去。 */
    public static boolean canInstall(Context c) {
        if (Build.VERSION.SDK_INT >= 26) {
            try {
                return c.getPackageManager().canRequestPackageInstalls();
            } catch (Exception e) {
                return true;
            }
        }
        return true;
    }

    public static void openInstallSettings(Context c) {
        if (Build.VERSION.SDK_INT < 26) return;
        try {
            Intent intent = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                    Uri.parse("package:" + c.getPackageName()));
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            c.startActivity(intent);
        } catch (Exception e) {
            // 忽略：个别 ROM 没有这个页面
        }
    }
}
