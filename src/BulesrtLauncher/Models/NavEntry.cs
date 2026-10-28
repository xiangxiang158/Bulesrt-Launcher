using System.ComponentModel;

namespace BulesrtLauncher.Models;

/// <summary>Sidebar row (主页 / 全部 / a category).</summary>
public sealed class NavEntry : INotifyPropertyChanged
{
    public string Id { get; set; } = "all";
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "\uE8B7";
    public bool IsHome { get; set; }

    /// <summary>True for a page contributed by a plugin (sidebar entry below the categories).</summary>
    public bool IsPlugin { get; set; }

    /// <summary>The plugin page this row opens (only when <see cref="IsPlugin"/>).</summary>
    public string PageId { get; set; } = "";

    private int _count;
    public int Count
    {
        get => _count;
        set
        {
            _count = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
