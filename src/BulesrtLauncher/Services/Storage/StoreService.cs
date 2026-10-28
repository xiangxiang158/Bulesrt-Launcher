using System.IO;
using System.Text.Json;
using BulesrtLauncher.Models;
using BulesrtLauncher.Services.Launching;
using BulesrtLauncher.Services.Platform;

namespace BulesrtLauncher.Services.Storage;

public sealed class StoreService
{
    public static string BaseDir => AppContext.BaseDirectory;
    public static string DataDir { get; } = Path.Combine(BaseDir, "data");
    public static string DbPath => Path.Combine(DataDir, "data.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public Database Db { get; private set; } = new();

    public static readonly (string Id, string Name, string Icon)[] DefaultCategories =
    {
        ("games", "游戏", "\uE7FC"),
        ("steam", "steam", "\uE774"),
        ("minecraft", "我的世界", "\uE7B8"),
        ("tools", "工具", "\uE90F"),
        ("software", "软件", "\uE71D"),
        ("code", "编程", "\uE943"),
        ("misc", "未分类", "\uE8B7"),
    };

    public void Load()
    {
        Directory.CreateDirectory(DataDir);
        if (File.Exists(DbPath))
        {
            try
            {
                var json = File.ReadAllText(DbPath);
                Db = JsonSerializer.Deserialize<Database>(json) ?? new Database();
            }
            catch
            {
                Db = new Database();
            }
        }

        if (Db.Categories.Count == 0)
        {
            foreach (var (id, name, icon) in DefaultCategories)
                Db.Categories.Add(new Category { Id = id, Name = name, Icon = icon });
        }
        if (Db.Items.Count > 0)
        {
            Db.Seeded = true;                 // already populated — never seed over it
        }
        else if (!Db.Seeded && Db.Items.Count == 0 && Db.Categories.Count > 0)
        {
            Seed();
            Db.Seeded = true;
        }
        Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            var tmp = DbPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Db, JsonOptions));
            File.Move(tmp, DbPath, true);
        }
        catch
        {
            // never crash the UI because of a failed save
        }
    }

    public AppItem AddItem(string name, string target, string categoryId, string args = "", string workdir = "")
    {
        var item = new AppItem
        {
            Id = Db.NextId++,
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(target.TrimEnd('\\', '/')) : name,
            Target = target,
            CategoryId = categoryId,
            Arguments = args,
            WorkingDirectory = workdir
        };
        Db.Items.Add(item);
        Save();
        return item;
    }

    public void DeleteItems(IEnumerable<int> ids)
    {
        var set = ids.ToHashSet();
        foreach (var item in Db.Items.Where(i => set.Contains(i.Id)).ToList())
            Db.Items.Remove(item);
        Save();
    }

    /// <summary>Comparable form of a target: resolves shortcuts, environment variables and ..</summary>
    public static string NormalizeTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return "";
        var value = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        if (value.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = ShortcutResolver.Resolve(value)?.Target;
            if (!string.IsNullOrWhiteSpace(resolved)) value = resolved;
        }
        if (value.Contains("://") || value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return value.ToLowerInvariant();
        try
        {
            return Path.GetFullPath(value).TrimEnd('\\', '/').ToLowerInvariant();
        }
        catch
        {
            return value.ToLowerInvariant();
        }
    }

    public AppItem? FindByTarget(string target)
    {
        var key = NormalizeTarget(target);
        if (key.Length == 0) return null;
        return Db.Items.FirstOrDefault(i => NormalizeTarget(i.Target) == key);
    }

    /// <summary>Overwrite an existing entry with fresh values (used by “覆盖”).</summary>
    public void ApplyTo(AppItem item, string name, string target, string categoryId, string args, string workdir)
    {
        item.Name = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileNameWithoutExtension(target.TrimEnd('\\', '/'))
            : name;
        item.Target = target;
        item.CategoryId = categoryId;
        item.Arguments = args;
        item.WorkingDirectory = workdir;
        Save();
    }

    public void MoveItems(IEnumerable<int> ids, string categoryId)
    {
        var set = ids.ToHashSet();
        foreach (var item in Db.Items.Where(i => set.Contains(i.Id)))
            item.CategoryId = categoryId;
        Save();
    }

    public void BumpRun(AppItem item)
    {
        item.RunCount++;
        item.LastRun = DateTime.Now;
        Save();
    }

    public Category AddCategory(string name)
    {
        var cat = new Category
        {
            Id = "c" + Guid.NewGuid().ToString("N")[..6],
            Name = string.IsNullOrWhiteSpace(name) ? "新分类" : name.Trim(),
            Icon = "\uE8B7"
        };
        Db.Categories.Add(cat);
        Save();
        return cat;
    }

    public void RenameCategory(string id, string name)
    {
        var cat = Db.Categories.FirstOrDefault(c => c.Id == id);
        if (cat is not null && !string.IsNullOrWhiteSpace(name))
        {
            cat.Name = name.Trim();
            Save();
        }
    }

    /// <summary>Move a category to another category's position (drag-to-swap in the sidebar).</summary>
    public bool MoveCategory(string id, string targetId)
    {
        if (string.Equals(id, targetId, StringComparison.Ordinal)) return false;
        var from = Db.Categories.FirstOrDefault(c => c.Id == id);
        var to = Db.Categories.FirstOrDefault(c => c.Id == targetId);
        if (from is null || to is null) return false;
        var fromIndex = Db.Categories.IndexOf(from);
        var toIndex = Db.Categories.IndexOf(to);
        Db.Categories.Move(fromIndex, toIndex);
        Save();
        return true;
    }

    /// <summary>Shift a category by -1 / +1 in the list.</summary>
    public bool MoveCategoryBy(string id, int delta)
    {
        var cat = Db.Categories.FirstOrDefault(c => c.Id == id);
        if (cat is null) return false;
        var index = Db.Categories.IndexOf(cat);
        var target = index + delta;
        if (target < 0 || target >= Db.Categories.Count) return false;
        Db.Categories.Move(index, target);
        Save();
        return true;
    }

    public void DeleteCategory(string id)
    {
        var cat = Db.Categories.FirstOrDefault(c => c.Id == id);
        if (cat is null) return;
        Db.Categories.Remove(cat);
        foreach (var item in Db.Items.Where(i => i.CategoryId == id))
            item.CategoryId = "misc";
        Save();
    }

    private void Seed()
    {
        var rows = Importer.Collect(48);
        foreach (var row in rows)
        {
            Db.Items.Add(new AppItem
            {
                Id = Db.NextId++,
                Name = row.Name,
                Target = row.Path,
                CategoryId = row.Category
            });
        }
    }
}


