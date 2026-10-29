using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using BulesrtLauncher.Models;

namespace BulesrtLauncher.Services.Launching;

public static class LaunchService
{
    /// <summary>Launch a target exactly like Explorer does (ShellExecute semantics).</summary>
    public static (bool Ok, string Error) Launch(AppItem item, bool asAdmin = false)
    {
        var raw = (item.Target ?? "").Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(raw))
            return (false, "启动目标为空");

        var extraArgs = (item.Arguments ?? "").Trim();
        var itemWorkDir = (item.WorkingDirectory ?? "").Trim().Trim('"');

        // A .lnk carries its own target / arguments / start-in; resolve it so we control the
        // working directory instead of letting the shell use whatever the shortcut stored.
        var target = raw;
        var shortcutArgs = "";
        var shortcutWorkDir = "";
        var shortcutRunAs = false;
        if (raw.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(raw))
        {
            var sc = ShortcutResolver.Resolve(raw);
            if (sc is not null && !string.IsNullOrWhiteSpace(sc.Target) &&
                (File.Exists(sc.Target) || Directory.Exists(sc.Target)))
            {
                target = sc.Target;
                shortcutArgs = sc.Arguments;
                shortcutWorkDir = sc.WorkingDirectory;
                shortcutRunAs = sc.RunAsAdmin;
            }
        }

        var looksLikeUri = target.Contains("://") ||
                           target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
                           target.StartsWith("steam:", StringComparison.OrdinalIgnoreCase);
        if (!looksLikeUri && !File.Exists(target) && !Directory.Exists(target))
            return (false, "文件不存在");

        try
        {
            var args = string.Join(" ", new[] { shortcutArgs, extraArgs }
                .Where(value => !string.IsNullOrWhiteSpace(value))).Trim();

            var autoDir = ResolveWorkingDirectory(target);
            var specialDir = autoDir.Length > 0 &&
                             !string.Equals(autoDir, Path.GetDirectoryName(target), StringComparison.OrdinalIgnoreCase);

            var workDir = "";
            if (IsDirectory(itemWorkDir)) workDir = itemWorkDir;          // user override wins
            else if (specialDir) workDir = autoDir;                        // e.g. miHoYo version folder
            else if (IsDirectory(shortcutWorkDir)) workDir = shortcutWorkDir; // shortcut's own “start in”
            else workDir = autoDir;

            var runElevated = asAdmin || shortcutRunAs;
            Log($"raw={raw} | target={target} | args=[{args}] | cwd=[{workDir}] | admin={runElevated}"
                + (shortcutRunAs && !asAdmin ? " (继承快捷方式的管理员标记)" : ""));
            var (ok, winError) = ShellLauncher.Execute(
                target,
                args.Length > 0 ? args : null,
                workDir.Length > 0 && Directory.Exists(workDir) ? workDir : null,
                runElevated ? "runas" : null);

            if (ok) return (true, "");
            if (winError == 1223) return (false, "已取消管理员授权");
            return (false, winError == 0 ? "启动失败" : $"ShellExecuteEx 失败 (WinError {winError})");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// 兼容模式：把原始路径（尤其是 .lnk）原样交给 Shell，让 Windows 自己解析，
    /// 和鼠标双击完全同一条路径 —— 不再 spawn explorer.exe（那玩意在某些机器上会 0xc0000142）。
    /// </summary>
    public static (bool Ok, string Error) LaunchViaExplorer(AppItem item)
    {
        var raw = (item.Target ?? "").Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(raw)) return (false, "启动目标为空");
        if (!raw.Contains("://") && !File.Exists(raw) && !Directory.Exists(raw))
            return (false, "文件不存在");

        var (ok, winError) = ShellLauncher.Execute(raw, null, null, null);
        Log($"raw={raw} | via shell（兼容模式，原样交给 Shell）");
        if (ok) return (true, "");
        if (winError == 1223) return (false, "已取消管理员授权");
        return (false, winError == 0 ? "启动失败" : $"ShellExecuteEx 失败 (WinError {winError})");
    }

    private static bool IsDirectory(string path)
        => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    private static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "launch.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // logging must never break launching
        }
    }

    /// <summary>
    /// Work out the working directory a launcher expects.
    /// miHoYo Launcher is special: launcher.exe lives in the program folder while HYP.exe sits in a
    /// versioned sub-folder and refuses to start unless that sub-folder is the current directory
    /// ("Failed to initialize registry manager").
    /// </summary>
    public static string ResolveWorkingDirectory(string target, string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var custom = Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"'));
            if (Directory.Exists(custom)) return custom;
        }

        if (string.IsNullOrWhiteSpace(target)) return "";
        if (target.Contains("://")) return "";
        var path = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        if (!File.Exists(path)) return "";

        var dir = Path.GetDirectoryName(path) ?? "";
        var fileName = Path.GetFileName(path);

        if (fileName.Equals("launcher.exe", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(dir).Equals("miHoYo Launcher", StringComparison.OrdinalIgnoreCase))
        {
            var versionDir = NewestMiHoYoVersionDir(dir);
            if (!string.IsNullOrEmpty(versionDir)) return versionDir;
        }

        if (fileName.Equals("HYP.exe", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(dir);
            if (parent is not null &&
                Path.GetFileName(parent).Equals("miHoYo Launcher", StringComparison.OrdinalIgnoreCase))
                return dir;
        }

        return dir;
    }

    private static string? NewestMiHoYoVersionDir(string launcherDir)
    {
        try
        {
            return Directory.GetDirectories(launcherDir)
                .Select(d => new { Dir = d, Hyp = Path.Combine(d, "HYP.exe") })
                .Where(x => File.Exists(x.Hyp))
                .OrderByDescending(x => Version.TryParse(Path.GetFileName(x.Dir), out var v) ? v : new Version(0, 0))
                .ThenByDescending(x => File.GetLastWriteTimeUtc(x.Hyp))
                .Select(x => x.Dir)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[]? apidl, uint dwFlags);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    /// <summary>在资源管理器里定位文件/打开文件夹 —— 直接调 Shell API，不 spawn explorer.exe。</summary>
    public static void OpenContainingFolder(string target)
    {
        IntPtr pidl = IntPtr.Zero;
        try
        {
            if (!File.Exists(target) && !Directory.Exists(target)) return;
            if (SHParseDisplayName(target, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero) return;
            var one = new[] { pidl };
            if (SHOpenFolderAndSelectItems(pidl, 1, one, 0) != 0)
            {
                // 文件夹本身：打开它
                SHOpenFolderAndSelectItems(pidl, 0, null, 1);
            }
        }
        catch
        {
            // ignored
        }
        finally
        {
            if (pidl != IntPtr.Zero) ILFree(pidl);
        }
    }

    public static void OpenFilePicker(Window owner, bool multiple, Action<string[]> onPicked)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择程序或快捷方式",
            Filter = "程序与快捷方式 (*.exe;*.lnk;*.url;*.bat;*.cmd)|*.exe;*.lnk;*.url;*.bat;*.cmd|所有文件 (*.*)|*.*",
            Multiselect = multiple,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(owner) == true)
            onPicked(dialog.FileNames);
    }
}

