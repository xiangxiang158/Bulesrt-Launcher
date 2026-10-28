using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using BulesrtLauncher.Services.Platform;
using BulesrtLauncher.Services.Plugins;

namespace BulesrtLauncher.Views;

public partial class MainWindow
{
    // ============================================================ 卸载

    private const string UninstallRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string UninstallAppKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BulesrtLauncher";

    /// <summary>卸载流程里置位：关闭时不再回写 data\（勾了「一起删数据」时尤其重要）。</summary>
    private bool _uninstalling;

    /// <summary>
    /// 设置面板左下角的「卸载…」按钮（就在「取消」左边）。
    /// 安装包装出来的版本有 uninstall.exe，直接交给它；便携版没有，就走内置卸载。
    /// </summary>
    private void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        var dir = InstallDir();
        var uninstaller = Path.Combine(dir, "uninstall.exe");

        if (File.Exists(uninstaller))
        {
            Confirm("卸载 Bulesrt Launcher",
                "会关闭启动器，并删除程序文件、桌面与开始菜单快捷方式、开机自启。\n\n" +
                "data\\ 里的项目库默认保留；到了卸载界面还可以另外勾选一起删掉。",
                () => RunUninstaller(uninstaller, dir));
            return;
        }

        // 便携版（没有 uninstall.exe）：用内置卸载面板问清楚再动手
        UninstallDirText.Text = dir;
        UninstallRemoveData.IsChecked = false;
        ShowPanel(PanelUninstall, 520);
    }

    private static string InstallDir() =>
        (AppContext.BaseDirectory ?? ".").TrimEnd(Path.DirectorySeparatorChar);

    /// <summary>内置卸载：先清理快捷方式 / 注册表 / 程序文件，再安排脚本删掉自己的 exe。</summary>
    private void UninstallConfirm_Click(object sender, RoutedEventArgs e)
    {
        var dir = InstallDir();
        if (!File.Exists(Path.Combine(dir, "BulesrtLauncher.exe")))
        {
            Toast("没找到 BulesrtLauncher.exe，这个目录可能已经卸载过了", true);
            CloseModal();
            return;
        }

        var removeData = UninstallRemoveData.IsChecked == true;
        var kept = new List<string>();

        // 1) 开机自启、快捷方式、注册表里的卸载条目
        try { AutostartService.Apply(false); } catch { /* best effort */ }
        try { _store.Db.Settings.Autostart = false; _store.Save(); } catch { /* best effort */ }
        DeleteShortcuts(kept);
        DeleteRegistryEntries();

        // 2) 程序文件（exe 自己留给收尾脚本删，正在被占用删不掉）
        foreach (var file in SafeFiles(dir))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("BulesrtLauncher.exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Equals("uninstall.exe", StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(file); } catch { kept.Add(name); }
        }

        // 3) 子目录：data\ / plugins\ 算用户数据，没勾选就留着
        foreach (var sub in SafeDirs(dir))
        {
            var name = Path.GetFileName(sub);
            var isUserData = name.Equals("data", StringComparison.OrdinalIgnoreCase)
                          || name.Equals("plugins", StringComparison.OrdinalIgnoreCase);
            if (isUserData && !removeData) continue;
            try { Directory.Delete(sub, true); } catch { kept.Add(name + "\\"); }
        }

        // 4) 退出后由脚本删 exe（顺带清理目录）
        ScheduleSelfDelete(dir, removeData);

        _uninstalling = true;
        CloseModal();
        Dispatcher.BeginInvoke(new Action(() => ShutdownForUninstall(kept.Count)), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>关掉插件、托盘、热键，然后退出，把文件交给脚本删。</summary>
    private void ShutdownForUninstall(int keptCount)
    {
        try { PluginService.ShutdownAll(); } catch { /* plugins are best effort */ }
        if (keptCount > 0)
        {
            try { Toast($"有 {keptCount} 个文件被占用，退出后会再试一次", true); } catch { /* 已准备退出 */ }
        }
        _forceClose = true;
        try { _tray?.Dispose(); } catch { /* 已准备退出 */ }
        Close();
        Application.Current.Shutdown();
    }

    /// <summary>安装包装出来的版本：拉起目录里的 uninstall.exe，然后自己退出。</summary>
    private void RunUninstaller(string uninstaller, string dir)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uninstaller,
                Arguments = $"--uninstall --dir \"{dir}\"",
                UseShellExecute = true,
                WorkingDirectory = dir,
            });
        }
        catch (Exception ex)
        {
            Toast("卸载程序启动失败：" + ex.Message, true);
            return;
        }

        // 让 uninstall.exe 先跑起来，再退出自己 —— 否则它删不掉正在被占用的文件
        _uninstalling = true;
        Dispatcher.BeginInvoke(new Action(() => ShutdownForUninstall(0)), System.Windows.Threading.DispatcherPriority.Background);
    }

    // ============================================================ cleanup helpers

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try { return Directory.GetFiles(dir); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.GetDirectories(dir); }
        catch { return Array.Empty<string>(); }
    }

    private static void DeleteShortcuts(List<string> kept)
    {
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Bulesrt Launcher.lnk");
        var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Bulesrt Launcher.lnk");
        foreach (var lnk in new[] { desktop, programs })
        {
            try
            {
                if (File.Exists(lnk)) File.Delete(lnk);
            }
            catch
            {
                kept.Add(Path.GetFileName(lnk));
            }
        }
    }

    private static void DeleteRegistryEntries()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallAppKey, false); } catch { /* 没写过就算了 */ }

        // 启动器自己写的是 BulesrtLauncher；老安装包还写过 BulesrtLauncher
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallRunKey, true);
            if (key is null) return;
            key.DeleteValue("RexeLauncher", false);
            key.DeleteValue("BulesrtLauncher", false);
        }
        catch
        {
            // 自启值删不掉只影响下次开机，不影响卸载本身
        }
    }

    /// <summary>
    /// 退出后再删 exe：和一个正在运行的进程抢文件是抢不过的，
    /// 所以留一个 PowerShell 小脚本，等本进程退出后再动手（路径带中文也不会乱码）。
    /// PowerShell 被组策略挡住时退回 cmd。
    /// </summary>
    private static void ScheduleSelfDelete(string dir, bool removeData)
    {
        if (TryScheduleViaPowerShell(dir, removeData)) return;
        FallbackViaCmd(dir, removeData);
    }

    private static bool TryScheduleViaPowerShell(string dir, bool removeData)
    {
        try
        {
            var script = Path.Combine(Path.GetTempPath(), "bulesrt-uninstall-" + Guid.NewGuid().ToString("N") + ".ps1");
            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
            sb.AppendLine("function Remove-Target([string]$target, [bool]$tree) {");
            sb.AppendLine("  for ($i = 0; $i -lt 20; $i++) {");
            sb.AppendLine("    if (-not (Test-Path -LiteralPath $target)) { return }");
            sb.AppendLine("    if ($tree) { Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue }");
            sb.AppendLine("    else { Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue }");
            sb.AppendLine("    if (-not (Test-Path -LiteralPath $target)) { return }");
            sb.AppendLine("    Start-Sleep -Milliseconds 300");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine($"$procId = {Environment.ProcessId}");
            sb.AppendLine("$waited = 0");
            sb.AppendLine("while ((Get-Process -Id $procId -ErrorAction SilentlyContinue) -and $waited -lt 180) { Start-Sleep -Milliseconds 250; $waited++ }");
            sb.AppendLine("Start-Sleep -Milliseconds 400");
            sb.AppendLine($"Remove-Target {PsLiteral(Path.Combine(dir, "uninstall.exe"))} $false");
            sb.AppendLine($"Remove-Target {PsLiteral(Path.Combine(dir, "BulesrtLauncher.exe"))} $false");
            if (removeData)
            {
                sb.AppendLine($"Remove-Target {PsLiteral(Path.Combine(dir, "data"))} $true");
                sb.AppendLine($"Remove-Target {PsLiteral(Path.Combine(dir, "plugins"))} $true");
            }
            // 目录空了才删，留着 data\ 的便携版不会被误删
            sb.AppendLine($"if ((Test-Path -LiteralPath {PsLiteral(dir)}) -and -not (Get-ChildItem -LiteralPath {PsLiteral(dir)} -Force -ErrorAction SilentlyContinue)) {{ Remove-Item -LiteralPath {PsLiteral(dir)} -Force -ErrorAction SilentlyContinue }}");
            sb.AppendLine("Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue");

            File.WriteAllText(script, sb.ToString(), new UTF8Encoding(true));
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>兜底：cmd 里等一下再删（中文路径依赖系统代码页，只在前一条路走不通时才用）。</summary>
    private static void FallbackViaCmd(string dir, bool removeData)
    {
        try
        {
            var exe = Path.Combine(dir, "BulesrtLauncher.exe");
            var tail = removeData
                ? $" & rd /s /q \"{Path.Combine(dir, "data")}\" & rd /s /q \"{Path.Combine(dir, "plugins")}\""
                : "";
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c ping -n 4 127.0.0.1 >nul & del /f /q \"{exe}\" >nul 2>nul{tail} & rd \"{dir}\" >nul 2>nul",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch
        {
            // 两条路都走不通时 exe 会留在原地，手动删一下即可
        }
    }

    /// <summary>PowerShell 单引号字符串：路径里的 ' 要写成 ''。</summary>
    private static string PsLiteral(string value) => "'" + value.Replace("'", "''") + "'";
}
