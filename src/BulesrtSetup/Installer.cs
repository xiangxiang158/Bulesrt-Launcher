using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace BulesrtSetup;

public sealed record InstallResult(bool Ok, string Message, string? InstalledExe);

public static class Installer
{
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "BulesrtLauncher");

    /// <summary>Reads setup.json sitting next to the installer ({ "url": "..." }).</summary>
    public static string? ReadBundledUrl()
    {
        try
        {
            var file = Path.Combine(AppContext.BaseDirectory, "setup.json");
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            return doc.RootElement.TryGetProperty("url", out var url) ? url.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True when this build carries the launcher payload (the offline installer).</summary>
    public static bool HasPayload() => OpenPayload() is not null;

    private static Stream? OpenPayload()
    {
        try { return typeof(Installer).Assembly.GetManifestResourceStream("payload.exe"); }
        catch { return null; }
    }

    /// <summary>
    /// Offline install: the launcher is embedded in this exe, and a brand-new empty library is
    /// written next to it (Seeded = true, so the app does not auto-import desktop shortcuts).
    /// </summary>
    public static async Task<InstallResult> RunOfflineAsync(string targetDir,
        IProgress<(double Percent, string Status)> progress, CancellationToken cancel,
        bool createShortcuts = true, bool fresh = false)
    {
        if (string.IsNullOrWhiteSpace(targetDir)) return new InstallResult(false, "请选择安装位置", null);
        if (!HasPayload()) return new InstallResult(false, "这个安装程序里没有内置安装包", null);

        var targetProblem = TargetProblem(targetDir);
        if (targetProblem is not null) return new InstallResult(false, targetProblem, null);

        try
        {
            progress.Report((4, "正在检查目标文件夹…"));
            var writable = await Task.Run(() => ProbeWritable(targetDir), cancel);
            if (writable is not null) return new InstallResult(false, writable, null);

            progress.Report((6, "正在关闭已运行的实例…"));
            await Task.Run(StopRunning, cancel);

            progress.Report((20, "正在释放文件…"));
            Directory.CreateDirectory(targetDir);
            var exe = Path.Combine(targetDir, "BulesrtLauncher.exe");

            // Write next to the target first, then swap: if the old exe is still locked the install
            // fails here, before anything touches the user's data folder.
            var staging = exe + ".new";
            await Task.Run(() =>
            {
                using var src = OpenPayload() ?? throw new InvalidOperationException("安装包损坏：找不到内置程序");
                using var dst = File.Create(staging);
                src.CopyTo(dst);
            }, cancel);

            try
            {
                File.Move(staging, exe, true);
            }
            catch (Exception ex)
            {
                try { File.Delete(staging); } catch { }
                return new InstallResult(false,
                    "程序文件写不进去，安装没有开始（你的项目库未被改动）。\n" +
                    "常见原因：启动器还在运行、或被安全软件/权限拦住了。\n" + ex.Message, null);
            }

            string? backup = null;
            if (fresh)
            {
                progress.Report((60, "正在备份旧数据…"));
                backup = await Task.Run(() => BackupData(targetDir), cancel);
            }

            progress.Report((70, "正在初始化空项目库…"));
            EnsureEmptyLibrary(targetDir);
            WriteReadme(targetDir);

            var version = ReadVersion(exe);

            if (createShortcuts)
            {
                progress.Report((90, "正在创建快捷方式…"));
                CreateShortcuts(exe, targetDir);
            }

            progress.Report((95, "正在注册卸载信息…"));
            RegisterUninstall(targetDir, exe, version);

            progress.Report((100, "安装完成"));
            var message = version is null ? "安装完成 · 空项目库" : $"安装完成 · 空项目库（v{version}）";
            if (backup is not null)
                message += $"，旧数据已备份到 {Path.GetFileName(backup)}";
            return new InstallResult(true, message, exe);
        }
        catch (OperationCanceledException)
        {
            return new InstallResult(false, "已取消", null);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, "安装失败：" + ex.Message, null);
        }
    }


    /// <summary>
    /// Desktop / Documents / drive roots are bad targets: Windows and security tools often block
    /// new executables there, and the user ends up with a launcher icon among their files.
    /// Returns a human hint when the folder should be refused.
    /// </summary>
    public static string? TargetProblem(string dir)
    {
        string full;
        try { full = Path.GetFullPath(dir).TrimEnd('\\'); }
        catch { return "安装路径不合法"; }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop).TrimEnd('\\');
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments).TrimEnd('\\');
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
        var downloads = Path.Combine(profile, "Downloads");

        bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        var label = Same(full, desktop) ? "桌面"
            : Same(full, documents) ? "文档"
            : Same(full, downloads) ? "下载"
            : Same(full, profile) ? "用户主目录"
            : null;

        if (label is not null)
            return $"不能直接装到「{label}」根目录：容易和你的文件混在一起，部分系统策略也会拦这里写 exe。" +
                   $"\n请在它下面建一个子文件夹，例如 {Path.Combine(full, "BulesrtLauncher")}。";

        if (full.Length <= 3)
            return "不能装到磁盘根目录，请选一个子文件夹。";

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd('\\');
        var programFiles86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).TrimEnd('\\');
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
        foreach (var guarded in new[] { programFiles, programFiles86, windows })
        {
            if (Same(full, guarded) || full.StartsWith(guarded + "\\", StringComparison.OrdinalIgnoreCase))
                return $"「{guarded}」下需要管理员权限，装进去以后更新也得提权。" +
                       $"\n建议用默认位置，或选 D:\\Apps\\BulesrtLauncher 这类普通目录。";
        }
        return null;
    }

    /// <summary>For a Desktop/Downloads style root, the folder we would rather install into.</summary>
    public static string? SuggestPath(string dir)
    {
        try
        {
            var full = Path.GetFullPath(dir).TrimEnd('\\');
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop).TrimEnd('\\');
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments).TrimEnd('\\');
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
            var downloads = Path.Combine(profile, "Downloads");
            bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            return Same(full, desktop) || Same(full, documents) || Same(full, downloads) || Same(full, profile)
                ? Path.Combine(full, "BulesrtLauncher")
                : null;
        }
        catch { return null; }
    }

    /// <summary>Writes and deletes a probe file so permission problems surface before anything else.</summary>
    private static string? ProbeWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N")[..6]);
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex)
        {
            return "这个文件夹写不进去：" + ex.Message +
                   "\n可能是权限不足，或被安全软件（受控文件夹访问 / 杀软）拦截；换一个普通目录再试。";
        }
    }

    /// <summary>Moves the existing data folder aside so a fresh empty library can be written.</summary>
    private static string? BackupData(string targetDir)
    {
        var dataDir = Path.Combine(targetDir, "data");
        if (!Directory.Exists(dataDir)) return null;

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backup = Path.Combine(targetDir, "data.bak-" + stamp);
        var n = 1;
        while (Directory.Exists(backup)) backup = Path.Combine(targetDir, $"data.bak-{stamp}-{n++}");
        try
        {
            Directory.Move(dataDir, backup);
            return backup;
        }
        catch
        {
            return null;   // locked; the copy step will report it
        }
    }

    /// <summary>Writes an empty data.json — but never over an existing one.</summary>
    /// <summary>Lays the bundled blue artwork into data\background so a fresh install is not a flat navy screen.</summary>
    private static void WriteDefaultBackdrop(string dataDir)
    {
        try
        {
            var dir = Path.Combine(dataDir, "background");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, "artwork.png");
            if (File.Exists(dest) && new FileInfo(dest).Length > 1024) return;

            using var src = typeof(Installer).Assembly.GetManifestResourceStream("artwork.png");
            if (src is null) return;
            using var dst = File.Create(dest);
            src.CopyTo(dst);
        }
        catch
        {
            // the launcher can re-extract it from its own resources on first run
        }
    }

    private static void EnsureEmptyLibrary(string targetDir)
    {
        var dataDir = Path.Combine(targetDir, "data");
        var dbPath = Path.Combine(dataDir, "data.json");
        if (File.Exists(dbPath)) return;

        Directory.CreateDirectory(dataDir);
        WriteDefaultBackdrop(dataDir);

        const string json = """
        {
          "Version": 1,
          "NextId": 1,
          "Seeded": true,
          "Settings": {
            "Theme": "dark",
            "View": "grid",
            "HideAfterLaunch": false,
            "CloseToTray": false,
            "Autostart": false,
            "Hotkey": "Ctrl+Alt+Space",
            "Scale": 100,
            "SoftwareRender": false,
            "CloseAction": "ask",
            "BackgroundImage": "artwork.png",
            "BackdropChosen": true,
            "BackgroundBlur": 18,
            "BackgroundDim": 0.30,
            "SurfaceOpacity": 1.0,
            "WindowTransparency": 0,
            "BackdropAnimation": true,
            "ShowRunning": true
          },
          "Categories": [],
          "Items": []
        }
        """;
        File.WriteAllText(dbPath, json);
    }

    private static void WriteReadme(string targetDir)
    {
        try
        {
            using var src = typeof(Installer).Assembly.GetManifestResourceStream("payload-readme.txt");
            if (src is null) return;
            using var reader = new StreamReader(src);
            File.WriteAllText(Path.Combine(targetDir, "说明.txt"), reader.ReadToEnd());
        }
        catch
        {
            // cosmetic
        }
    }

    public static async Task<InstallResult> RunAsync(string source, string targetDir,
        IProgress<(double Percent, string Status)> progress, CancellationToken cancel,
        bool createShortcuts = true, bool fresh = false)
    {
        source = (source ?? "").Trim().Trim('"');
        if (source.Length == 0) return new InstallResult(false, "请填写安装包地址", null);
        if (string.IsNullOrWhiteSpace(targetDir)) return new InstallResult(false, "请选择安装位置", null);

        var onlineProblem = TargetProblem(targetDir);
        if (onlineProblem is not null) return new InstallResult(false, onlineProblem, null);
        var staging = "rexe-setup-" + Guid.NewGuid().ToString("N")[..8];
        var tempRoot = Path.Combine(Path.GetTempPath(), staging);
        try
        {
            Directory.CreateDirectory(tempRoot);
        }
        catch
        {
            tempRoot = Path.Combine(AppContext.BaseDirectory, staging);
            Directory.CreateDirectory(tempRoot);
        }
        var zipPath = Path.Combine(tempRoot, "package.zip");
        var extractDir = Path.Combine(tempRoot, "package");
        Directory.CreateDirectory(extractDir);

        try
        {
            // 1) fetch
            if (source.Contains("://", StringComparison.Ordinal))
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("BulesrtSetup/1.0");
                progress.Report((0, "正在连接…"));
                using var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancel);
                if (!response.IsSuccessStatusCode)
                    return new InstallResult(false, $"下载失败：HTTP {(int)response.StatusCode}", null);

                var total = response.Content.Headers.ContentLength ?? -1;
                await using var input = await response.Content.ReadAsStreamAsync(cancel);
                await using var output = File.Create(zipPath);

                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await input.ReadAsync(buffer, cancel)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n), cancel);
                    read += n;
                    var percent = total > 0 ? read * 100.0 / total : Math.Min(95, read / 200000.0);
                    progress.Report((Math.Min(percent * 0.6, 60), $"正在下载… {read / 1048576.0:F1} MB" +
                                                            (total > 0 ? $" / {total / 1048576.0:F1} MB" : "")));
                }
            }
            else
            {
                var local = Environment.ExpandEnvironmentVariables(source);
                if (!File.Exists(local)) return new InstallResult(false, $"找不到安装包：{local}", null);
                var total = new FileInfo(local).Length;
                progress.Report((10, "正在读取本地安装包…"));
                await using var input = File.OpenRead(local);
                await using var output = File.Create(zipPath);
                await input.CopyToAsync(output, cancel);
                progress.Report((60, $"已读取 {total / 1048576.0:F1} MB"));
            }

            cancel.ThrowIfCancellationRequested();

            // 2) extract
            progress.Report((70, "正在解压…"));
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractDir, true), cancel);

            var sourceRoot = FindAppRoot(extractDir);
            if (sourceRoot is null)
                return new InstallResult(false, "安装包里没有找到 BulesrtLauncher.exe", null);

            var version = ReadVersion(Path.Combine(sourceRoot, "BulesrtLauncher.exe"));

            // 3) stop running copies
            progress.Report((84, "正在关闭已运行的实例…"));
            StopRunning();

            // 4) optional clean slate
            string? backup = null;
            if (fresh)
            {
                progress.Report((86, "正在备份旧数据…"));
                backup = BackupData(targetDir);
            }

            // 4b) copy (keeps any surviving data folder)
            progress.Report((88, "正在安装文件…"));
            CopyDirectory(sourceRoot, targetDir);

            // 5) shortcuts
            var exe = Path.Combine(targetDir, "BulesrtLauncher.exe");
            if (createShortcuts)
            {
                progress.Report((95, "正在创建快捷方式…"));
                CreateShortcuts(exe, targetDir);
            }

            progress.Report((97, "正在注册卸载信息…"));
            RegisterUninstall(targetDir, exe, version);

            progress.Report((100, "安装完成"));
            var done = version is null ? "安装完成" : $"安装完成（v{version}）";
            if (backup is not null) done += $"，旧数据已备份到 {Path.GetFileName(backup)}";
            return new InstallResult(true, done, exe);
        }
        catch (OperationCanceledException)
        {
            return new InstallResult(false, "已取消", null);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, "安装失败：" + ex.Message, null);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static string? FindAppRoot(string root)
    {
        if (File.Exists(Path.Combine(root, "BulesrtLauncher.exe"))) return root;
        foreach (var dir in Directory.GetDirectories(root))
            if (File.Exists(Path.Combine(dir, "BulesrtLauncher.exe")))
                return dir;
        return null;
    }

    private static string? ReadVersion(string exe)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            return info.FileVersion;
        }
        catch
        {
            return null;
        }
    }

    private static void StopRunning()
    {
        foreach (var process in Process.GetProcessesByName("BulesrtLauncher"))
        {
            try
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(4000)) process.Kill(true);
            }
            catch
            {
                // ignore — the copy step will report a locked file if it matters
            }
        }
        Thread.Sleep(300);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.StartsWith("data" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                relative.Equals("data", StringComparison.OrdinalIgnoreCase))
                continue; // never overwrite user data

            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static void CreateShortcuts(string exe, string workingDir)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var link = Path.Combine(desktop, "Bulesrt Launcher.lnk");
        var startLink = Path.Combine(programs, "Bulesrt Launcher.lnk");

        WriteShortcut(link, exe, workingDir);
        WriteShortcut(startLink, exe, workingDir);
    }

    // ============================================================ 卸载支持

    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BulesrtLauncher";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "BulesrtLauncher";

    /// <summary>放在安装目录里的卸载程序（就是本安装器的副本）。</summary>
    public static string UninstallExe(string targetDir) => Path.Combine(targetDir, "uninstall.exe");

    /// <summary>默认目录里有没有装过（用来在卸载界面预填路径）。</summary>
    public static bool IsInstalled(string dir)
    {
        try { return File.Exists(Path.Combine(dir, "BulesrtLauncher.exe")); }
        catch { return false; }
    }

    /// <summary>已安装版本（从 exe 版本信息读），没有就返回 null。</summary>
    public static string? InstalledVersion(string dir)
    {
        var exe = Path.Combine(dir, "BulesrtLauncher.exe");
        return File.Exists(exe) ? ReadVersion(exe) : null;
    }

    /// <summary>
    /// 安装收尾：把本安装器复制成 uninstall.exe，并写「应用和功能」里的卸载项。
    /// 两条安装路径（离线 / 在线）都走这里。
    /// </summary>
    private static void RegisterUninstall(string targetDir, string exe, string? version)
    {
        try
        {
            // 1) 卸载程序：优先复制当前安装器自己；在线版则生成一个小的「删目录」脚本兜底
            var self = Environment.ProcessPath;
            var uninstall = UninstallExe(targetDir);
            if (!string.IsNullOrEmpty(self) && File.Exists(self) &&
                !string.Equals(Path.GetFullPath(self), Path.GetFullPath(uninstall), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Copy(self, uninstall, true); } catch { /* 被占用就算了 */ }
            }

            // 2) 注册表卸载项（当前用户，不需要管理员）
            //    走「生成 .reg + reg import」：不用引 Registry API，路径带空格、带引号都不会错。
            var reg = new System.Text.StringBuilder();
            reg.AppendLine("Windows Registry Editor Version 5.00");
            reg.AppendLine();
            reg.AppendLine("[HKEY_CURRENT_USER\\" + UninstallKey.Replace("/", "\\") + "]");
            reg.AppendLine($"\"DisplayName\"=\"Bulesrt Launcher\"");
            reg.AppendLine($"\"DisplayVersion\"=\"{RegEscape(string.IsNullOrWhiteSpace(version) ? "2.0.0" : version!)}\"");
            reg.AppendLine($"\"Publisher\"=\"xiang\"");
            reg.AppendLine($"\"DisplayIcon\"=\"{RegEscape(exe)},0\"");
            reg.AppendLine($"\"InstallLocation\"=\"{RegEscape(targetDir)}\"");
            reg.AppendLine($"\"UninstallString\"=\"\\\"{RegEscape(uninstall)}\\\" --uninstall\"");
            reg.AppendLine($"\"QuietUninstallString\"=\"\\\"{RegEscape(uninstall)}\\\" --uninstall --quiet\"");
            reg.AppendLine($"\"NoModify\"=dword:00000001");
            reg.AppendLine($"\"NoRepair\"=dword:00000001");
            reg.AppendLine($"\"EstimatedSize\"=dword:{DirectorySizeKb(targetDir):x8}");

            var regFile = Path.Combine(Path.GetTempPath(), "bulesrt-uninstall.reg");
            File.WriteAllText(regFile, reg.ToString(), System.Text.Encoding.Unicode);
            RunReg($"import \"{regFile}\"");
            try { File.Delete(regFile); } catch { }
        }
        catch
        {
            // 注册表写不进去只影响「应用和功能」里的条目，不影响卸载程序本身
        }
    }

    /// <summary>.reg 文件里的字符串转义：反斜杠要成对，双引号要写成 \"。</summary>
    private static string RegEscape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>跑一条 reg.exe 命令（隐藏窗口，失败不抛）。</summary>
    private static void RunReg(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch { }
    }

    private static int DirectorySizeKb(string dir)
    {
        try
        {
            var total = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
            return (int)Math.Min(int.MaxValue, total / 1024);
        }
        catch { return 0; }
    }

    /// <summary>
    /// 卸载：停掉正在跑的启动器，删快捷方式 / 开机自启 / 注册表项 / 程序文件。
    /// removeUserData=true 时连 data\ 和 plugins\ 一起删；否则保留，只留个空壳目录。
    /// </summary>
    public static async Task<InstallResult> RunUninstallAsync(string targetDir, bool removeUserData,
        IProgress<(double Percent, string Status)> progress, CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(targetDir)) return new InstallResult(false, "请选择安装位置", null);
        if (!Directory.Exists(targetDir)) return new InstallResult(false, "这个目录不存在，可能已经卸载过了", null);
        if (!IsInstalled(targetDir))
            return new InstallResult(false, "这个目录里没有 BulesrtLauncher.exe，看起来不是安装目录", null);

        try
        {
            progress.Report((10, "正在关闭启动器…"));
            await Task.Run(StopRunning, cancel);

            progress.Report((30, "正在删除快捷方式和开机自启…"));
            DeleteShortcuts();
            // 启动器自己写的开机自启值是 "RexeLauncher"；"BulesrtLauncher" 一并清掉，防止老版本残留
            RunReg($"delete \"{RunKey}\" /v \"BulesrtLauncher\" /f");
            RunReg($"delete \"{RunKey}\" /v \"{RunValueName}\" /f");

            progress.Report((50, "正在删除程序文件…"));
            var kept = new List<string>();
            foreach (var file in Directory.GetFiles(targetDir))
            {
                var name = Path.GetFileName(file);
                if (string.Equals(name, "uninstall.exe", StringComparison.OrdinalIgnoreCase)) continue; // 最后用脚本删自己
                try { File.Delete(file); }
                catch { kept.Add(name); }
            }

            foreach (var dir in Directory.GetDirectories(targetDir))
            {
                var name = Path.GetFileName(dir);
                var isUserData = name.Equals("data", StringComparison.OrdinalIgnoreCase) ||
                                 name.Equals("plugins", StringComparison.OrdinalIgnoreCase);
                if (isUserData && !removeUserData) continue;
                try { Directory.Delete(dir, true); }
                catch { kept.Add(name + "\\"); }
            }

            progress.Report((80, "正在清理注册表…"));
            RunReg($"delete \"{UninstallKey}\" /f");

            progress.Report((90, "正在收尾…"));
            var uninstallSelf = UninstallExe(targetDir);
            var leftovers = Directory.GetFileSystemEntries(targetDir)
                .Where(e => !string.Equals(e, uninstallSelf, StringComparison.OrdinalIgnoreCase))
                .ToList();
            ScheduleSelfDelete(targetDir, deleteDirectory: leftovers.Count == 0);

            progress.Report((100, "卸载完成"));
            var message = removeUserData
                ? "卸载完成，data\\ 和 plugins\\ 已一并删除"
                : "卸载完成（data\\ 和 plugins\\ 保留着，想彻底清掉可以手动删）";
            if (kept.Count > 0) message += $"，{kept.Count} 项被占用没删掉：" + string.Join("、", kept.Take(3));
            return new InstallResult(true, message, null);
        }
        catch (Exception ex)
        {
            return new InstallResult(false, "卸载失败：" + ex.Message, null);
        }
    }

    private static void DeleteShortcuts()
    {
        try
        {
            var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Bulesrt Launcher.lnk");
            var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Bulesrt Launcher.lnk");
            if (File.Exists(desktop)) File.Delete(desktop);
            if (File.Exists(programs)) File.Delete(programs);
        }
        catch { }
    }

    /// <summary>退出后由 cmd 删掉 uninstall.exe（deleteDirectory 时连目录一起删）。</summary>
    private static void ScheduleSelfDelete(string targetDir, bool deleteDirectory)
    {
        try
        {
            var self = UninstallExe(targetDir);
            var tail = deleteDirectory ? $" & rd /s /q \"{targetDir}\"" : "";
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c ping -n 3 127.0.0.1 >nul & del /f /q \"{self}\" >nul 2>nul{tail}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            Process.Start(psi);
        }
        catch { }
    }

    private static void WriteShortcut(string lnkPath, string target, string workingDir)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return;
            var shell = Activator.CreateInstance(type);
            if (shell is null) return;

            var shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            if (shortcut is null) return;

            var st = shortcut.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { target });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { target + ",0" });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Bulesrt Launcher - Windows 快速启动器" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, Array.Empty<object>());
        }
        catch
        {
            // shortcuts are best effort
        }
    }
}






