using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace BulesrtLauncher.Models;

/// <summary>A single launchable entry shown in the grid / list.</summary>
public sealed class AppItem : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string CategoryId { get; set; } = "misc";
    public int RunCount { get; set; }
    public DateTime LastRun { get; set; }

    private ImageSource? _icon;
    [JsonIgnore]
    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasIcon)); }
    }

    private bool _isSelected;
    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    private bool _isRunning;
    /// <summary>True while at least one instance of the target process exists.</summary>
    [JsonIgnore]
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    private int _liveInstances;
    /// <summary>How many instances are open right now — the "多开" counter.</summary>
    [JsonIgnore]
    public int LiveInstances
    {
        get => _liveInstances;
        set
        {
            if (_liveInstances == value) return;
            _liveInstances = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(InstanceText));
            OnPropertyChanged(nameof(HasManyInstances));
        }
    }

    [JsonIgnore] public bool HasManyInstances => LiveInstances > 1;
    [JsonIgnore] public string InstanceText => LiveInstances > 1 ? $"×{LiveInstances}" : "";
    [JsonIgnore] public string StatusText => IsRunning
        ? (LiveInstances > 1 ? $"运行中 ×{LiveInstances}" : "运行中")
        : (RunCount > 0 ? $"打开 {RunCount} 次" : "");

    [JsonIgnore] public bool HasIcon => _icon is not null;
    [JsonIgnore] public string Letter => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();
    [JsonIgnore] public string RunText => RunCount > 0 ? $"打开 {RunCount} 次" : "";

    public event PropertyChangedEventHandler? PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
