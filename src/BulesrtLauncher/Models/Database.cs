using System.Collections.ObjectModel;

namespace BulesrtLauncher.Models;

/// <summary>Root object serialised to data.json.</summary>
public sealed class Database
{
    public int Version { get; set; } = 1;
    public int NextId { get; set; } = 1;

    /// <summary>
    /// True once the library has been seeded (or was delivered pre-populated). Keeps an
    /// intentionally empty library from re-importing desktop shortcuts on every launch.
    /// </summary>
    public bool Seeded { get; set; }

    public AppSettings Settings { get; set; } = new();
    public ObservableCollection<Category> Categories { get; set; } = new();
    public ObservableCollection<AppItem> Items { get; set; } = new();
}
