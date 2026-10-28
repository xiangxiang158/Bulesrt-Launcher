using System.IO;

namespace BulesrtLauncher.Services.Platform;

public sealed record ShortcutRow(string Name, string Path, string Category);

public static class Importer
{
    private static readonly string Desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private static readonly string PublicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    private static readonly string StartMenu =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs");

    private static readonly (string Category, string[] Keys)[] Rules =
    {
        ("steam", new[] { "steam", "蒸汽" }),
        ("minecraft", new[] { "minecraft", "我的世界", "hmcl", "pcl" }),
        ("code", new[] { "code", "studio", "idea", "pycharm", "github", "docker", "python", "node", "git",
                          "sublime", "webstorm", "clion", "devenv", "terminal", "pwsh", "cmd" }),
        ("games", new[] { "game", "游戏", "launcher", "启动器", "原神", "崩坏", "明日方舟", "鸣潮", "绝区零",
                           "三角洲", "泰拉", "雀魂", "缺氧" }),
        ("tools", new[] { "tool", "工具箱", "驱动", "截图", "压缩", "7-zip", "winrar", "bandizip", "everything",
                           "powertoys", "obs", "剪映", "格式工厂", "火绒", "packet tracer", "notepad" }),
        ("software", new[] { "office", "word", "excel", "powerpoint", "wechat", "微信", "qq", "钉钉", "飞书",
                              "网易云", "音乐", "bilibili", "哔哩", "potplayer", "vlc", "chrome", "edge", "firefox" }),
    };

    public static string Classify(string name, string path, string urlText = "")
    {
        var blob = $"{name} {path} {urlText}".ToLowerInvariant();
        foreach (var (category, keys) in Rules)
            if (keys.Any(k => blob.Contains(k)))
                return category;
        return "misc";
    }

    private static IEnumerable<string> EnumShortcuts(string folder, int depth)
    {
        if (!Directory.Exists(folder)) yield break;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = depth > 0,
            MaxRecursionDepth = depth,
            IgnoreInaccessible = true
        };
        foreach (var file in Directory.EnumerateFiles(folder, "*.*", options))
        {
            var ext = Path.GetExtension(file);
            if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
                yield return file;
        }
    }

    public static List<ShortcutRow> Collect(int limit = 48)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ShortcutRow>();

        void Push(string path, bool fromStartMenu)
        {
            var key = Path.GetFileName(path);
            if (!seen.Add(key)) return;
            var name = Path.GetFileNameWithoutExtension(path);
            var url = "";
            if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            {
                try { url = File.ReadAllText(path); } catch { }
            }
            result.Add(new ShortcutRow(name, path, Classify(name, path, url)));
        }

        foreach (var folder in new[] { Desktop, PublicDesktop })
            foreach (var p in EnumShortcuts(folder, 1))
                Push(p, false);

        if (result.Count < limit)
        {
            foreach (var p in EnumShortcuts(StartMenu, 3))
            {
                Push(p, true);
                if (result.Count >= limit * 2) break;
            }
        }

        return result.Take(limit).ToList();
    }
}

