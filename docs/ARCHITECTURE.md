# Bulesrt Launcher

从零写的 Windows 快速启动器（WPF / .NET 8），深海军蓝 + 白 + 淡蓝配色，图标取自蓝发鲸鱼女仆插画，大圆角卡片 + 系统高清图标网格。

> 工程内部命名空间仍为 `BulesrtLauncher`（历史原因），对外名称、进程名、程序集、快捷方式均已改为 **Bulesrt Launcher / BulesrtLauncher**。

## 运行

```bat
run.cmd
```

或直接运行 `dist\BulesrtLauncher-2.0.0-lite\BulesrtLauncher.exe`（单文件，需要 .NET 8 桌面运行时）。
数据保存在 exe 同级的 `data\data.json`，整个目录可拷走。

## 功能

| 模块 | 说明 |
|---|---|
| 启动 | `Process.Start(UseShellExecute=true)`，与资源管理器双击同一条系统路径；右键可「以管理员身份运行」（runas） |
| 图标 | 解析 `.lnk/.url` 后从**真实目标**取 256px 图标（与 Explorer 同源、无快捷方式小箭头）→ 自动裁掉透明留白 → 写入 `data/icon-cache` 缓存，下次启动秒开；失效快捷方式用彩色字母磁贴（按名称哈希取渐变色） |
| 分类 | 主页 / 全部 / 游戏 / steam / 我的世界 / 工具 / 软件 / 编程 / 未分类，可新建；分类上右键可**重命名 / 删除 / 上移 / 下移**；分类之间**拖动可交换顺序** |
| 拖拽 | **图标拖到左侧分类即可移动过去**（目标分类会高亮）；从资源管理器拖文件到分类 = 直接导入该分类；拖文件到窗口空白处 = 导入当前分类 |
| 搜索 | `Ctrl+K` 或右上角放大镜，按名称/路径模糊匹配，`↑↓` 选择、`Enter` 运行、`Esc` 关闭 |
| 键盘 | 方向键在网格里移动选择（自动按列数换行）、`Home/End` 首尾、`Enter/Space` 启动、`Delete` 删除、`Esc` 取消选择、`Shift+F10` 打开右键菜单、`Ctrl+1..9` 切分类 |
| 鼠标 | 单击选中（蓝色描边），双击启动；内容区右键可新建 / 批量添加 / 刷新图标 / 切换视图 / 设置 |
| 添加 | 单个编辑（名称/路径/参数/工作目录/分类）、批量粘贴路径、拖拽文件进窗口、一键重扫桌面与开始菜单 |
| 去重 | 目标路径已存在时弹出「覆盖 / 跳过 / 取消」；比较时会把 `.lnk` 解析成真实目标、展开环境变量、忽略大小写；同一批里重复的也只保留一条；覆盖会保留原条目位置并刷新图标 |
| 设置 | 开机自启、启动后隐藏、关闭到托盘、深浅主题、默认视图、界面缩放、全局热键录制、**自定义背景**；插件入口与「关于 / 版权」收在面板左下角 |
| 背景 | 选一张本地图片或 **GIF 动图**（复制进 `data/background/`），也能一键用内置插画；`模糊 / 压暗 / 面板透明 / 窗口透明` 四个滑块 + `播放动图` 开关全实时预览；静态图按 2560px 上限解码，动图按帧预算解码 |
| 透明 | 两种粒度：`图标盒子透明` 压卡片 alpha（`ThemeService` 改画笔），`主页背景透明` 把整个背景层淡出、**按像素透出窗口后面真实的桌面和其它窗口**（`AllowsTransparency` + 自绘命中测试）；设置/搜索等弹窗始终不透明 |
| 插件 | `plugins\<name>\plugin.json + dll` 自动加载，每个插件一个 AssemblyLoadContext；提供项目右键动作、插件命令、启动拦截（`e.Cancel`）、增删项目、Toast/日志；设置 → 数据 → 插件管理 查看加载状态 |
| 运行状态 | 每 3 秒扫一次进程表：运行中的项目右上角亮绿点（只按**可见窗口**数判定，浏览器/Electron 的十几个子进程不会被算成"多开"）；右键可「切到运行中的窗口」/「结束运行中的实例」/「再开一个实例（多开）」；设置里可关闭 |
| 全局热键 | 默认 `Ctrl+Alt+Space`，被占用时自动降级到 `Ctrl+Alt+F9` / `Ctrl+Shift+F9` 并在状态栏提示 |
| 托盘 | 关闭时可选「每次询问 / 最小化到托盘 / 直接退出」，勾选「记住我的选择」后不再弹窗；托盘双击唤起、右键退出 |
| 主题 | 深/浅两套 `ResourceDictionary` + 顶部辉光 `GlowBrush`，运行时热切换，配色取自图标插画 |

