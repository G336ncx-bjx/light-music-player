package com.skylark.music;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.graphics.BitmapFactory;
import android.media.AudioManager;
import android.media.MediaMetadata;
import android.media.MediaPlayer;
import android.media.session.MediaSession;
import android.media.session.PlaybackState;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.PowerManager;

import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Random;
import java.util.Set;

/**
 * 播放服务：MediaPlayer + 通知栏控制 + 锁屏/耳机按键（MediaSession）。
 * 云端歌曲先下载到缓存（可关闭），播放队列与播放模式保存在 Store。
 */
public class PlayerService extends Service {

    public static final String ACTION_TOGGLE = "com.skylark.music.TOGGLE";
    public static final String ACTION_NEXT = "com.skylark.music.NEXT";
    public static final String ACTION_PREV = "com.skylark.music.PREV";
    public static final String ACTION_STOP = "com.skylark.music.STOP";

    /** 界面用它刷新（单 Activity 应用，直接用静态回调最简单）。 */
    public interface Listener {
        void onPlayerChanged();
    }

    public static Listener listener;
    public static PlayerService instance;

    private static final int NOTIFY_ID = 1001;
    private static final String CHANNEL = "playback";

    private MediaPlayer player;
    private MediaSession session;
    private NotificationManager notificationManager;
    private AudioManager audioManager;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Random random = new Random();

    private boolean prepared;
    private boolean playing;
    private boolean buffering;
    private String bufferingText = "";
    private Thread workThread;
    private int playToken;
    private boolean becomingNoisyRegistered;

    private final BroadcastReceiver becomingNoisy = new BroadcastReceiver() {
        @Override
        public void onReceive(Context context, Intent intent) {
            pause();
        }
    };

    private final AudioManager.OnAudioFocusChangeListener focusListener =
            new AudioManager.OnAudioFocusChangeListener() {
                public void onAudioFocusChange(int change) {
                    if (change == AudioManager.AUDIOFOCUS_LOSS) {
                        pause();
                    } else if (change == AudioManager.AUDIOFOCUS_LOSS_TRANSIENT) {
                        pause();
                    } else if (change == AudioManager.AUDIOFOCUS_LOSS_TRANSIENT_CAN_DUCK) {
                        if (player != null) player.setVolume(0.3f, 0.3f);
                    } else if (change == AudioManager.AUDIOFOCUS_GAIN) {
                        if (player != null) player.setVolume(1f, 1f);
                    }
                }
            };

    @Override
    public void onCreate() {
        super.onCreate();
        instance = this;
        Store.load(this);
        pruneCache();
        audioManager = (AudioManager) getSystemService(Context.AUDIO_SERVICE);
        notificationManager = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
        createChannel();

        player = new MediaPlayer();
        player.setAudioAttributes(new android.media.AudioAttributes.Builder()
                .setUsage(android.media.AudioAttributes.USAGE_MEDIA)
                .setContentType(android.media.AudioAttributes.CONTENT_TYPE_MUSIC)
                .build());
        player.setWakeMode(this, PowerManager.PARTIAL_WAKE_LOCK);
        player.setOnPreparedListener(new MediaPlayer.OnPreparedListener() {
            public void onPrepared(MediaPlayer mp) {
                prepared = true;
                startPlaybackFromPrepared();
            }
        });
        player.setOnCompletionListener(new MediaPlayer.OnCompletionListener() {
            public void onCompletion(MediaPlayer mp) {
                playing = false;
                prepared = false;
                next(false);
            }
        });
        player.setOnErrorListener(new MediaPlayer.OnErrorListener() {
            public boolean onError(MediaPlayer mp, int what, int extra) {
                playing = false;
                prepared = false;
                clearBuffering();
                Store.status = "播放失败（" + what + "/" + extra + "）";
                updateNotification();
                notifyChanged();
                return true;
            }
        });

        session = new MediaSession(this, "Skylark");
        session.setCallback(new MediaSession.Callback() {
            @Override
            public void onPlay() {
                play();
            }

            @Override
            public void onPause() {
                pause();
            }

            @Override
            public void onSkipToNext() {
                next(true);
            }

            @Override
            public void onSkipToPrevious() {
                prev();
            }

            @Override
            public void onSeekTo(long pos) {
                seekTo(pos / 1000.0);
            }
        });
        session.setActive(true);
        registerBecomingNoisy();
        updateNotification();
        handler.postDelayed(uiTick, 2000);
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        String action = intent == null ? null : intent.getAction();
        if (ACTION_TOGGLE.equals(action)) {
            toggle();
        } else if (ACTION_NEXT.equals(action)) {
            next(true);
        } else if (ACTION_PREV.equals(action)) {
            prev();
        } else if (ACTION_STOP.equals(action)) {
            pause();
        }
        // 保证是前台服务（通知栏常驻，后台也能继续播放）
        if (!playing && !buffering) {
            if (Build.VERSION.SDK_INT >= 26) startForegroundServiceCompat();
        }
        return START_STICKY;
    }

