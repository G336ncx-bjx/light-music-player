package com.skylark.music;

import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Context;
import android.media.MediaScannerConnection;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.provider.MediaStore;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

/**
 * 把下载下来的音频 / 歌词放进系统的「下载」目录，方便拷到别的地方听。
 * Android 10 及以上走 MediaStore（不需要任何存储权限）；
 * 更老的系统直接写公共目录（需要 WRITE_EXTERNAL_STORAGE）。
 */
public class Saver {

    /** 下载目录里的子文件夹，所有云雀下载的文件都放这儿。 */
    public static final String FOLDER = "云雀";

    /** 保存一个文件，返回给人看的位置描述。 */
    public static String save(Context ctx, File source, String name, String mime) throws IOException {
        if (Build.VERSION.SDK_INT >= 29) return saveViaMediaStore(ctx, source, name, mime);
        return saveToPublicDir(ctx, source, name, mime);
    }

    public static String folderHint() {
        return Build.VERSION.SDK_INT >= 29 ? "下载/" + FOLDER + "/" : "内部存储/Download/" + FOLDER + "/";
    }

    private static String saveViaMediaStore(Context ctx, File source, String name, String mime) throws IOException {
        ContentValues values = new ContentValues();
        values.put(MediaStore.MediaColumns.DISPLAY_NAME, name);
        values.put(MediaStore.MediaColumns.MIME_TYPE, mime);
        values.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/" + FOLDER);

        ContentResolver resolver = ctx.getContentResolver();
        Uri uri = resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values);
        if (uri == null) throw new IOException("系统没有给出写入位置");
        InputStream in = null;
        OutputStream out = null;
        try {
            out = resolver.openOutputStream(uri);
            if (out == null) throw new IOException("打不开写入流");
            in = new FileInputStream(source);
            copy(in, out);
        } catch (IOException e) {
            try {
                resolver.delete(uri, null, null);
            } catch (Exception ignored) {
                // 清理失败就算了，下次下载系统会自己加 (1)
            }
            throw e;
        } finally {
            close(in);
            close(out);
        }
        return folderHint() + name;
    }

    private static String saveToPublicDir(Context ctx, File source, String name, String mime) throws IOException {
        File dir = new File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS), FOLDER);
        if (!dir.exists() && !dir.mkdirs()) throw new IOException("建不了下载目录（需要存储权限）");
        File target = new File(dir, name);
        InputStream in = null;
        OutputStream out = null;
        try {
            in = new FileInputStream(source);
            out = new FileOutputStream(target);
            copy(in, out);
        } finally {
            close(in);
            close(out);
        }
        try {
            MediaScannerConnection.scanFile(ctx, new String[] { target.getAbsolutePath() },
                    new String[] { mime }, null);
        } catch (Exception ignored) {
            // 扫描失败不影响文件本身
        }
        return target.getAbsolutePath();
    }

    private static void copy(InputStream in, OutputStream out) throws IOException {
        byte[] buf = new byte[65536];
        int n;
        while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
    }

    private static void close(Object stream) {
        try {
            if (stream instanceof InputStream) ((InputStream) stream).close();
            else if (stream instanceof OutputStream) ((OutputStream) stream).close();
        } catch (Exception ignored) {
            // 关不掉也没关系
        }
    }
}