## 目录

```
BulesrtLauncher/
├─ run.cmd                         启动脚本
├─ BulesrtLauncher.csproj
├─ app.manifest                    PerMonitorV2 DPI + asInvoker
├─ App.xaml                        只负责合并资源字典（颜色 → 令牌 → 控件样式）
├─ App.xaml.cs                     入口、单实例（按安装目录区分）、渲染模式
├─ Themes/
│  └─ Dark.xaml | Light.xaml       颜色令牌（运行时热切换）
├─ Styles/
│  ├─ Tokens.xaml                  字体、转换器、文本样式
│  └─ Controls.xaml                按钮 / 输入框 / 复选框 / Chip / 滚动条 / 侧栏行
├─ Views/
│  ├─ MainWindow.xaml              界面结构（标题栏、侧栏、网格/列表、搜索层、面板）
│  ├─ MainWindow.xaml.cs           核心：字段、构造、首帧重绘、状态栏
│  ├─ MainWindow.Window.cs         窗口外壳：标题栏拖动、托盘、热键、关闭询问
│  ├─ MainWindow.Navigation.cs     侧栏导航、分类计数、右键菜单、分类拖动排序
│  ├─ MainWindow.Items.cs          网格/列表渲染、选择、启动、条目菜单、图标、去重
│  ├─ MainWindow.DragDrop.cs       条目拖到分类、文件拖入
│  ├─ MainWindow.Search.cs         搜索层
│  ├─ MainWindow.Dialogs.cs        弹窗容器、确认框、重复项处理
│  ├─ MainWindow.Settings.cs       设置面板 + 背景图导入 + 关于 / 版权
│  ├─ MainWindow.Uninstall.cs      卸载（uninstall.exe / 内置便携版卸载）
│  ├─ MainWindow.Editor.cs         条目编辑 / 批量添加
│  ├─ MainWindow.View.cs           视图切换、缩放、主题、背景图层（解码 / 模糊 / 压暗缓存）
│  ├─ MainWindow.Shortcuts.cs      键盘：方向键、Enter、Delete、Ctrl+K、Ctrl+1..9
│  ├─ MainWindow.Toasts.cs         右下角提示
│  └─ TextPrompt.cs                「新建分类 / 重命名」输入框
├─ Models/
│  ├─ AppItem.cs                   启动项
│  ├─ Category.cs                  分类
│  ├─ AppSettings.cs               设置
│  ├─ Database.cs                  data.json 根对象
│  ├─ NavEntry.cs                  侧栏行（含计数）
│  └─ ItemDraft.cs                 待添加项（去重流程用）
├─ Services/
│  ├─ Storage/StoreService.cs      JSON 原子写入、分类、计数、去重查询、首次导入
│  ├─ Launching/
│  │  ├─ LaunchService.cs          ShellExecute 语义启动、打开所在位置、文件选择
│  │  ├─ ShellLauncher.cs          ShellExecuteExW P/Invoke（显式 lpDirectory）
│  │  └─ ShortcutResolver.cs       IShellLink 解析 .lnk
│  ├─ Icons/IconService.cs         256px 图标提取、裁边、磁盘缓存
│  ├─ Platform/
│  │  ├─ Importer.cs               桌面 / 开始菜单扫描 + 分类推断
│  │  ├─ HotkeyService.cs          RegisterHotKey + HwndSource 消息钩子
│  │  └─ AutostartService.cs       HKCU Run 自启动
│  └─ Ui/
│     ├─ ThemeService.cs           资源字典切换 + 面板透明（改 alpha）
│     ├─ BackgroundService.cs      背景图导入 / 内嵌插画导出 / 限宽解码 / 清理
│     └─ Converters.cs             绑定转换器（字母磁贴配色等）
├─ Assets/
│  ├─ art.png                      原始插画（图标母图）
│  ├─ app.ico                      多尺寸图标（10 个尺寸，16–256）
│  └─ logo.png                     标题栏 / 安装器用的 256px 头像
├─ tools/make_icon.py              插画 → 图标的生成脚本（去白底 / 圆角深蓝卡 / 多尺寸 ico）
├─ docs/                           截图
└─ dist/                           发布产物
```