    private void startForegroundServiceCompat() {
        try {
            startForeground(NOTIFY_ID, buildNotification());
        } catch (Exception e) {
            // 忽略：部分系统在无通知权限时会抛异常
        }
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    @Override
    public void onDestroy() {
        instance = null;
        saveState();
        pruneCache();
        handler.removeCallbacks(uiTick);
        if (session != null) {
            session.setActive(false);
            session.release();
        }
        if (player != null) {
            player.release();
            player = null;
        }
        if (becomingNoisyRegistered) {
            try {
                unregisterReceiver(becomingNoisy);
            } catch (Exception e) {
                // 忽略
            }
        }
        super.onDestroy();
    }

    // ---------- 播放控制 ----------

    public boolean isPlaying() {
        return playing;
    }

    public boolean isBuffering() {
        return buffering;
    }

    public String bufferingText() {
        return bufferingText;
    }

    public double position() {
        if (player == null || !prepared) return 0;
        try {
            return player.getCurrentPosition() / 1000.0;
        } catch (Exception e) {
            return 0;
        }
    }

    public double duration() {
        Song s = Store.current();
        if (s != null && s.duration > 0) return s.duration;
        if (player != null && prepared) {
            try {
                return player.getDuration() / 1000.0;
            } catch (Exception e) {
                return 0;
            }
        }
        return 0;
    }

    public void setEndpoint(String endpoint) {
        Store.endpoint = endpoint;
    }

    public void playQueue(List<Song> songs, int at) {
        Store.queue.clear();
        Store.queue.addAll(songs);
        Store.index = at;
        Store.saveQueue(this);
        playAt(at);
    }

    public void enqueue(Song song, boolean playNext) {
        int existing = indexOf(song.cloudPath);
        if (existing >= 0) {
            Store.status = "播放队列里已经有这首歌了";
            notifyChanged();
            return;
        }
        if (Store.queue.isEmpty()) {
            Store.queue.add(song);
            Store.index = 0;
        } else if (playNext) {
            int at = Math.max(0, Math.min(Store.index + 1, Store.queue.size()));
            Store.queue.add(at, song);
        } else {
            Store.queue.add(song);
        }
        Store.saveQueue(this);
        Store.status = playNext ? "已设为下一首播放" : "已加入播放队列";
        notifyChanged();
    }

    public void removeAt(int position) {
        if (position < 0 || position >= Store.queue.size()) return;
        Store.queue.remove(position);
        if (position < Store.index) Store.index--;
        else if (position == Store.index) {
            if (Store.queue.isEmpty()) {
                Store.index = -1;
                stopPlayback();
            } else {
                if (Store.index >= Store.queue.size()) Store.index = 0;
                playAt(Store.index);
            }
        }
        Store.saveQueue(this);
        notifyChanged();
    }

    public void move(int from, int to) {
        if (from < 0 || from >= Store.queue.size()) return;
        if (to < 0 || to >= Store.queue.size()) return;
        Song s = Store.queue.remove(from);
        Store.queue.add(to, s);
        if (Store.index == from) Store.index = to;
        else if (from < Store.index && to >= Store.index) Store.index--;
        else if (from > Store.index && to <= Store.index) Store.index++;
        Store.saveQueue(this);
        notifyChanged();
    }

    public void clearQueue() {
        Store.queue.clear();
        Store.index = -1;
        stopPlayback();
        Store.saveQueue(this);
        notifyChanged();
    }

    public void playAt(int position) {
        if (position < 0 || position >= Store.queue.size()) return;
        Store.index = position;
        playToken++;
        final Song song = Store.queue.get(position);
        Store.saveQueue(this);
        Prefs.setLastSong(this, song.cloudPath);
        beginPlay(song, 0);
    }

    public void toggle() {
        if (playing) pause();
        else play();
    }

    public void play() {
        Song song = Store.current();
        if (song == null) {
            if (Store.queue.isEmpty()) {
                Store.status = "播放队列是空的";
                notifyChanged();
                return;
            }
            playAt(0);
            return;
        }
        if (player != null && prepared) {
            requestFocus();
            player.start();
            playing = true;
            updateNotification();
            notifyChanged();
        } else {
            playAt(Store.index);
        }
    }

    public void pause() {
        if (player != null && prepared && playing) {
            player.pause();
        }
        playing = false;
        savePosition();
        updateNotification();
        notifyChanged();
    }

    public void seekTo(double seconds) {
        if (player == null || !prepared) return;
        if (seconds < 0) seconds = 0;
        player.seekTo((int) (seconds * 1000));
        savePosition();
        notifyChanged();
    }

    public void next(boolean byUser) {
        if (Store.queue.isEmpty()) return;
        if (Store.mode == 2 && !byUser) {
            playAt(Store.index);
            return;
        }
        int nextIndex;
        if (Store.mode == 3) {
            nextIndex = randomIndex();
        } else {
            nextIndex = Store.index + 1;
            if (nextIndex >= Store.queue.size()) {
                if (Store.mode == 0) {
                    pause();
                    Store.status = "播放列表已结束";
                    return;
                }
                nextIndex = 0;
            }
        }
        playAt(nextIndex);
    }

    public void prev() {
        if (Store.queue.isEmpty()) return;
        if (position() > 4) {
            seekTo(0);
            return;
        }
        int target = Store.index - 1;
        if (target < 0) target = Store.mode == 0 ? 0 : Store.queue.size() - 1;
        playAt(target);
    }

    public void setMode(int m) {
        Store.mode = m;
        Prefs.setMode(this, m);
        Store.status = "播放模式：" + Store.modeName(m);
        notifyChanged();
    }

    private int randomIndex() {
        if (Store.queue.size() <= 1) return 0;
        int next = Store.index;
        for (int i = 0; i < 20 && next == Store.index; i++) next = random.nextInt(Store.queue.size());
        return next;
    }

    private int indexOf(String cloudPath) {
        for (int i = 0; i < Store.queue.size(); i++) {
            if (Store.queue.get(i).cloudPath.equals(cloudPath)) return i;
        }
        return -1;
    }

    // ---------- 播放内核 ----------

    private void beginPlay(final Song song, final double startAt) {
        final String endpoint = Store.endpoint;
        if (endpoint.length() == 0) {
            Store.status = "请先在设置里填云盘链接或令牌";
            notifyChanged();
            return;
        }
        final File target = Store.cacheFile(this, song);
        if (Store.isCached(this, song)) {
            // 已经在本机（多半是上一首时预取好的）→ 直接播，切歌几乎没等待
            clearBuffering();
            openFile(target, startAt);
            prefetchNext();
            return;
        }
        // 没在本机就先下载这一首，同时预取下一首：默认最多占两首的空间，切歌不用等
        downloadThenPlay(song, startAt);
        prefetchNext();
    }

    /** 把这一首下载到本机再播（没开缓存时这份文件会在切歌 / 退出时删掉）。 */
    private void downloadThenPlay(final Song song, final double startAt) {
        final String endpoint = Store.endpoint;
        final int token = playToken;
        final File target = Store.cacheFile(this, song);
        buffering = true;
        bufferingText = "正在缓冲…";
        Store.status = "正在缓冲：" + song.title;
        updateNotification();
        notifyChanged();
        Thread t = new Thread(new Runnable() {
            public void run() {
                try {
                    Cloud.download(endpoint, song.cloudPath, target, new Util.Progress() {
                        public void onProgress(long done, long total) {
                            final int percent = total > 0 ? (int) (done * 100 / total) : 0;
                            handler.post(new Runnable() {
                                public void run() {
                                    if (token != playToken) return;
                                    bufferingText = "正在缓冲… " + percent + "%";
                                    updateNotification();
                                    notifyChanged();
                                }
                            });
                        }
                    });
                    probeDuration(song);
                    handler.post(new Runnable() {
                        public void run() {
                            if (token != playToken) return;
                            clearBuffering();
                            openFile(target, startAt);
                        }
                    });
                } catch (final IOException e) {
                    handler.post(new Runnable() {
                        public void run() {
                            if (token != playToken) return;
                            clearBuffering();
                            Store.status = "下载失败：" + Util.shorten(e.getMessage());
                            updateNotification();
                            notifyChanged();
                        }
                    });
                }
            }
        });
        t.setDaemon(true);
        t.start();
    }

    private void clearBuffering() {
        buffering = false;
        bufferingText = "";
    }

    private void openFile(final File file, final double startAt) {
        try {
            prepared = false;
            player.reset();
            player.setDataSource(file.getAbsolutePath());
            player.prepareAsync();
            pendingSeek = startAt;
            requestFocus();
            updateNotification();
            notifyChanged();
        } catch (Exception e) {
            Store.status = "无法播放：" + Util.shorten(e.getMessage());
            notifyChanged();
        }
    }

    private double pendingSeek = 0;

    private void startPlaybackFromPrepared() {
        // 缓冲状态要在准备好之后立刻清掉，否则界面会一直显示「正在缓冲」
        clearBuffering();
        if (pendingSeek > 0) {
            player.seekTo((int) (pendingSeek * 1000));
            pendingSeek = 0;
        }
        Song s = Store.current();
        if (s != null && s.duration <= 0) {
            try {
                Store.setDuration(this, s, player.getDuration() / 1000.0);
            } catch (Exception e) {
                // 忽略
            }
        }
        player.start();
        playing = true;
        Store.status = "";
        updateNotification();
        notifyChanged();
    }

    private void stopPlayback() {
        playing = false;
        prepared = false;
        try {
            player.stop();
        } catch (Exception e) {
            // 忽略
        }
        updateNotification();
    }

    /** 播放时顺带把下一首下载好。 */
    /** 预取「下一首」：切歌时直接本地播放，几乎没有等待；顺带清掉多余的本地文件。 */
    private void prefetchNext() {
        pruneCache();
        if (!Store.prefetchEnabled()) return;
        if (Store.queue.size() <= 1 || Store.endpoint.length() == 0) return;
        final Song s = nextSong();
        if (s == null || Store.isCached(this, s)) return;
        final String endpoint = Store.endpoint;
        Thread t = new Thread(new Runnable() {
            public void run() {
                File target = Store.cacheFile(PlayerService.this, s);
                try {
                    Cloud.download(endpoint, s.cloudPath, target, null);
                    probeDuration(s);
                    handler.post(new Runnable() {
                        public void run() {
                            pruneCache();
                        }
                    });
                } catch (Exception e) {
                    if (target.exists()) target.delete();
                }
            }
        });
        t.setDaemon(true);
        t.start();
    }

    /** 按当前播放模式算下一首（顺序播放到底就是没有）。 */
    private Song nextSong() {
        if (Store.queue.isEmpty()) return null;
        if (Store.mode == 3) {
            if (Store.queue.size() <= 1) return null;
            return Store.queue.get(randomIndex());
        }
        int at = Store.index + 1;
        if (at >= Store.queue.size()) {
            if (Store.mode == 0) return null;
            at = 0;
        }
        if (at == Store.index) return null;
        return Store.queue.get(at);
    }

    /**
     * 空间策略：没开缓存时，本机不留任何音频文件（歌词和正在下载的 .part 除外）；
     * 打开「把听过的歌都留在本机」后才会保留全部。
     */
    private void pruneCache() {
        if (Store.cacheAll()) return;
        File dir = Store.cacheDir(this);
        Set<String> keep = new HashSet<String>();
        if (Store.keepWindow()) {
            // 直连播放走不通时按「留两首」处理：正在听的那一首 + 下一首
            Song current = Store.current();
            if (current != null) keep.add(Store.cacheFile(this, current).getName());
            Song next = nextSong();
            if (next != null) keep.add(Store.cacheFile(this, next).getName());
        }
        File[] files = dir.listFiles();
        if (files == null) return;
        for (int i = 0; i < files.length; i++) {
            File f = files[i];
            if (f.isDirectory()) continue;         // lyrics/ 留着
            String name = f.getName();
            if (name.endsWith(".part")) continue;  // 正在下载的临时文件
            if (keep.contains(name)) continue;
            f.delete();
        }
        // 歌词缓存跟着同一套策略走：默认只留正在听的和下一首
        Store.pruneLyricCache(this);
    }

    /** 设置里换本地占用策略后，立刻按新策略清一遍。 */
    public void pruneNow() {
        pruneCache();
        notifyChanged();
    }

    /** 取前 256KB 解析 MP3 时长。 */
    public static void probeDuration(final Song song) {
        if (song.duration > 0) return;
        if (!song.fileName.toLowerCase().endsWith(".mp3")) return;
        try {
            File local = Store.cacheFile(instance, song);
            byte[] head;
            if (local.exists() && local.length() > 0) {
                java.io.FileInputStream in = new java.io.FileInputStream(local);
                head = new byte[(int) Math.min(262144, local.length())];
                int read = 0;
                while (read < head.length) {
                    int n = in.read(head, read, head.length - read);
                    if (n <= 0) break;
                    read += n;
                }
                in.close();
                double d = Util.mp3Duration(head, local.length());
                if (d > 0) {
                    Store.setDuration(instance, song, d);
                    return;
                }
            }
            if (instance == null || Store.endpoint.length() == 0) return;
            int probe = (int) Math.min(262144, song.size > 0 ? song.size : 262144);
            head = Cloud.head(Store.endpoint, song.cloudPath, probe);
            double d = Util.mp3Duration(head, song.size);
            if (d > 0) Store.setDuration(instance, song, d);
        } catch (Exception e) {
            // 解析失败就保持 --:--
        }
    }

    private void requestFocus() {
        if (audioManager == null) return;
        try {
            audioManager.requestAudioFocus(focusListener, AudioManager.STREAM_MUSIC,
                    AudioManager.AUDIOFOCUS_GAIN);
        } catch (Exception e) {
            // 忽略
        }
    }

    private void registerBecomingNoisy() {
        try {
            IntentFilter filter = new IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY);
            if (Build.VERSION.SDK_INT >= 33) {
                registerReceiver(becomingNoisy, filter, Context.RECEIVER_NOT_EXPORTED);
            } else {
                registerReceiver(becomingNoisy, filter);
            }
            becomingNoisyRegistered = true;
        } catch (Exception e) {
            becomingNoisyRegistered = false;
        }
    }

