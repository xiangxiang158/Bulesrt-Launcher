using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Bulesrt.Sdk;
using BulesrtLauncher.Services.Plugins;

namespace BulesrtLauncher.Views;

public partial class PluginsWindow : Window
{
    private readonly ObservableCollection<Row> _rows = new();

    public PluginsWindow()
    {
        InitializeComponent();
        PluginList.ItemsSource = _rows;
        Reload();
    }

    private void Reload()
    {
        _rows.Clear();

        var plugins = PluginService.Loaded;
        foreach (var p in plugins)
        {
            _rows.Add(new Row
            {
                Name = p.Name,
                VersionText = "  v" + p.Version,
                Detail = p.Ok
                    ? (string.IsNullOrWhiteSpace(p.Author) ? p.Folder : $"{p.Author} · {p.Folder}")
                    : (p.Error ?? "加载失败"),
                Status = p.Ok ? "已加载" : "失败",
                StatusBrush = p.Ok ? new SolidColorBrush(Color.FromRgb(0x2F, 0xA3, 0x6A))
                                   : new SolidColorBrush(Color.FromRgb(0xC0, 0x50, 0x50))
            });
        }

        if (_rows.Count == 0)
        {
            _rows.Add(new Row
            {
                Name = "还没有插件",
                VersionText = "",
                Detail = "把插件文件夹（含 plugin.json）放到 plugins\\ 里，然后点「重新加载」或重启启动器。",
                Status = "空",
                StatusBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x5E, 0x75))
            });
        }

        SummaryText.Text = $"共 {plugins.Count} 个：{plugins.Count(p => p.Ok)} 个已加载" +
                           (plugins.Any(p => !p.Ok) ? $"，{plugins.Count(p => !p.Ok)} 个失败" : "") +
                           $" · 命令 {PluginService.Commands.Count} 个" +
                           $" · 菜单动作 {PluginService.ItemActions.Count} 个" +
                           $" · 页面 {PluginService.Pages.Count} 个";
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(PluginService.Folder);
            Process.Start(new ProcessStartInfo(PluginService.Folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "打开插件目录失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => Reload();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public sealed class Row
    {
        public string Name { get; init; } = "";
        public string VersionText { get; init; } = "";
        public string Detail { get; init; } = "";
        public string Status { get; init; } = "";
        public Brush StatusBrush { get; init; } = Brushes.Gray;
    }
}