> 单文件体量：拆分后 `MainWindow.*.cs` 每个 70–260 行（原先一个文件 1500 行），
> `App.xaml` 从 318 行降到 15 行，样式与颜色令牌分层加载。

## 构建

推荐这种「一个 exe」的精简发布（框架依赖，需要 .NET 8 桌面运行时）：

```bat
dotnet publish -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false ^
  -o dist\BulesrtLauncher-2.0.0-lite
```

产物就一个 `BulesrtLauncher.exe`（约 1.2 MB，其中图标 260 KB），运行后只会在旁边生成 `data\`。

要给没装 .NET 的机器，用自包含版（一个目录约 145 MB）：

```bat
dotnet publish -c Release -r win-x64 --self-contained true -o dist\BulesrtLauncher-2.0.0
```

需要 .NET SDK 8 或更高（本机 SDK 10.0.403 已通过）。

## 默认背景

新装（或第一次运行、从没选过背景）的默认外观就是**蓝色插画背景 + 透明图标盒子**：

- 背景图 `Assets/art.png` 内置在 exe 里，首次运行自动释放到 `data\background\artwork.png`；
- 默认参数：深色主题、模糊 18、压暗 30%、图标盒子 100% 透明、窗口不透桌面；
- 安装包同样内置这张图：装完 `data\background\artwork.png` 就已经在了，`data.json` 里
  `BackgroundImage = "artwork.png"`、`BackdropChosen = true`；
- `BackdropChosen` 是"用户选过背景"的标记——一旦为 true（包括手动点了「恢复内置深蓝背景」），
  程序就不会再用默认图覆盖你的选择。

![](artwork.png)

## 安装包

`BulesrtSetup` 工程出两个离线安装包（同一个 exe，编译期差一个常量 `FRESH_DEFAULT`）：

| 产物 | 行为 |
|---|---|
| `dist\BulesrtLauncher-2.0.0-离线空项目版.exe` | 覆盖安装时**保留**现有 `data\`；只装程序 + 空库 |
| `dist\BulesrtLauncher-2.0.0-全新安装包.exe` | 默认勾选「全新安装」：先把 `data\` 改名成 `data.bak-时间` 备份，再放一份空库 |

```bat
:: 全新安装（清空项目，先备份）
BulesrtLauncher-2.0.0-全新安装包.exe --dir D:\Apps\BulesrtLauncher --auto

