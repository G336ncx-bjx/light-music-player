package com.skylark.music;

import android.view.View;
import android.view.ViewGroup;
import android.widget.BaseAdapter;
import android.widget.LinearLayout;
import android.widget.TextView;

import java.util.ArrayList;
import java.util.List;
import java.util.Set;

/**
 * 音乐库 / 播放队列共用的列表适配器。
 * 行里显示序号（多选时变成勾）、歌名、歌手、时长，正在播放的那首左侧有竖条高亮。
 */
public class SongAdapter extends BaseAdapter {

    private static class RowHolder {
        View bar;
        TextView index, title, artist, time;
    }

    private final AppShell host;
    private final List<Song> data = new ArrayList<Song>();
    /** 音乐库列表不高亮正在播放的那首（那是播放队列的事）。 */
    private final boolean highlightCurrent;
    /** 多选管理：长按列表进入，之后点一行切换选中。 */
    private boolean selecting;
    private final Set<String> picked = new java.util.HashSet<String>();

    SongAdapter(AppShell host, boolean highlightCurrent) {
        this.host = host;
        this.highlightCurrent = highlightCurrent;
    }

    void setData(List<Song> source) {
        data.clear();
        data.addAll(source);
        notifyDataSetChanged();
    }

    boolean isSelecting() { return selecting; }

    void setSelecting(boolean value) {
        selecting = value;
        picked.clear();
        notifyDataSetChanged();
    }

    int pickedCount() { return picked.size(); }

    void toggle(Song song) {
        if (song == null) return;
        if (!picked.remove(song.cloudPath)) picked.add(song.cloudPath);
        notifyDataSetChanged();
    }

    void selectAll() {
        for (int i = 0; i < data.size(); i++) picked.add(data.get(i).cloudPath);
        notifyDataSetChanged();
    }

    void clearPicked() {
        picked.clear();
        notifyDataSetChanged();
    }

    /** 当前列表是不是全勾上了（「取消全选」用）。 */
    boolean allPicked() {
        if (data.isEmpty()) return false;
        for (int i = 0; i < data.size(); i++) {
            if (!picked.contains(data.get(i).cloudPath)) return false;
        }
        return true;
    }

    List<Song> pickedSongs() {
        List<Song> out = new ArrayList<Song>();
        for (int i = 0; i < data.size(); i++) {
            Song s = data.get(i);
            if (picked.contains(s.cloudPath)) out.add(s);
        }
        return out;
    }

    public int getCount() { return data.size(); }

    public Object getItem(int position) { return data.get(position); }

    public long getItemId(int position) { return position; }

    public View getView(int position, View convertView, ViewGroup parent) {
        LinearLayout view;
        if (convertView instanceof LinearLayout) {
            view = (LinearLayout) convertView;
        } else {
            view = buildRowView();
        }
        RowHolder holder = (RowHolder) view.getTag();
        Song song = data.get(position);
        Song current = Store.current();
        boolean active = highlightCurrent && current != null && current.cloudPath.equals(song.cloudPath);
        boolean pickedRow = selecting && picked.contains(song.cloudPath);
        view.setBackgroundColor(pickedRow ? host.cAlt : host.cSurface);
        holder.index.setText(pickedRow ? "✓" : String.valueOf(position + 1));
        holder.index.setTextColor(pickedRow ? host.cAccent : (active ? host.cAccent : host.cDim));
        holder.title.setText(song.title);
        holder.title.setTextColor(active ? host.cAccent : host.cText);
        holder.bar.setVisibility(active ? View.VISIBLE : View.INVISIBLE);
        double duration = song.duration > 0 ? song.duration : Store.durationOf(host, song);
        holder.artist.setText(song.artistText());
        holder.time.setText(duration > 0 ? Util.formatTime(duration) : "--:--");
        return view;
    }

    /** 一行：左边正在播放的竖条 + 序号（或勾）+ 歌名歌手 + 时长。 */
    private LinearLayout buildRowView() {
        LinearLayout root = host.row();
        root.setBackgroundColor(host.cSurface);
        root.setPadding(host.dp(10), host.dp(10), host.dp(14), host.dp(10));

        View bar = new View(host);
        bar.setBackgroundColor(host.cAccent);
        LinearLayout.LayoutParams barP = new LinearLayout.LayoutParams(host.dp(3),
                ViewGroup.LayoutParams.MATCH_PARENT);
        barP.rightMargin = host.dp(10);
        root.addView(bar, barP);

        TextView index = host.text("1", 12, host.cDim);
        index.setGravity(android.view.Gravity.CENTER);
        LinearLayout.LayoutParams indexP = new LinearLayout.LayoutParams(host.dp(26),
                ViewGroup.LayoutParams.WRAP_CONTENT);
        indexP.rightMargin = host.dp(8);
        root.addView(index, indexP);

        LinearLayout middle = host.column();
        root.addView(middle, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        TextView title = host.text("", 15, host.cText);
        title.setSingleLine(true);
        title.setEllipsize(android.text.TextUtils.TruncateAt.END);
        TextView artist = host.text("", 12, host.cDim);
        artist.setSingleLine(true);
        artist.setEllipsize(android.text.TextUtils.TruncateAt.END);
        middle.addView(title);
        middle.addView(artist);

        TextView time = host.text("", 12, host.cDim);
        time.setGravity(android.view.Gravity.RIGHT);
        LinearLayout.LayoutParams timeP = new LinearLayout.LayoutParams(host.dp(44),
                ViewGroup.LayoutParams.WRAP_CONTENT);
        timeP.leftMargin = host.dp(8);
        root.addView(time, timeP);

        RowHolder holder = new RowHolder();
        holder.bar = bar;
        holder.index = index;
        holder.title = title;
        holder.artist = artist;
        holder.time = time;
        root.setTag(holder);
        return root;
    }
}
