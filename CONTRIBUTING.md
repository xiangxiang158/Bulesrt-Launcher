# 参与贡献

感谢你想改点什么。这个项目是纯本地的 WPF 应用，没有服务端，构建也就是两条 `dotnet publish`。

## 环境

- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download)（不是运行时，是 SDK）
- 可选：Visual Studio 2022（打开 `BulesrtLauncher.sln`）

## 构建

```bat
build.cmd
```

产物在 `dist\`：

```
dist\launcher\BulesrtLauncher.exe    单文件启动器
dist\setup\BulesrtSetup.exe          安装包（保留 data\）
dist\setup-fresh\BulesrtSetup.exe    安装包（全新安装）
```

> 顺序不能反：安装器把启动器 exe 当**嵌入资源**打包，`build.cmd` 会先发布启动器再拷进 `src\BulesrtSetup\payload\`。
> 直接编译安装器工程会因为缺少 payload 报错，这是故意的提示。

## 代码结构

```
src\BulesrtLauncher\Views\MainWindow.*.cs   按功能拆分的窗口逻辑（设置 / 编辑 / 卸载 / 运行状态…）
src\BulesrtLauncher\Services\               图标、启动、存储、插件、平台、UI（主题 / 背景 / 玻璃画笔）
src\BulesrtLauncher\Sdk 所在目录            src\BulesrtLauncher.Sdk 插件接口
```

改 UI 时的两个约定：

- **颜色令牌**在 `Themes\Dark.xaml` / `Themes\Light.xaml`，控件样式在 `Styles\`；
- 「图标盒子透明」用的几个画笔由 `SurfaceState` 持有，XAML 里必须用
  `{Binding CardBrush, Source={x:Static ui:SurfaceState.Instance}}` 这种绑定取实例，
  **不要**把画笔直接写进 `Style.Setter` / `DataTemplate` —— WPF 会在模板密封时冻结 Freezable，滑块就失效了。

## 提交 PR

1. 从 `main` 切分支：`git checkout -b fix/xxx`；
2. 提交信息用 `feat:` / `fix:` / `docs:` / `perf:` / `ci:` 开头；
3. 不要提交 `bin\` `obj\` `dist\` `data\`（`.gitignore` 已挡住，别 `-f` 强加）；
4. 界面改动请附截图（用系统自带程序当示例数据，别用个人项目，避免泄露隐私）。

## 插件

插件开发看 [`src/BulesrtLauncher.Sdk`](src/BulesrtLauncher.Sdk) 和 [`samples/HelloPlugin`](samples/HelloPlugin)，不需要改主程序。

## 许可证

提交的代码默认按 [MIT](LICENSE) 授权。