:: 保留数据（覆盖升级）
BulesrtLauncher-2.0.0-离线空项目版.exe --dir D:\Apps\BulesrtLauncher --auto
```

命令行还可以加 `--fresh` / `--keep-data` 覆盖界面上的勾选，`--no-shortcuts` / `--no-launch` 同前。

安装器会先做三道检查，**任何一道不过都不碰你的数据**：

1. 目标目录不能是桌面 / 文档 / 下载 / 用户主目录 / 磁盘根 / Program Files——会提示并给出建议路径
   （浏览时选了这些根目录，会自动补成 `<它>\BulesrtLauncher`，也可以点「默认位置」一键还原）；
2. 探针写一个临时文件确认目录可写（被安全软件拦、没权限会当场说明，而不是装到一半失败）；
3. 先把新 exe 写成 `BulesrtLauncher.exe.new` 再原子替换，替换失败就中止——**旧 exe 还在、data 也没动**。

只有这些全过了，才会去「关掉旧实例 → 备份 data → 释放文件」，所以不会再出现"数据被改名了但没装成"这种半成品状态。

### 卸载

装完之后，安装器会做两件事让卸载有地方可去：

1. 把自己复制成 `<安装目录>\uninstall.exe`；
2. 在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\BulesrtLauncher` 写一条卸载记录
   （DisplayName / DisplayVersion / Publisher / DisplayIcon / InstallLocation / UninstallString /
   QuietUninstallString / EstimatedSize，`NoModify` + `NoRepair`），走的是「生成 .reg + `reg import`」，
   所以路径带空格也不会出错。

三种卸载方式：

| 方式 | 行为 |
|---|---|
| **启动器里 设置 → 左下角「卸载…」按钮**（在「取消」旁边） | 二次确认后拉起 `uninstall.exe --uninstall --dir <安装目录>`，然后自动关闭启动器 |
| Windows 设置 → 应用 → Bulesrt Launcher → 卸载 | 跑 `uninstall.exe --uninstall` |
| 双击安装目录里的 `uninstall.exe` | 同一个界面；也可当安装器用 |

安装器页脚还有一条「卸载已安装的版本…」，已经装过时出现，点它直接切到卸载模式。

卸载会停掉正在运行的启动器、删程序文件、删桌面与开始菜单快捷方式、删开机自启、清注册表项。
界面上有一个勾选框：

- **不勾**（默认）：保留 `data\` 和 `plugins\`，卸载后重装能直接接着用；
- **勾上**：项目库、背景图、图标缓存、装过的插件一起删，目录也一起清掉（`uninstall.exe` 自删除）。

命令行：`uninstall.exe --uninstall`（有界面）/ `--uninstall --quiet`（静默，保留数据）。

## 功能图

想自己出一张功能总览图，可以跑 `python tools\make_feature_map.py`（生成 1800×1150 的深色图，
六个分区 + 运行结构），仓库里没有预生成这张图。

## 目录布局（发布后）

```
BulesrtLauncher\
├─ BulesrtLauncher.exe 启动器
├─ data\               数据：data.json / icon-cache / launch.log
└─ 说明.txt
```

数据全部在 `data\`，程序文件只有 exe 本身；整个文件夹可直接拷走。

## 图标与配色

图标不是手绘的，而是从插画 `Assets/art.png` 自动生成，改图后重跑一句即可：

```bat
python tools\make_icon.py
```

脚本做的事：

1. 从四角漫水填充掉白色纸底（只吃连通到边界的近白像素，围裙等内部白色不受影响），得到真正的 alpha；
2. 把角色压进一个圆角深蓝卡片（`#1B2858 → #0A1028` 竖向渐变 + 中心淡蓝辉光 + 1px 内描边），四周留 6% 边距，不切人物；
3. 输出 10 个尺寸（16/20/24/32/40/48/64/96/128/256），≤48 的自动轻微放大主体并锐化，保证小图标也能看出人；
4. 手写 ICO 容器：256 用 PNG 压缩，其余用 32bpp BMP + alpha，GDI/Explorer 都能读；
5. 顺手导出 `Assets/logo.png`（256px），给标题栏和安装器头像用。

