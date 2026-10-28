using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Bulesrt.Sdk;

namespace BulesrtLauncher.Services.Plugins;

/// <summary>What happened to one plugin folder.</summary>
public sealed class PluginInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Version { get; init; } = "1.0.0";
    public string Author { get; init; } = "";
    public required string Folder { get; init; }
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public IPlugin? Instance { get; init; }
    public PluginContext? Context { get; init; }
}

/// <summary>One isolated load context per plugin, so two plugins can ship conflicting dependencies.</summary>
public sealed class PluginContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginContext(string mainAssemblyPath) : base(isCollectible: true)
        => _resolver = new AssemblyDependencyResolver(mainAssemblyPath);

    protected override Assembly? Load(AssemblyName name)
    {
        // Always use the host's SDK copy: a second copy would break type identity.
        if (string.Equals(name.Name, typeof(IPlugin).Assembly.GetName().Name, StringComparison.Ordinal))
            return null;

        var path = _resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}

/// <summary>Finds and loads plugins from <c>plugins\&lt;folder&gt;\plugin.json</c>.</summary>
public static class PluginService
{
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "plugins");

    public static List<PluginInfo> Loaded { get; } = new();
    public static List<IPluginCommand> Commands { get; } = new();
    public static List<IPluginItemAction> ItemActions { get; } = new();
    public static List<IPluginPage> Pages { get; } = new();

    public static event Action<LaunchEvent>? Launching;
    public static event Action? ItemsChanged;

    internal static void RaiseItemsChanged()
    {
        try { ItemsChanged?.Invoke(); } catch { /* ignore plugin errors */ }
    }

    public static int WorkingCount => Loaded.Count(p => p.Ok);

    /// <summary>Drops everything and loads the plugins folder again (new plugins only take effect after restart
    /// if they were already loaded — .NET cannot unload types that are still referenced).</summary>
    public static List<PluginInfo> LoadAll(Func<string, IPluginHost> hostFactory)
    {
        ShutdownAll();
        Loaded.Clear();
        Commands.Clear();
        ItemActions.Clear();
        Pages.Clear();

        try
        {
            Directory.CreateDirectory(Folder);
            DropReadmeIfEmpty();
        }
        catch
        {
            return Loaded;
        }

        foreach (var dir in Directory.GetDirectories(Folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var info = LoadOne(dir, hostFactory(dir));
            if (info is not null) Loaded.Add(info);
        }
        return Loaded;
    }

    /// <summary>First run: leaves a short note inside the empty plugins folder.</summary>
    private static void DropReadmeIfEmpty()
    {
        if (Directory.GetDirectories(Folder).Length > 0) return;
        var readme = Path.Combine(Folder, "README.txt");
        if (File.Exists(readme)) return;
        File.WriteAllText(readme,
            "把插件文件夹放进来，每个插件一个子文件夹，里面至少要有 plugin.json 和它的 dll。" + Environment.NewLine +
            Environment.NewLine +
            "plugin.json 示例：" + Environment.NewLine +
            "{ \"id\": \"hello\", \"name\": \"Hello Plugin\", \"version\": \"1.0.0\", \"assembly\": \"HelloPlugin.dll\" }" + Environment.NewLine +
            Environment.NewLine +
            "放好后重启启动器，或在 设置 → 数据 → 插件管理… 里查看加载结果。" + Environment.NewLine);
    }

    private static PluginInfo? LoadOne(string dir, IPluginHost host)
    {
        var manifestPath = Path.Combine(dir, "plugin.json");
        string id = Path.GetFileName(dir);
        string name = id;
        string version = "1.0.0";
        string author = "";
        string? assemblyName = null;
        string? typeName = null;

        if (File.Exists(manifestPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var v) && v.ValueKind == JsonValueKind.String) id = v.GetString()!;
                if (root.TryGetProperty("name", out v) && v.ValueKind == JsonValueKind.String) name = v.GetString()!;
                if (root.TryGetProperty("version", out v) && v.ValueKind == JsonValueKind.String) version = v.GetString()!;
                if (root.TryGetProperty("author", out v) && v.ValueKind == JsonValueKind.String) author = v.GetString()!;
                if (root.TryGetProperty("assembly", out v) && v.ValueKind == JsonValueKind.String) assemblyName = v.GetString();
                if (root.TryGetProperty("type", out v) && v.ValueKind == JsonValueKind.String) typeName = v.GetString();
            }
            catch (Exception ex)
            {
                return Fail(dir, id, name, version, author, "plugin.json 解析失败：" + ex.Message);
            }
        }
        else
        {
            return Fail(dir, id, name, version, author, "缺少 plugin.json");
        }

        assemblyName ??= Directory.GetFiles(dir, "*.dll").Select(Path.GetFileName).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(assemblyName))
            return Fail(dir, id, name, version, author, "文件夹里没有 dll");

        var assemblyPath = Path.Combine(dir, assemblyName!);
        if (!File.Exists(assemblyPath))
            return Fail(dir, id, name, version, author, $"找不到 {assemblyName}");

        try
        {
            var context = new PluginContext(assemblyPath);
            var assembly = context.LoadFromAssemblyPath(assemblyPath);

            Type? type = typeName is null ? null : assembly.GetType(typeName, throwOnError: false);
            type ??= assembly.GetTypes()
                .FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null);

            if (type is null)
                return Fail(dir, id, name, version, author, "没有找到实现 IPlugin 的公开类");

            var instance = (IPlugin)Activator.CreateInstance(type)!;
            instance.Initialize(host);

            return new PluginInfo
            {
                Id = id,
                Name = name,
                Version = version,
                Author = author,
                Folder = dir,
                Ok = true,
                Instance = instance,
                Context = context
            };
        }
        catch (Exception ex)
        {
            return Fail(dir, id, name, version, author, ex.GetType().Name + "：" + ex.Message);
        }
    }

    private static PluginInfo Fail(string dir, string id, string name, string version, string author, string error)
        => new()
        {
            Id = id,
            Name = name,
            Version = version,
            Author = author,
            Folder = dir,
            Ok = false,
            Error = error
        };

    public static void ShutdownAll()
    {
        foreach (var plugin in Loaded.Where(p => p.Ok && p.Instance is not null))
        {
            try { plugin.Instance!.Shutdown(); }
            catch { /* a broken plugin must not break shutdown */ }
        }
        foreach (var plugin in Loaded)
        {
            try { plugin.Context?.Unload(); } catch { }
        }
    }

    /// <summary>Fires the launch hook; returns false when a plugin vetoed the launch.</summary>
    public static bool RaiseLaunching(LaunchEvent args)
    {
        var handler = Launching;
        if (handler is null) return true;
        foreach (var d in handler.GetInvocationList().Cast<Action<LaunchEvent>>())
        {
            try { d(args); }
            catch { /* a throwing plugin does not block the launch */ }
        }
        return !args.Cancel;
    }
}
