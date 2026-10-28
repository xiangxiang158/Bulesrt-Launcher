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

    // ============================================================ navigation
    private void BuildNav(string? selectId = null)
    {
        var wanted = selectId ?? _current;
        _nav.Clear();
        _nav.Add(new NavEntry { Id = "home", Title = "主页", Icon = "\uE80F", IsHome = true });
        _nav.Add(new NavEntry { Id = "all", Title = "全部", Icon = "\uE8FD" });
        foreach (var c in _store.Db.Categories)
            _nav.Add(new NavEntry { Id = c.Id, Title = c.Name, Icon = c.Icon });
        foreach (var page in Services.Plugins.PluginService.Pages)
        {
            var icon = string.IsNullOrWhiteSpace(page.Icon) ? "\uE8B7" : page.Icon;
            _nav.Add(new NavEntry
            {
                Id = "plugin:" + page.Id,
                Title = page.Title,
                Icon = icon,
                IsPlugin = true,
                PageId = page.Id
            });
        }
        UpdateCounts();

        var index = _nav.ToList().FindIndex(n => n.Id == wanted);
        NavList.SelectedIndex = index >= 0 ? index : 1;
    }

    private void UpdateCounts()
    {
        var total = _store.Db.Items.Count;
        foreach (var entry in _nav)
        {
            entry.Count = entry.Id switch
            {
                "home" or "all" => total,
                _ => _store.Db.Items.Count(i => i.CategoryId == entry.Id)
            };
        }
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is NavEntry entry)
        {
            _current = entry.Id;
            if (entry.IsPlugin)
            {
                ShowPluginPage(entry.PageId);
                return;
            }
            HidePluginPage();
            RefreshList();
        }
    }

    private string CategoryName(string id) =>
        _store.Db.Categories.FirstOrDefault(c => c.Id == id)?.Name ?? "未分类";

    private void RefreshList()
    {
        IEnumerable<AppItem> items = _store.Db.Items;
        string title;
        if (_current == "home")
        {
            items = items.OrderByDescending(i => i.RunCount).ThenBy(i => i.Name);
            title = "主页";
            BreadcrumbText.Text = "主页 / 总览";
        }
        else if (_current == "all")
        {
            items = items.OrderBy(i => i.Name);
            title = "全部";
            BreadcrumbText.Text = "全部";
        }
        else
        {
            items = items.Where(i => i.CategoryId == _current).OrderBy(i => i.Name);
            title = CategoryName(_current);
            BreadcrumbText.Text = $"全部 / {title}";
        }

        var list = items.ToList();
        _shown.Clear();
        foreach (var item in list) _shown.Add(item);

        HeaderTitle.Text = title;
        HeaderCount.Text = $"{list.Count} 个项目";
        StatusText.Text = $"已就绪 · {title}";
        UpdateCounts();
        EmptyHint.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_selected is not null) _selected.IsSelected = false;
        _selected = null;
    }


    // ============================================================ category drag / menu
    private const string CategoryDragFormat = "BulesrtLauncher.Category";
    private Point _navDragStart;
    private NavEntry? _navDragCandidate;

    private void NavList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _navDragStart = e.GetPosition(this);
        _navDragCandidate = NavContainerAt(e.GetPosition(NavList))?.DataContext as NavEntry;
    }

    private void NavList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _navDragCandidate is null) return;
        var candidate = _navDragCandidate;
        if (candidate.IsHome || candidate.Id == "all") return;

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _navDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _navDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _navDragCandidate = null;
        var data = new DataObject();
        data.SetData(CategoryDragFormat, candidate.Id);
        data.SetData(DataFormats.Text, candidate.Title);
        try
        {
            e.Handled = true;
            DragDrop.DoDragDrop(NavList, data, DragDropEffects.Move);
        }
        catch
        {
            // drag cancelled
        }
    }
    private void NavList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var container = NavContainerAt(e.GetPosition(NavList));
        if (container?.DataContext is not NavEntry entry || entry.IsHome || entry.Id == "all") return;

        e.Handled = true;
        var menu = new ContextMenu
        {
            PlacementTarget = container,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };
        menu.Items.Add(MenuItem("重命名分类", () => RenameCategoryPrompt(entry)));
        menu.Items.Add(MenuItem("删除分类", () => DeleteCategoryPrompt(entry)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("上移", () => MoveCategoryBy(entry, -1)));
        menu.Items.Add(MenuItem("下移", () => MoveCategoryBy(entry, 1)));
        menu.IsOpen = true;
    }

    private void RenameCategoryPrompt(NavEntry entry)
    {
        var dialog = new TextPrompt("重命名分类", "分类名称", entry.Title) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _store.RenameCategory(entry.Id, dialog.Value);
        BuildNav(entry.Id);
        Toast("分类已重命名");
    }

    private void DeleteCategoryPrompt(NavEntry entry)
    {
        Confirm("删除分类", $"确定删除「{entry.Title}」？该分类下的项目会移到「未分类」。", () =>
        {
            _store.DeleteCategory(entry.Id);
            var next = _current == entry.Id ? "all" : _current;
            _current = next;
            BuildNav(next);
            RefreshList();
            Toast($"已删除分类「{entry.Title}」");
        });
    }

    private void MoveCategoryBy(NavEntry entry, int delta)
    {
        if (!_store.MoveCategoryBy(entry.Id, delta)) return;
        BuildNav(entry.Id);
        Toast("分类顺序已更新");
    }


    // ============================================================ categories
    private void BtnNewCategory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TextPrompt("新建分类", "分类名称", "") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var cat = _store.AddCategory(dialog.Value);
        BuildNav();
        _current = cat.Id;
        NavList.SelectedIndex = _nav.ToList().FindIndex(n => n.Id == cat.Id);
        RefreshList();
    }


}

