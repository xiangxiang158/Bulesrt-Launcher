using System.Diagnostics;
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
        if (raw.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(raw))
        {
            var sc = ShortcutResolver.Resolve(raw);
            if (sc is not null && !string.IsNullOrWhiteSpace(sc.Target) &&
                (File.Exists(sc.Target) || Directory.Exists(sc.Target)))
            {
                target = sc.Target;
                shortcutArgs = sc.Arguments;
                shortcutWorkDir = sc.WorkingDirectory;
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

            Log($"raw={raw} | target={target} | args=[{args}] | cwd=[{workDir}] | admin={asAdmin}");
            var (ok, winError) = ShellLauncher.Execute(
                target,
                args.Length > 0 ? args : null,
                workDir.Length > 0 && Directory.Exists(workDir) ? workDir : null,
                asAdmin ? "runas" : null);

            if (ok) return (true, "");
            if (winError == 1223) return (false, "已取消管理员授权");
            return (false, winError == 0 ? "启动失败" : $"ShellExecuteEx 失败 (WinError {winError})");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
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

    public static void OpenContainingFolder(string target)
    {
        try
        {
            if (File.Exists(target))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
            else if (Directory.Exists(target))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
        }
        catch
        {
            // ignored
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

