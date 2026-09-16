# 轻音乐 (Light Music)

一个轻量、顺手的 Windows 本地音乐播放器：**单个 exe，零依赖、零配置**，双击桌面快捷方式就能听歌。

音乐与歌词都放在本地文件夹里，程序不联网、不上传、不扫描系统里的其它内容。

![音乐库](docs/screenshots/library-dark.png)

## 功能

- **歌曲列表**：自动扫描音乐文件夹，按「歌名 - 歌手.mp3」解析歌名与歌手（支持一位或多位歌手）；可搜索、可按序号 / 歌曲 / 歌手 / 时长排序。
- **播放控制**：播放 / 暂停、上一首 / 下一首、进度拖动、音量调节、静音。
- **播放模式**：顺序播放、列表循环、单曲循环、随机播放。
- **播放列表（播放队列）**：双击音乐库里的歌曲即从当前列表开始播放；右键可「下一首播放 / 添加到队列」；队列里可移除、上移下移、清空、导入导出 M3U。
- **歌词**：
  - 软件内歌词页：逐行高亮、自动滚动、点哪句跳哪句、支持翻译行（同一时间戳的第二行）、歌词偏移微调。
  - **桌面歌词**：独立置顶浮窗，可拖动、可调字号 / 颜色 / 不透明度；**支持锁定（鼠标穿透）**，锁定后完全不影响点击和操作电脑。
- **音乐目录**：默认使用 `music` 文件夹，可随时在设置里更改，也可以直接把文件夹拖进窗口。
- **其它**：深色 / 浅色主题、全局多媒体按键、托盘图标、记忆播放进度与窗口位置、单实例运行。

## 快速开始

```powershell
# 1) 构建（用 Windows 自带的 .NET Framework 编译器，不需要安装任何依赖）
powershell -ExecutionPolicy Bypass -File build.ps1

# 2) 构建 + 创建桌面快捷方式 + 启动
powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Start
```

之后点击桌面上的「轻音乐」即可运行。程序是单文件 `dist\LightMusic.exe`（约 200 KB），可以直接复制到任何 Windows 10 / 11 电脑上运行。

### 音乐与歌词的放置

```
music/
├─ 冬眠 - 司南.mp3
├─ 冬眠 - 司南.lrc          ← 歌词与歌曲同名，放在同一个目录
├─ City of Stars - Ryan Gosling、Emma Stone.mp3
└─ City of Stars - Ryan Gosling、Emma Stone.lrc
```

- 歌名与歌手用 ` - `（空格 连字符 空格）分隔，多歌手可用 `、` `,` `&` `/` 分隔。
- 有歌词时会自动关联；歌词支持 UTF-8 / UTF-16 / GBK 编码，支持 `[offset:±毫秒]` 与「同一时间戳的第二行作为翻译」。
- 首次运行会自动寻找 `music` 文件夹（exe 同级 → 逐级向上 → 系统的「音乐」目录），也可以在设置里手动指定。

## 桌面歌词

![桌面歌词](docs/screenshots/desktop-lyrics.png)

- 打开：主界面顶栏的歌词按钮，或快捷键 `D`。
- 移动：按住歌词拖动。
- **锁定（鼠标穿透）**：右键菜单「锁定」、主界面顶栏的锁形按钮、设置里的开关，或全局快捷键 `Ctrl+Alt+L`。
  锁定后歌词置顶显示但完全穿透鼠标：点击、拖动、框选都会直接落到下面的窗口，不影响你使用电脑；也不会出现在 Alt+Tab 里、不抢焦点。
- 字号、颜色、不透明度、翻译开关都在「设置 → 桌面歌词」里调整，位置与样式会自动记忆。

## 快捷键