    private void savePosition() {
        Song s = Store.current();
        if (s != null) {
            Prefs.setLastPosition(this, (int) position());
            Prefs.setLastSong(this, s.cloudPath);
        }
    }

    private void saveState() {
        savePosition();
        Store.saveQueue(this);
    }

    private void notifyChanged() {
        if (listener != null) listener.onPlayerChanged();
    }

    // ---------- 通知栏 ----------

    private void createChannel() {
        if (Build.VERSION.SDK_INT < 26) return;
        NotificationChannel channel = new NotificationChannel(CHANNEL, "播放控制",
                NotificationManager.IMPORTANCE_LOW);
        channel.setShowBadge(false);
        notificationManager.createNotificationChannel(channel);
    }

    private PendingIntent serviceIntent(String action) {
        Intent intent = new Intent(this, PlayerService.class);
        intent.setAction(action);
        int flags = PendingIntent.FLAG_UPDATE_CURRENT;
        if (Build.VERSION.SDK_INT >= 23) flags |= PendingIntent.FLAG_IMMUTABLE;
        return PendingIntent.getService(this, action.hashCode(), intent, flags);
    }

    private Notification buildNotification() {
        Notification.Builder b;
        if (Build.VERSION.SDK_INT >= 26) {
            b = new Notification.Builder(this, CHANNEL);
        } else {
            b = new Notification.Builder(this);
        }
        Song song = Store.current();
        String title = song == null ? "云雀" : song.title;
        String text = song == null ? "还没有播放歌曲" : song.artistText();
        // 只有「还在准备、还没出声」时才显示缓冲提示，而且不带百分比：
        // 百分比只在界面里滚动显示，通知栏一旦停在某个数字上就再也不会变。
        if (buffering && !playing) text = "正在缓冲…";

        Intent open = new Intent(this, MainActivity.class);
        open.setFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP);
        int flags = PendingIntent.FLAG_UPDATE_CURRENT;
        if (Build.VERSION.SDK_INT >= 23) flags |= PendingIntent.FLAG_IMMUTABLE;
        PendingIntent content = PendingIntent.getActivity(this, 0, open, flags);

