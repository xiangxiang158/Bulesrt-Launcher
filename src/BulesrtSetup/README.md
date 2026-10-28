# BulesrtSetup · Bulesrt Launcher 安装器

两种模式，同一个 exe：

- **离线空项目版**（默认）：启动器已经内置在这个 exe 里，双击就能装，不联网；装完是一个**空项目库**（0 个项目，7 个默认分类）。
- **在线版**：传 `--url` 或放一个 `setup.json` 时，改回"下载 zip 再解压安装"的老模式。

产物：`dist\BulesrtLauncher-2.0.0-离线空项目版.exe`（约 2.4 MB，框架依赖，需要 .NET 8 Desktop Runtime）。

## 用法

### 图形界面

双击安装包：

1. **安装位置**：默认 `%LOCALAPPDATA%\Programs\BulesrtLauncher`（可改）；
2. 直接点「安装空项目版」，几秒钟装完，创建桌面 / 开始菜单快捷方式并启动。

离线模式下界面里不会出现"安装包地址"那一栏，只有一块「离线安装包 · 无需联网」的说明。

### 命令行（脚本 / 静默安装）

```bat
:: 离线空项目版，装到默认位置并启动
BulesrtLauncher-2.0.0-离线空项目版.exe --auto

:: 装到指定目录、不建快捷方式、不启动
BulesrtLauncher-2.0.0-离线空项目版.exe --dir D:\Apps\BulesrtLauncher --auto --no-shortcuts --no-launch

:: 在线版（有 url 就自动切换）
BulesrtSetup.exe --url https://example.com/BulesrtLauncher-2.0.0-lite.zip --auto
```

| 参数 | 说明 |
|---|---|
| `--dir` / `-d` | 安装目录，默认 `%LOCALAPPDATA%\Programs\BulesrtLauncher` |
| `--auto` | 启动后自动开始安装（不点按钮） |
| `--no-shortcuts` | 不创建桌面/开始菜单快捷方式 |
| `--no-launch` | 安装完成后不启动程序 |
| `--url` / `-u` | 切到在线模式，填 zip 的 http(s) 链接或本地路径 |

### 固定下载地址（setup.json）

把下面内容存成 `setup.json`，放在安装器同目录，就会自动切到在线模式：

```json
{ "url": "https://example.com/BulesrtLauncher-2.0.0-lite.zip" }
```

## 空项目库是怎么做出来的

安装时如果目标目录里**没有** `data\data.json`，安装器会写一份：

```json
{ "Version": 1, "NextId": 1, "Seeded": true, "Settings": { … }, "Categories": [], "Items": [] }
```

- `Seeded: true` 是关键：启动器原来在"项目为空"时会自动抓 48 个桌面/开始菜单快捷方式，看到这个标记就跳过，库保持空的；
- `Categories: []`：首次启动时由启动器补上 7 个默认分类（游戏 / steam / 我的世界 / 工具 / 软件 / 编程 / 未分类）；
- **已有 `data.json` 时绝不覆盖**，所以升级安装不会动用户的项目。

## 行为说明

- **保留数据**：安装时跳过 `data` 目录，只覆盖程序文件。
- **自动关闭旧实例**：安装前先关闭正在运行的 `BulesrtLauncher.exe`。
- **失败可见**：出错会弹窗，并把堆栈写到同目录 `setup-error.log`。

## 构建

```bat
:: 1) 先发布启动器（单文件 lite）
cd ..\BulesrtLauncher
dotnet publish -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false ^
  -o dist\BulesrtLauncher-2.0.0-lite

:: 2) 把启动器塞进安装器的 payload
copy ..\BulesrtLauncher\dist\BulesrtLauncher-2.0.0-lite\BulesrtLauncher.exe payload\

:: 3) 发布安装器
cd ..\BulesrtSetup
dotnet publish -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false ^
  -o dist\BulesrtSetup-2.0.0
```

`payload\BulesrtLauncher.exe` 和 `payload\payload-readme.txt` 会作为嵌入资源打进安装器
（`LogicalName` 分别是 `payload.exe` / `payload-readme.txt`），所以装完自带 `说明.txt`。

要给没装 .NET 的机器，把 `--self-contained false` 换成 `--self-contained true`。