主题色同样从插画里取：深海军蓝 `#070C1C / #0D1530 / #121B3A / #192445` 打底，
文字用近白 `#E7ECF9`，强调色取插画蓝提亮后的 `#5A79E8`（`#5A79E8` 在深底上对比度够，
直接用画面的 `#4A64A4` 会发灰）。字母磁贴的调色板也统一成蓝 / 靛 / 淡紫蓝一族，
不再有薄荷绿、紫、青这些和插画打架的颜色。

窗口背景也从纯色换成了「深蓝 + 顶部辉光」：`GlowBrush` 是一支以 `#6C8BF2`（图标里那圈光）
为起点的径向渐变，叠在 `BgBrush` 上，和图标卡片是同一套色，所以没有自定义背景图时也不会显得空。

## 自定义背景与主题透明

设置 → 背景里这些控件，全部实时预览、写进 `data.json` 的 `Settings`：

| 字段 | 作用 |
|---|---|
| `BackgroundImage` | `data\background\` 下的文件名；空 = 用内置深蓝背景；GIF 会自动播放 |
| `BackgroundBlur` | 0–60，`BlurEffect` 半径 |
| `BackgroundDim` | 0–0.85，黑色遮罩不透明度；**0 = 背景图完全不加遮罩**（顶栏和侧栏的渐变也一起归零） |
| `SurfaceOpacity` | 0–1，**图标盒子/卡片**的 alpha 压缩比例（100% = 方块完全消失，只剩图标） |
| `WindowTransparency` | 0–1，**主页背景层**淡出多少，露出 Windows 亚克力（壁纸模糊） |
| `BackdropAnimation` | 播放 GIF 动图；关掉就停在第一帧 |

两种"透明"是不一样的，所以拆成两个滑块：

- **图标盒子透明**：改画笔 alpha（`Panel/Card/Card2/Input/Tile/TileHover` + 字母磁贴的 `TileFade`），图标底下那层方块变成半透/全透，背景图或壁纸直接透上来；设置、搜索等弹窗用单独的 `DialogBrush`，永远不透明。
- **主页背景透明**：把整个背景层（图片/遮罩/辉光）按比例淡出。窗口是 `AllowsTransparency` 的按像素透明窗口，淡出的地方露出的是**窗口后面真实的内容**——桌面、浏览器、任何窗口，不是壁纸模糊。
- 分层窗口对**全透明像素是点击穿透的**，所以 100% 透明时窗口拖不动。解法是在最底层铺一个 `#02000000`（1/255 黑）的矩形：肉眼完全看不出来，但每个像素都有 alpha，窗口永远可点、可拖。
- 分层窗口没有系统边框，所以 `Views/MainWindow.Window.cs` 里自己处理 `WM_NCHITTEST`（6px 缩放边）和 `WM_GETMINMAXINFO`（最大化只占工作区、不盖任务栏）；`Root.Clip` 用圆角矩形裁出 12px 圆角，窗口才不是直角。
- 两者都到 100% 时，页面上只剩图标和文字，其余全部透到桌面。

一键预设（「插画 + 透明盒子」按钮）= 插画壁纸 + 压暗 30% + 盒子 100% 透明 + 深色主题，
也就是文档里这几张截图的样子。

![](artwork.png)

![](artwork.png)

图层顺序（`MainWindow.xaml` 的 `Backdrop`）：

```
Rectangle     BgBrush（内置深蓝底，窗口透明时一起淡出）
 └ Image      UniformToFill + 可选模糊，带 1.08 放大防止边缘透明；GIF 用 DispatcherTimer 逐帧换
 └ BgScrim    纯黑遮罩（压暗滑块）
 └ TopScrim   顶部 110px 渐变，标题栏文字不透底
 └ LeftScrim  左侧 320px 渐变，侧栏文字不透底
 └ GlowBrush  顶部蓝色辉光
 └ 正常内容（卡片、侧栏、标题栏 —— 不受窗口透明影响）
```