| 按键 | 作用 |
| --- | --- |
| `空格` | 播放 / 暂停 |
| `←` `→` | 快退 / 快进 5 秒 |
| `Ctrl + ←` / `Ctrl + →` | 上一首 / 下一首 |
| `↑` `↓` | 音量加减 |
| `F` | 定位到搜索框 |
| `L` / `Q` / `D` | 歌词页 / 播放队列 / 桌面歌词 |
| `M` | 静音 |
| `Ctrl + Alt + L` | 锁定 / 解锁桌面歌词（全局） |
| `Ctrl + Alt + D` | 显示 / 隐藏桌面歌词（全局） |
| 多媒体键 | 播放 / 暂停、上一首、下一首（全局，可在设置里关闭） |

## 界面

| 歌词页 | 播放队列 |
| --- | --- |
| ![歌词页](docs/screenshots/lyrics-dark.png) | ![播放队列](docs/screenshots/queue-dark.png) |

| 设置 | 浅色主题 |
| --- | --- |
| ![设置](docs/screenshots/settings-dark.png) | ![浅色主题](docs/screenshots/library-light.png) |

## 项目结构

```
build.ps1                    构建脚本（调用系统自带 csc.exe，把 XAML 作为资源内嵌进 exe）
scripts/make-icon.ps1        生成多尺寸应用图标（纯 System.Drawing 绘制）
scripts/install.ps1          构建 + 创建桌面 / 开始菜单快捷方式
src/Program.cs               入口：单实例、自检与截图模式
src/MainWindow.cs            主窗口：外壳、播放调度、托盘、全局热键、设置持久化
src/LibraryView.cs           音乐库视图（列表 / 排序 / 右键菜单）
src/QueueView.cs             播放队列视图（增删排序 / M3U 导入导出）
src/LyricsView.cs            软件内歌词页（逐行高亮 + 平滑滚动）
src/SettingsView.cs          设置页
src/DesktopLyricsWindow.cs   桌面歌词浮窗（置顶 / 拖动 / 鼠标穿透锁定）
src/PlayerEngine.cs          基于 WPF MediaPlayer 的播放内核（含平滑进度估算）
src/Services.cs              目录探测、LRC 解析、时长解析、扫描、配置、M3U
src/Models.cs                数据模型与设置项
src/Theme.cs                 配色、矢量图标、控件工厂
src/Resources/theme.xaml     控件样式（按钮 / 列表 / 滚动条 / 滑块 / 菜单…）
src/Resources/templates.xaml 列表行数据模板
```

### 一些实现细节

- **零依赖**：直接使用 Windows 自带的 .NET Framework 4.x 与 WPF，用 `csc.exe` 编译，产物是单个 exe，不需要安装 .NET SDK 或任何第三方库。
- **快速扫描**：时长不依赖解码器，直接解析文件头（MP3 支持 Xing/VBRI 帧数、FLAC 的 STREAMINFO、WAV、M4A/MP4 的 mvhd），并按时长 + 修改时间做缓存。
- **歌词解析**：自动识别 UTF-8 / UTF-16 / GBK，支持一行多时间戳、`offset` 偏移与翻译行。
- **播放进度**：MediaPlayer 的位置更新较粗糙，这里用锚点 + 秒表插值，让进度条与歌词滚动更平滑。
- **配置位置**：`%APPDATA%\LightMusic\settings.json`（该目录不可写时自动退回 exe 同级 `data` 目录）。

### 开发用命令

```powershell
# 无界面自检：歌词解析、文件名解析、时长解析、真实播放、扫描、配置、M3U
dist\LightMusic.exe --selftest

# 离屏渲染界面截图，便于检查排版（library / lyrics / queue / settings / desktop）
dist\LightMusic.exe --shot out.png lyrics dark

# 真实启动界面 5 秒后自动退出（冒烟测试）
dist\LightMusic.exe --smoke

# 验证桌面歌词“锁定”是否真的鼠标穿透（用 WindowFromPoint 做命中测试）
dist\LightMusic.exe --lockcheck
```

## 许可证

[MIT](LICENSE)
