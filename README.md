# 云雀 (Skylark)

[![CI](https://github.com/G336ncx-bjx/skylark-music/actions/workflows/ci.yml/badge.svg)](https://github.com/G336ncx-bjx/skylark-music/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/G336ncx-bjx/skylark-music?label=release)](https://github.com/G336ncx-bjx/skylark-music/releases)

一个轻量、顺手的 **云端音乐播放器**，Windows 与 Android 共用同一个云盘曲库：

| 平台 | 程序 | 体积 | 说明 |
| --- | --- | --- | --- |
| Windows 10 / 11 | `Skylark.exe` | 约 300 KB | 单文件、零依赖，双击桌面快捷方式就能听；带桌面歌词（可锁定为鼠标穿透） |
| Android 5.0 及以上 | `Skylark-android.apk` | 约 150 KB | 直接装 APK，不用应用商店；音乐库 / 队列 / 歌词 / 设置四个页面 |

音乐与歌词放在你自己的**云盘分享文件夹**里（清华云盘 / Seafile 的分享链接，或资料库 API 令牌），
播放时自动下载到本地缓存，下次播放同一首就是本地播放；程序不上传任何数据、不扫描系统里的其它内容。

![音乐库](docs/screenshots/library-dark.png)

## 下载

不想自己编译的话，直接到 [Releases](https://github.com/G336ncx-bjx/skylark-music/releases) 下载：

- `Skylark.exe`：Windows 单文件绿色版，下载后双击即可运行（Windows 10 / 11）。
- `Skylark-android.apk`：Android 安装包，手机上点开安装即可（Android 5.0 及以上）。
- `Skylark-win-x64.zip`：exe + 使用说明的压缩包。

下载后如果想让桌面有个快捷方式，运行 `scripts\install.ps1`（或手动给 exe 发送快捷方式到桌面）即可。

### 第一次运行提示「未知发布者」/「未知来源」怎么办？

这是**正常现象**：`Skylark.exe` 与 `Skylark-android.apk` 都没有购买代码签名证书，
Windows 和 Android 对未签名的程序都会提示一次，不是文件有问题。处理方式：

1. Windows：点提示框里的「**更多信息**」，再点「**仍要运行**」；
2. Android：点「**允许安装未知应用**」→「**仍要安装**」（各家 ROM 的措辞略有差别，意思一样）；
3. 想更放心的话，可以对照 Release 里的 `SHA256SUMS.txt` 校验你下载到的文件（或在 PowerShell 里
   `Get-FileHash .\Skylark.exe -Algorithm SHA256` 比对）；
4. 最稳妥的是自己构建：Windows 端是纯 C#，用系统自带编译器 `build.ps1` 一条命令就能编出同样的 exe；
   Android 端 `android\build.ps1` 也一样，只需要 JDK + Android SDK，**不需要 Gradle**。

签名是固定的（Android 用仓库里的 `android/skylark.jks`），所以后续版本可以直接覆盖安装、不会要求先卸载。

另外，程序只访问你在设置里填写的那个云盘地址，不向任何其它服务器发送数据；
如果你看到杀毒软件提示联网，那是因为它在读取你的云盘（播放/缓存/上传/删除都走这个地址）。

## 功能

- **歌曲列表**：读取云盘分享文件夹，按「歌名 - 歌手.mp3」解析歌名与歌手（支持一位或多位歌手）。
  - **单击歌名 = 播放这一首并加入播放列表**（已在列表里不会重复添加）；
  - 顶部有「上传歌曲 / 随机播放 / 播放全部」，右键还有「下一首播放 / 从列表移除」等操作；
  - 支持搜索，以及按序号 / 歌曲 / 歌手 / 时长排序。
- **上传到云盘**：点「上传歌曲」选择文件，或把歌曲 / 歌词文件、整个文件夹**直接拖进窗口**即可上传到分享目录；上传进度显示在左下角，完成后自动刷新列表。
- **播放控制**：播放 / 暂停、上一首 / 下一首、进度拖动、音量调节、静音。
- **播放模式**：顺序播放、列表循环、单曲循环、随机播放。
- **播放列表（播放队列）**：单击队列里的歌曲即刻播放；每行右侧的 **✕** 把这一首移出队列，右键还能上移 / 下移 / 下一首播放；支持清空、导入导出 M3U。
- **歌词**：
  - 软件内歌词页：逐行高亮、自动滚动、点哪句跳哪句、支持翻译行（同一时间戳的第二行）、歌词偏移微调。
  - **桌面歌词**：独立置顶浮窗，可拖动、可调字号 / 颜色 / 不透明度；**支持锁定（鼠标穿透）**，锁定后完全不影响点击和操作电脑。
- **本地占用**：默认只留**两首**——正在听的那一首和下一首（切歌几乎不用等，切到后面会删掉更早的）；
  设置里一个开关可以改成「听过的歌都缓存到本机」（可离线播放），也可以一键清理。
  两端行为一致：播放器需要先拿到本机文件才能放（云盘返回的 MIME 类型不规范，直接在线播放会一直卡缓冲）。
- **主题**：**跟随系统（默认）** / 深色 / 浅色，系统切换深浅色时自动跟随。
- **其它**：全局多媒体按键、托盘图标、记忆播放进度与窗口位置、单实例运行。

## 快速开始

```powershell
# 1) 构建（用 Windows 自带的 .NET Framework 编译器，不需要安装任何依赖）
powershell -ExecutionPolicy Bypass -File build.ps1

# 2) 构建 + 创建桌面快捷方式 + 启动
powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Start
```

之后点击桌面上的「云雀」即可运行。程序是单文件 `dist\Skylark.exe`（约 200 KB），可以直接复制到任何 Windows 10 / 11 电脑上运行。

### 音乐与歌词的放置（在云盘里）

```
云盘分享文件夹/
├─ 冬眠 - 司南.mp3
├─ 冬眠 - 司南.lrc          ← 歌词与歌曲同名，放在同一个目录
├─ City of Stars - Ryan Gosling、Emma Stone.mp3
└─ City of Stars - Ryan Gosling、Emma Stone.lrc
```

- 歌名与歌手用 ` - `（空格 连字符 空格）分隔，多歌手可用 `、` `,` `&` `/` 分隔。
- 有歌词时会自动关联；歌词支持 UTF-8 / UTF-16 / GBK 编码，支持 `[offset:±毫秒]` 与「同一时间戳的第二行作为翻译」。
- 在「设置 → 云端音乐」里粘贴分享链接（形如 `https://cloud.tsinghua.edu.cn/d/xxxxxxxxxxxx/`），点「刷新列表」即可。

### 两种连接方式

| 方式 | 能做什么 | 怎么拿 |
| --- | --- | --- |
| **分享链接**（默认） | 列目录、播放、上传 | 网页版里对文件夹「分享」生成的链接 |
| **资料库 API 令牌** | 上面全部 + **删除云端文件**、上传覆盖同名文件，且不依赖分享链接 | 网页版打开资料库 → 设置 → API 令牌 → 新建（权限选读写） |

两种都填在「设置 → 云端音乐」里，填了令牌就优先用令牌。**令牌等同密码**，只保存在本机设置里，请不要公开或提交到仓库；
不想用了可以在网页版里删掉这个令牌。

只填其中一个即可：只填令牌（分享链接留空）同样能列目录、播放、上传、删除，界面上也只会显示资料库名称而不会显示令牌明文。

### 支持的格式

| 格式 | 说明 |
| --- | --- |
| mp3 / wav / wma / m4a / aac | 直接用 Windows 自带解码播放（推荐 mp3） |
| flac / ogg / opus / ape / wv | **不再支持**：这些文件不会被扫描进音乐库，扫描时会提示忽略了多少个 |

> 说明：v3.0.0 起移除了无损 FLAC 支持（内置解码器已删除）。曲库统一用 mp3/m4a 这类格式，更轻更快；
> 云端若还有 flac 文件，请在网页版里转成 mp3 后再放回来。

## 桌面歌词

![桌面歌词](docs/screenshots/desktop-lyrics.png)

三种状态，互不干扰：

| 状态 | 外观 | 鼠标 |
| --- | --- | --- |
| **已锁定** | 完全透明，只有带阴影的文字（无底色、无边框） | 整窗鼠标穿透，点击直接落到下面的窗口 |
| **未锁定 · 鼠标不在上面** | 透明 | 不挡操作 |
| **未锁定 · 鼠标移上去** | 出现半透明底与阴影，右上角浮出小工具栏 | 可拖动、可点工具栏 |

![锁定的桌面歌词](docs/screenshots/desktop-lyrics-locked.png)

- 右上角工具栏（鼠标移上去才会出现）：**锁定 / 解锁**、字号减、字号加、回到主界面、关闭。
  锁定时整窗鼠标穿透，其余按钮隐藏，只在**鼠标靠近时**在右上角浮出一个 **「解锁」小按钮**（移开后自动淡出），所以**锁定状态下依然能用鼠标一键解锁**。
- 外语歌：当前句有译文时，只显示这一句的原文 + 译文（同样字号，上下两行）；没有译文时才显示下一句作预览。
- 打开 / 隐藏：主界面顶栏的歌词按钮，快捷键 `D`，或全局快捷键 `Ctrl+Alt+D`。
- 锁定 / 解锁：工具栏上的锁按钮、主界面顶栏的锁形按钮、设置里的开关，或全局快捷键 `Ctrl+Alt+L`。
- 颜色可以直接在桌面歌词上**右键 → 歌词颜色**切换（含白色 / 黑色 / 深灰等 10 个预设与自定义取色器），字号、不透明度、翻译开关在「设置 → 桌面歌词」里调整。
- 锁定后不会出现在 Alt+Tab 里，也不会抢焦点；位置与样式自动记忆。

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

## Android 版

手机上不显示桌面歌词（用不上），其它功能与 Windows 版对齐，并且共用同一个云盘曲库：

| 页面 | 能做什么 |
| --- | --- |
| **音乐库** | 搜索歌名 / 歌手；顶部四个按钮：播放全部、随机播放、排序（歌名 / 歌手 / 时长 / 云盘顺序）、刷新；右下角「上传」可选择多个文件传到云盘。**点歌名 = 立刻播放并加入播放队列**（已在队列里不会重复添加），长按有「下一首播放 / 加到队列末尾 / 从音乐库移除 / 从云盘删除」 |
| **队列** | 队列就是播放顺序，点哪首播哪首；长按可移除、上移、下移、设为下一首；右上角一键清空。**队列与播放位置会保留到下次打开**，不用每次重新点 |
| **歌词** | 逐行高亮 + 自动居中滚动，点任意一行跳到那一句；`−0.5 秒 / +0.5 秒` 微调偏移（每首歌单独记住）；外语歌的译文显示在原文下面 |
| **设置** | 分享链接 / API 令牌（带「粘贴」按钮）、测试连接、缓存开关与占用统计、清除缓存、主题（**默认跟随系统**，也可固定浅色 / 深色）、恢复已隐藏的歌曲、关于 |

- **播放控制**：底部播放条有进度拖动、上一首 / 播放暂停 / 下一首、播放模式（顺序 / 列表循环 / 单曲循环 / 随机）；
  切到别的 App 或锁屏后也能继续播，通知栏、锁屏与耳机按键都能控制。
- **从别的 App 分享上传**：在文件管理器或其它 App 里选「分享」→ 云雀，选中的音频会直接上传到云盘。
- **本地只留两首（默认）**：正在听的那一首 + 预取的下一首，切歌几乎不用等，
  切到后面会自动删掉更早的；设置里一个开关可以改成「听过的歌都缓存到本机」（可离线播放），并随时清理。
- **深色 / 浅色**：默认跟随系统，系统切换深浅色时界面会自动跟着变。

安装与使用：下载 `Skylark-android.apk` → 手机上点开安装（允许「未知来源」）→
首次打开会自动跳到「设置」，填入分享链接或 API 令牌 → 回到「音乐库」即可看到云端全部歌曲，
与 Windows 版填的是同一个地址。

### 签名密钥（覆盖升级的前提）

Android 要求同一个包名的后续版本必须用**同一个密钥**签名才能直接覆盖安装，所以密钥不能丢，也不能进公开仓库：

- 本机：第一次跑 `android\build.ps1` 时如果 `android\skylark.jks` 不存在，会自动生成一个，
  口令写在 `android\keystore.pass`；这两个文件都在 `.gitignore` 里，**请自己备份**。
- CI / Release：从仓库 Secrets 读取 `ANDROID_KEYSTORE_BASE64`（密钥文件的 base64）与
  `ANDROID_KEYSTORE_PASS`（口令），这样发布出去的 APK 和你本机编的签名一致，可以互相覆盖安装。

```powershell
# 把本机密钥与口令写进 GitHub Secrets（只需做一次；密钥不会出现在仓库里）
[Convert]::ToBase64String([IO.File]::ReadAllBytes("android\skylark.jks")) |
    gh secret set ANDROID_KEYSTORE_BASE64 --repo G336ncx-bjx/skylark-music
Get-Content android\keystore.pass |
    gh secret set ANDROID_KEYSTORE_PASS --repo G336ncx-bjx/skylark-music
```

## 界面

| 歌词页 | 播放队列 |
| --- | --- |
| ![歌词页](docs/screenshots/lyrics-dark.png) | ![播放队列](docs/screenshots/queue-dark.png) |

| 长歌词自动滚动 | 设置（双列布局） |
| --- | --- |
| ![长歌词](docs/screenshots/lyrics-scroll.png) | ![设置](docs/screenshots/settings-dark.png) |

| 浅色主题 | 桌面歌词（锁定：无背景、鼠标穿透） |
| --- | --- |
| ![浅色主题](docs/screenshots/library-light.png) | ![锁定的桌面歌词](docs/screenshots/desktop-lyrics-locked.png) |

## 项目结构

```
build.ps1                    构建脚本（调用系统自带 csc.exe，把 XAML 作为资源内嵌进 exe）
scripts/make-icon.ps1        生成多尺寸应用图标（纯 System.Drawing 绘制）
scripts/make-android-icon.ps1 生成 Android 图标（圆角方形 + 自适应图标前景）
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
android/build.ps1            Android 构建脚本（aapt2 → javac → d8 → zipalign → apksigner，不用 Gradle）
android/AndroidManifest.xml  Android 清单（权限、前台播放服务、分享上传入口）
android/src/…                Android 端源码（界面 / 前台播放服务 / 云盘 / 歌词 / 设置）
android/res/…                图标、矢量播放按钮、字符串
android/tools/SelfTest.java  纯 Java 逻辑自检（歌词、文件名、时长解析）
android/build.ps1 同目录的 skylark.jks / keystore.pass  本机签名密钥与口令（不入库，见「签名密钥」一节）
```

### 一些实现细节

- **零依赖**：直接使用 Windows 自带的 .NET Framework 4.x 与 WPF，用 `csc.exe` 编译，产物是单个 exe，不需要安装 .NET SDK 或任何第三方库。
- **快速扫描**：时长不依赖解码器，直接解析文件头（MP3 支持 Xing/VBRI 帧数、WAV、M4A/MP4 的 mvhd），并按时长 + 修改时间做缓存。
- **歌词解析**：自动识别 UTF-8 / UTF-16 / GBK，支持一行多时间戳、`offset` 偏移与翻译行。
- **播放进度**：MediaPlayer 的位置更新较粗糙，这里用锚点 + 秒表插值，让进度条与歌词滚动更平滑。
- **配置位置**：`%APPDATA%\Skylark\settings.json`（该目录不可写时自动退回 exe 同级 `data` 目录）。

Android 端的实现路线也是「不用第三方库」：

- **零依赖**：只用 Android 系统自带的 API（`MediaPlayer` + 前台服务 + `MediaSession`），
  APK 里只有一个 `classes.dex`（约 100 KB），没有 Gradle、没有 AndroidX、没有原生 .so；
- **构建**：`aapt2` 编译资源 → `javac` 编译 → `d8` 生成 dex → `zipalign` 对齐 → `apksigner` 签名；
- **歌词 / 时长解析**：与 Windows 版同一套逻辑（LRC 偏移、多时间戳、翻译行；MP3 帧头算时长），
  由 `android/tools/SelfTest.java` 在构建前跑一遍纯 Java 自检；
- **中文路径**：`aapt2` / `zipalign` 是原生程序，读不了中文目录，所以构建脚本在 `%TEMP%` 里编译、
  最后把 APK 复制回 `dist`。

### 开发用命令

```powershell
# 无界面自检：歌词解析、文件名解析、时长解析、真实播放、扫描、配置、M3U
dist\Skylark.exe --selftest

# 离屏渲染界面截图，便于检查排版（library / lyrics / queue / settings / desktop）
dist\Skylark.exe --shot out.png lyrics dark

# 真实启动界面 5 秒后自动退出（冒烟测试）
dist\Skylark.exe --smoke

# 验证桌面歌词“锁定”是否真的鼠标穿透（用 WindowFromPoint 做命中测试）
dist\Skylark.exe --lockcheck

# 把本地文件夹里的歌一次性补齐到云端（缺的上传、同名同大小跳过、大小不同覆盖）
dist\Skylark.exe --uploadall <令牌或分享链接> "D:\某个文件夹"

# 整库校验：逐首拉文件头解析时长 + 统计歌词覆盖率
dist\Skylark.exe --cloudtest <令牌或分享链接>

# Android：构建 APK（先跑一遍纯 Java 逻辑自检，再用 aapt2/javac/d8 编译打包签名）
powershell -ExecutionPolicy Bypass -File android\build.ps1
```

## 许可证

[MIT](LICENSE)

## 版本与发布约定

- 只有**功能级改动**（用户能感知的新能力或修复）才会打 `v*` 标签并发布 Release；
- 工具类、内部重构、命令行的改动只推送到 `main`（CI 依旧会构建 + 跑自检），版本号保持不变；
- 主版本号只在出现不兼容变更时提升，尽量保持「一个版本对应一次有意义的交付」。
- 需要比 Release 更新的构建时：打开 [Actions](https://github.com/G336ncx-bjx/skylark-music/actions/workflows/ci.yml) 里最新一次成功的 CI，下载 `Skylark-dev` 产物即可（每次推送都会重新构建并跑一遍自检）。