        b.setContentTitle(title)
                .setContentText(text)
                .setSmallIcon(android.R.drawable.ic_media_play)
                .setLargeIcon(BitmapFactory.decodeResource(getResources(), R.mipmap.ic_launcher))
                .setContentIntent(content)
                .setOngoing(playing)
                .setOnlyAlertOnce(true)
                .setShowWhen(false);

        b.addAction(new Notification.Action.Builder(
                android.R.drawable.ic_media_previous, "上一首", serviceIntent(ACTION_PREV)).build());
        b.addAction(new Notification.Action.Builder(
                playing ? android.R.drawable.ic_media_pause : android.R.drawable.ic_media_play,
                playing ? "暂停" : "播放", serviceIntent(ACTION_TOGGLE)).build());
        b.addAction(new Notification.Action.Builder(
                android.R.drawable.ic_media_next, "下一首", serviceIntent(ACTION_NEXT)).build());

        if (Build.VERSION.SDK_INT >= 21) {
            Notification.MediaStyle style = new Notification.MediaStyle();
            style.setShowActionsInCompactView(0, 1, 2);
            if (session != null) style.setMediaSession(session.getSessionToken());
            b.setStyle(style);
        }
        return b.build();
    }

    private void updateNotification() {
        Notification n = buildNotification();
        lastNotifyText = n.extras.getString(Notification.EXTRA_TEXT);
        try {
            startForeground(NOTIFY_ID, n);
        } catch (Exception e) {
            // 无通知权限时忽略
        }
        updateSession();
    }

    /** 上一次发出去的通知文案，用来避免每秒重复刷通知。 */
    private String lastNotifyText = "";

    /**
     * 每 2 秒把「锁屏 / 通知栏」的状态对齐一次：
     * 更新播放进度（锁屏进度条），并且只要文案和上次不同就重发通知——
     * 这样即使某个状态变化漏了刷新，也会在 2 秒内自愈，不会卡在「正在缓冲 90%」。
     */
    private final Runnable uiTick = new Runnable() {
        public void run() {
            updateSession();
            Notification n = buildNotification();
            String text = n.extras.getString(Notification.EXTRA_TEXT);
            if (text != null && !text.equals(lastNotifyText)) updateNotification();
            handler.postDelayed(this, 2000);
        }
    };

    private void updateSession() {
        if (session == null) return;
        Song song = Store.current();
        if (song != null) {
            MediaMetadata.Builder md = new MediaMetadata.Builder();
            md.putString(MediaMetadata.METADATA_KEY_TITLE, song.title);
            md.putString(MediaMetadata.METADATA_KEY_ARTIST, song.artistText());
            double d = duration();
            if (d > 0) md.putLong(MediaMetadata.METADATA_KEY_DURATION, (long) (d * 1000));
            session.setMetadata(md.build());
        }
        PlaybackState.Builder st = new PlaybackState.Builder();
        st.setState(playing ? PlaybackState.STATE_PLAYING : PlaybackState.STATE_PAUSED,
                (long) (position() * 1000), playing ? 1f : 0f);
        st.setActions(PlaybackState.ACTION_PLAY | PlaybackState.ACTION_PAUSE
                | PlaybackState.ACTION_PLAY_PAUSE | PlaybackState.ACTION_SKIP_TO_NEXT
                | PlaybackState.ACTION_SKIP_TO_PREVIOUS | PlaybackState.ACTION_SEEK_TO);
        session.setPlaybackState(st.build());
    }
}