几个实现细节：

- 选图后**复制**进 `data\background\`（只留一张），这样整个文件夹拷到别的机器背景还在；「清除」会删掉目录内容并回落到内置背景。
- 解码时先读 `PixelWidth`，超过 2560px 才用 `DecodePixelWidth` 降采样 —— 4K 壁纸拖滑块也不会卡；`BitmapImage` 解码后 `Freeze()`，可以跨线程复用。
- 同一张图按路径缓存，只有换图才重新解码。
- 透明是改**共享画笔的颜色**：`PanelBrush / CardBrush / Card2Brush / TileBrush` 由 `SurfaceState` 单例持有，XAML 里用 `{Binding CardBrush, Source={x:Static ui:SurfaceState.Instance}}` 拿到同一个实例，调透明度只改 `Color`，不重建资源字典。⚠️ 两个坑都实测过：① 画笔放进 `ResourceDictionary` 会被合并时**冻结**，改 `Color` 直接抛 `InvalidOperationException`；② 画笔作为属性值**直接写在 `Style.Setter` / `ControlTemplate` / `DataTemplate` 里**，模板密封时同样会被冻结（于是「图标盒子透明」拖了没反应、盒子卡在某个透明度）。所以这几个画笔一律走 `Binding` 取实例，任何模板里都不放画笔对象本身。字母磁贴的淡化走 `SurfaceState.TileFade` 绑定。滑块 60ms 节流，落笔 650ms 后自动存盘。
- 拖窗口 / 缩放窗口时进「交互模式」：`WM_ENTERSIZEMOVE` / `WM_EXITSIZEMOVE` 里临时关掉背景模糊、把大图插值降到 `LowQuality`、停掉 GIF，松手再恢复。分层窗口（`AllowsTransparency`）每帧都要整窗重绘+合成，带着 Gaussian 模糊拖动就是主要掉帧来源。
- 弹窗（设置 / 搜索 / 确认）打开期间背景动图暂停，避免每 100ms 一次整窗重绘跟拖滑块抢渲染。
- 模糊是 `BlurEffect`，半径大于 0 时给 `Image` 套 `ScaleTransform(1+r/90)`，否则四周会露出透明边。
- 动图用 `GifBitmapDecoder` 一次性解出所有帧（按 160 MB 像素预算截断，最多 160 帧），每帧按 GIF 自己的 delay 用 `DispatcherTimer` 轮播；窗口最小化或隐藏时跳过绘制，不烧 CPU。APNG / WebP 需要额外编解码器，目前按静态图显示。
- 玻璃窗口有两个坑：`CompositionTarget.BackgroundColor` 默认是黑色，必须改成 `Transparent`，否则玻璃后面是一块死黑；`Window.Background` 也要透明（真正的底色由 `Backdrop` 里的 `BgBrush` 矩形负责，所以 `窗口透明 = 0` 时观感和以前完全一致）。

![](settings.png)

## 插件 SDK

插件是"一个 dll + 一个 plugin.json"，放进 `plugins\<文件夹>\` 即可：

```
plugins\hello\
├─ plugin.json
└─ HelloPlugin.dll
```

```json
{ "id": "hello", "name": "Hello Plugin", "version": "1.0.0", "assembly": "HelloPlugin.dll" }
```

```csharp
public sealed class HelloPlugin : IPlugin
{
    public void Initialize(IPluginHost host)
    {
        host.RegisterCommand(new PingCommand());          // Ctrl+K 能搜到，插件命令面板里也能跑
        host.RegisterItemAction(new CopyNameAction());    // 项目右键菜单多一项
        host.RegisterPage(new StatsPage());               // 侧栏多一页，内容自己用 WPF 画
        host.Launching += e => host.Log($"启动 {e.Name}"); // e.Cancel = true 可拦下
    }
    public void Shutdown() { }
}
```

页面扩展点（`IPluginPage`）：

```csharp
public FrameworkElement CreateView(IPluginHost host)
{
    var panel = new StackPanel();
    panel.Children.Add(new TextBlock { Text = $"共 {host.Items.Count} 个项目" });
    return panel;   // 填满内容区，懒加载，只在第一次打开时调用
}
```

- SDK 程序集：`BulesrtLauncher.Sdk`（net8.0-windows，无第三方依赖），打包在 `dist\BulesrtLauncher-SDK-1.1.0.zip`（含 `SDK.md` 和 HelloPlugin 示例源码）；
- 每个插件一个 `AssemblyLoadContext`，插件对 SDK 的引用统一解析到启动器自带的版本；
- 插件抛异常不会带崩启动器：初始化失败在面板里显示原因，事件异常被吞掉；
- 入口：设置 → 数据 → 插件管理… / 插件命令… / 打开插件目录；命令行 `--plugins` 直接弹出面板；
- 插件页面挂在侧栏分类下面（`plugin:<id>`），选中时内容区从网格切成插件自己画的 `FrameworkElement`。

## 常见问题

**启动「米哈游启动器」时弹出 `Failed to initialize registry manager`**

米哈游的 `launcher.exe` 只是壳，真正干活的是版本目录里的 `HYP.exe`（例如 `D:\Games\miHoYo Launcher\1.19.0.382\HYP.exe`）。
`HYP.exe` 会读取**当前工作目录**下的配置，如果工作目录是 `miHoYo Launcher` 根目录（或桌面）就会报这个错。

这里有两个坑，v1.0.3 都修掉了：

1. **项目存的是 `.lnk` 而不是 exe**。桌面快捷方式自带"起始位置"，而 shell 对 `.lnk` 的处理会忽略我们传的工作目录。
   → 现在启动前先用 `IShellLink` 解析出真实目标、参数和起始位置，再启动目标 exe。
2. **.NET `Process.Start(UseShellExecute=true)` 不会把工作目录传给 shell**（实测：同样的参数用 `Process.Start` 启动失败，直接调 `ShellExecuteExW` 就正常）。
   → 现在改为自己 P/Invoke `ShellExecuteExW`，显式传 `lpDirectory`，和资源管理器双击完全同一条路径。

自动推断规则：

| 目标 | 自动使用的工作目录 |
|---|---|
| `...\miHoYo Launcher\launcher.exe` | 最新的含 `HYP.exe` 的版本子目录 |
| `...\miHoYo Launcher\<版本>\HYP.exe` | 该版本目录 |
| 其它程序 | 程序所在目录（若快捷方式带起始位置则优先用它） |

每次启动都会追加一行日志到 `data\launch.log`，方便排查：

```
2026-01-01 00:00:00 raw=...\米哈游启动器.lnk | target=D:\Games\miHoYo Launcher\launcher.exe | cwd=D:\Games\miHoYo Launcher\1.19.0.382
```

## 设计稿对应

- 配色取自 `Assets/art.png` 插画（见上节「图标与配色」）；
- 顶栏、侧栏、图标网格、搜索面板、设置三栏、批量添加的版式见 `docs/main.png`、`docs/settings.png`；
- 窗口使用 `WindowChrome` 自绘（无系统标题栏），保留可拖拽、可缩放、Win11 圆角与深色边框。

![](main.png)

## 版权

Copyright (c) 2026 xiang. All rights reserved. 版权所有 © 2026 xiang

- 代码与文档：MIT（仓库根的 `LICENSE`）；
- 美术素材（`Assets/art.png`、`logo.png`、`app.ico` 及文档截图里的角色形象）：不在 MIT 范围内，
  只允许随本程序原样分发，禁止单独提取再分发或商用（详见仓库根的 `ASSETS-LICENSE.md`）；
- 编译产物的版本信息里带 `Product: Bulesrt Launcher` / `Company: xiang` / `Copyright: © 2026 xiang`。




