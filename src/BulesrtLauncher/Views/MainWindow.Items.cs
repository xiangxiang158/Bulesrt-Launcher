using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BulesrtLauncher.Models;
using BulesrtLauncher.Services.Icons;
using BulesrtLauncher.Services.Launching;
using BulesrtLauncher.Services.Platform;
using BulesrtLauncher.Services.Storage;
using BulesrtLauncher.Services.Ui;
using WinForms = System.Windows.Forms;

namespace BulesrtLauncher.Views;

public partial class MainWindow
{
    // ============================================================ selection
    private AppItem? _selected;

    private void SelectItem(AppItem? item, bool scroll = false)
    {
        if (_selected is not null) _selected.IsSelected = false;
        _selected = item;
        if (item is null)
        {
            StatusText.Text = $"已就绪 · {HeaderTitle.Text}";
            return;
        }
        item.IsSelected = true;
        StatusText.Text = $"已选择 · {item.Name}   （Enter 启动 / Delete 删除）";
        if (scroll) BringItemIntoView(item);
    }

    private void BringItemIntoView(AppItem item)
    {
        var host = _gridView ? (ItemsControl)GridItems : ListItems;
        if (host.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement element)
            element.BringIntoView();
    }

    private int GridColumns()
    {
        if (!_gridView) return 1;
        var width = GridItems.ActualWidth;
        if (width <= 0) return 1;
        return Math.Max(1, (int)(width / 106));
    }

    private void MoveSelection(int dx, int dy)
    {
        if (_shown.Count == 0) return;
        var index = _selected is null ? -1 : _shown.IndexOf(_selected);
        var columns = GridColumns();
        var step = dx + dy * columns;
        var next = index < 0
            ? (step >= 0 ? 0 : _shown.Count - 1)
            : Math.Clamp(index + step, 0, _shown.Count - 1);
        SelectItem(_shown[next], true);
    }

    private void MoveSelectionEdge(bool last)
    {
        if (_shown.Count == 0) return;
        SelectItem(last ? _shown[^1] : _shown[0], true);
    }

    private void LaunchSelected()
    {
        if (_selected is null) return;
        Launch(_selected, false);
    }

