# Bulesrt Launcher 插件 SDK

给 Bulesrt Launcher 写插件只需要一个 dll + 一个 `plugin.json`，丢进启动器目录下的
`plugins\<你的文件夹>\` 就会在下次启动时加载。

- SDK 程序集：`lib\net8.0\BulesrtLauncher.Sdk.dll`（net8.0，无第三方依赖）
- 示例插件：`samples\HelloPlugin\`
- 目标框架：插件建议用 `net8.0` 或 `net8.0-windows`（要弹 WPF 对话框/用剪贴板就选后者）

## 30 秒上手

```
plugins\
└─ hello\
   ├─ plugin.json
   └─ HelloPlugin.dll
```

`plugin.json`：

```json
{
  "id": "hello",
  "name": "Hello Plugin",
  "version": "1.0.0",
  "author": "你的名字",
  "assembly": "HelloPlugin.dll",
  "type": "HelloPlugin.HelloPlugin"
}
```

`type` 可以省略 —— 省略时 SDK 会自动找第一个实现 `IPlugin` 且有无参构造的公开类。

代码：

```csharp
using Bulesrt.Sdk;

public sealed class HelloPlugin : IPlugin
{
    private IPluginHost? _host;

    public void Initialize(IPluginHost host)
    {
        _host = host;
        host.Log("上线了");
        host.RegisterCommand(new PingCommand());
        host.Launching += e => host.Log($"要启动 {e.Name}");
    }

    public void Shutdown() => _host?.Log("下线了");
}

public sealed class PingCommand : IPluginCommand
{
    public string Title => "打个招呼";
    public string Description => "弹一条提示";
    public void Execute(IPluginHost host) => host.Toast($"现在有 {host.Items.Count} 个项目");
}
```

编译后把 dll 和 `plugin.json` 复制进 `plugins\hello\`，重启启动器即可。

## 能用它做什么

| 能力 | 接口 | 说明 |
|---|---|---|
| 读项目库 | `IPluginHost.Items` | 只读的 `IItemRef`（Id / Name / Target / CategoryId） |
| 加/删项目 | `AddItem` / `RemoveItem` | 加之前会查重，重复目标返回 null |
| 加右键菜单 | `RegisterItemAction` | 每个项目右键菜单多一项，`AppliesTo` 决定对谁显示 |
| 加命令 | `RegisterCommand` | `Ctrl+K` 搜索里能搜到并回车运行，也出现在 设置 → 数据 → 插件命令… |
| 加页面 | `RegisterPage` | 侧栏分类下面多一项，点开就是你自己画的 WPF 页面（懒加载，首次打开才 `CreateView`） |
| 拦截启动 | `IPluginHost.Launching` | 事件里把 `e.Cancel = true` 就不启动这一次 |
| 感知变更 | `IPluginHost.ItemsChanged` | 用户在界面上增删项目后触发 |
| 提示 | `Toast` / `Status` / `Log` | `Log` 写到 `data\plugins.log` |
| 环境 | `Version` / `DataDirectory` / `PluginDirectory` / `Theme` | 版本号、数据目录、本插件目录、当前主题 |

### 页面长什么样

```csharp
public sealed class StatsPage : IPluginPage
{
    public string Id => "hello-stats";      // 全局唯一
    public string Title => "Hello 面板";     // 侧栏文字
    public string Icon => "\uE9D9";          // Segoe Fluent Icons 字形，空则用拼图图标

    public FrameworkElement CreateView(IPluginHost host)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = $"共 {host.Items.Count} 个项目", FontSize = 16 });
        var button = new Button { Content = "打开数据目录" };
        button.Click += (_, _) => Process.Start(host.DataDirectory);
        panel.Children.Add(button);
        return panel;   // 返回的元素会填满内容区
    }
}
```

页面里可以随便用 WPF 控件，`host` 提供的 API 都能用；背景/前景建议用启动器主题里的颜色
（`Theme` 告诉你当前是深色还是浅色），不然切主题时会突兀。

线程：`Initialize` 在 UI 线程上调用；事件回调也在 UI 线程，可以直接改 UI 状态，但别做耗时操作
（要跑长任务自己开线程，再 `Dispatcher` 回来 —— 或者干脆只调 `Toast`）。

## 加载与隔离

- 每个插件一个 `AssemblyLoadContext`，两个插件可以带不同版本的同一个依赖而不打架；
- 插件对 `BulesrtLauncher.Sdk` 的引用**统一解析到启动器自带的版本**，所以不要把自己那份 SDK dll
  放进插件文件夹（放了也会被忽略）；
- 插件抛异常不会带崩启动器：初始化失败会在 插件管理 里显示“失败 + 原因”，
  事件里的异常会被吞掉并继续启动流程；
- .NET 不能真正卸载已经加载的类型，所以「插件管理 → 重新加载」只对**新加的**插件生效；
  删除/替换已有插件需要重启启动器。

## 调试

- `设置 → 数据 → 插件管理…`：看每个插件是否加载成功、失败原因；
- `设置 → 数据 → 打开插件目录`：直达 `plugins\`；
- `data\plugins.log`：`host.Log()` 的内容，以及插件初始化 / 面板打开时的异常堆栈；
- 命令行 `BulesrtLauncher.exe --plugins`：启动后直接弹出插件面板，方便边写边看。

## 已知限制

- 页面只能放在内容区（侧栏一项 + 一块内容），还不能往标题栏/状态栏塞按钮；
- 插件无法读写 `data.json`（只能通过 Host 的 API 增删项目），避免两个进程/插件同时写库；
- 插件命令和项目混在同一个搜索结果里，快捷键也共用（回车运行选中的那条）。