    private void ContentArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemFromSource(e.OriginalSource as DependencyObject) is null)
            SelectItem(null);
    }

    private void ContentArea_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemFromSource(e.OriginalSource as DependencyObject) is not null) return;
        e.Handled = true;
        var menu = new ContextMenu { PlacementTarget = sender as UIElement, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        menu.Items.Add(MenuItem("新建项目", () => OpenEditor(null)));
        menu.Items.Add(MenuItem("批量添加", () => BtnBatch_Click(this, new RoutedEventArgs())));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("清理重复项", CleanDuplicates));
        menu.Items.Add(MenuItem("刷新图标", RefreshIcons));
        menu.Items.Add(MenuItem(_gridView ? "切换为列表视图" : "切换为图标视图", () =>
        {
            _gridView = !_gridView;
            _store.Db.Settings.View = _gridView ? "grid" : "list";
            _store.Save();
            ApplyView();
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("设置", OpenSettingsPanel));
        menu.IsOpen = true;
    }

    private void RefreshIcons()
    {
        IconService.ClearCache();
        foreach (var item in _store.Db.Items) item.Icon = null;
        Toast("正在重新提取图标…");
        LoadIcons();
    }

    private void CleanDuplicates()
    {
        var groups = _store.Db.Items
            .GroupBy(i => StoreService.NormalizeTarget(i.Target))
            .Where(g => g.Key.Length > 0 && g.Count() > 1)
            .ToList();

        if (groups.Count == 0)
        {
            Toast("没有发现重复项");
            return;
        }

        var removable = groups
            .SelectMany(g => g.OrderByDescending(i => i.RunCount).ThenBy(i => i.Id).Skip(1))
            .Select(i => i.Id)
            .ToList();

        Confirm("清理重复项",
            $"发现 {groups.Count} 组、共 {removable.Count} 个重复条目。每组保留使用次数最多的一个，确定清理？",
            () =>
            {
                _store.DeleteItems(removable);
                RefreshList();
                Toast($"已清理 {removable.Count} 个重复项");
            });
    }

    private void OpenSettingsPanel()
    {
        BtnSettings_Click(this, new RoutedEventArgs());
    }

    private static AppItem? ItemFromSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: AppItem item }) return item;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }


    private void LoadIcons()
    {
        _ = LoadIconsAsync();
    }

    private async Task LoadIconsAsync()
    {
        var pending = _store.Db.Items.Where(i => i.Icon is null).ToList();
        if (pending.Count == 0) return;

        var tasks = pending.Select(async item =>
        {
            var icon = await IconService.GetAsync(item.Target);
            if (icon is not null) item.Icon = icon;
        });
        await Task.WhenAll(tasks);
    }

    // ============================================================ item actions
    private AppItem? ItemFrom(object sender)
        => (sender as FrameworkElement)?.DataContext as AppItem;

    private void Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemFrom(sender) is not { } item) return;
        if (e.ClickCount >= 2)
        {
            Launch(item, false);
            return;
        }
        SelectItem(item);
        _dragStart = e.GetPosition(this);
        _dragCandidate = item;
    }


    private void Tile_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemFrom(sender) is not { } item) return;
        e.Handled = true;
        SelectItem(item);
        var menu = BuildItemMenu(item);
        menu.PlacementTarget = sender as UIElement;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private ContextMenu BuildItemMenu(AppItem item)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("打开", () => Launch(item, false)));
        menu.Items.Add(MenuItem("以管理员身份运行", () => Launch(item, true)));
        menu.Items.Add(MenuItem("用资源管理器方式打开（兼容）", () => LaunchCompat(item)));
        menu.Items.Add(MenuItem("再开一个实例（多开）", () => Launch(item, false)));
        if (item.IsRunning)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItem(
                item.LiveInstances > 1 ? $"切到运行中的窗口（{item.LiveInstances} 个实例）" : "切到运行中的窗口",
                () => ActivateRunning(item)));
            menu.Items.Add(MenuItem("结束运行中的实例", () => CloseRunning(item)));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("打开所在位置", () => LaunchService.OpenContainingFolder(item.Target)));
        menu.Items.Add(MenuItem("编辑", () => OpenEditor(item)));
        menu.Items.Add(MenuItem("复制路径", () =>
        {
            try { System.Windows.Clipboard.SetText(item.Target); Toast("路径已复制"); }
            catch { /* ignored */ }
        }));
        AppendPluginItemActions(menu, item);
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("删除", () => Confirm("删除项目", $"确定删除「{item.Name}」？", () =>
        {
            _store.DeleteItems(new[] { item.Id });
            if (_selected?.Id == item.Id) _selected = null;
            RefreshList();
            Toast($"已删除 · {item.Name}");
        })));
        return menu;
    }

    private static MenuItem MenuItem(string header, Action action)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => action();
        return mi;
    }

    /// <summary>兼容启动：交给 explorer.exe，和鼠标双击完全同一条路径。</summary>
    private void LaunchCompat(AppItem item)
    {
        var (ok, err) = LaunchService.LaunchViaExplorer(item);
        if (ok) Toast($"已用资源管理器打开 · {item.Name}");
        else Toast("启动失败：" + err, true);
    }

    private void Launch(AppItem item, bool admin)
    {
        var evt = new Bulesrt.Sdk.LaunchEvent(item.Id, item.Name, item.Target, admin);
        if (!Services.Plugins.PluginService.RaiseLaunching(evt))
        {
            Status($"插件拦下了这次启动 · {item.Name}");
            Toast($"插件拦下了 · {item.Name}", true);
            return;
        }

        var (ok, error) = LaunchService.Launch(item, admin);
        if (ok)
        {
            _store.BumpRun(item);
            Status($"已启动 · {item.Name}");
            Toast($"已启动 · {item.Name}");
            if (_store.Db.Settings.HideAfterLaunch) Hide();

            // the running dot should show up on the next paint, not 3 seconds later
            var refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            refresh.Tick += (_, _) => { refresh.Stop(); RefreshRunning(); };
            refresh.Start();
        }
        else
        {
            Status($"启动失败：{error}");
            Toast($"启动失败：{error}", true);
            MessageBox.Show(this, error, "启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }


}

