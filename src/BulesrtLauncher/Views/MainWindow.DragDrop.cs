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
    // ============================================================ drag to category
    private const string ItemDragFormat = "BulesrtLauncher.AppItem";
    private Point _dragStart;
    private AppItem? _dragCandidate;
    private ListBoxItem? _navHighlight;

    private void Tile_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var item = _dragCandidate;
        _dragCandidate = null;
        var data = new DataObject();
        data.SetData(ItemDragFormat, item.Id);
        data.SetData(DataFormats.Text, item.Name);
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        }
        catch
        {
            // drag cancelled
        }
    }

    private ListBoxItem? NavContainerAt(Point point)
    {
        var hit = NavList.InputHitTest(point) as DependencyObject;
        while (hit is not null)
        {
            if (hit is ListBoxItem item) return item;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }

    private void HighlightNav(NavEntry? entry)
    {
        if (_navHighlight is not null)
        {
            _navHighlight.ClearValue(BackgroundProperty);
            _navHighlight = null;
        }
        if (entry is null) return;
        if (NavList.ItemContainerGenerator.ContainerFromItem(entry) is ListBoxItem container)
        {
            container.Background = (Brush)FindResource("AccentSoftBrush");
            _navHighlight = container;
        }
    }

    private void NavList_DragOver(object sender, DragEventArgs e)
    {
        var container = NavContainerAt(e.GetPosition(NavList));
        var entry = container?.DataContext as NavEntry;
        var hasCategory = e.Data.GetDataPresent(CategoryDragFormat);
        var hasItem = e.Data.GetDataPresent(ItemDragFormat);
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);

        var isCategory = entry is { IsHome: false } && entry.Id != "all";
        var accept = hasCategory ? isCategory : (hasItem || hasFiles) && isCategory;

        e.Effects = accept ? (hasFiles ? DragDropEffects.Copy : DragDropEffects.Move) : DragDropEffects.None;
        HighlightNav(accept ? entry : null);
        e.Handled = true;
    }
    private void NavList_DragLeave(object sender, DragEventArgs e) => HighlightNav(null);

    private void NavList_Drop(object sender, DragEventArgs e)
    {
        HighlightNav(null);
        var entry = NavContainerAt(e.GetPosition(NavList))?.DataContext as NavEntry;
        if (entry is null || entry.IsHome || entry.Id == "all")
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        if (e.Data.GetData(CategoryDragFormat) is string categoryId)
        {
            if (_store.MoveCategory(categoryId, entry.Id))
            {
                BuildNav(categoryId);
                Toast($"分类顺序已更新");
            }
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(ItemDragFormat) is int id)
        {
            _store.MoveItems(new[] { id }, entry.Id);
            RefreshList();
            Toast($"已移动到「{entry.Title}」");
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var drafts = files
                .Select(file => new ItemDraft(Path.GetFileNameWithoutExtension(file), file, entry.Id, "", ""))
                .ToList();
            AddDrafts(drafts);
            e.Handled = true;
        }
    }


    // ============================================================ drag & drop
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var target = _current is "home" or "all" ? "misc" : _current;
        var drafts = files
            .Select(file => new ItemDraft(Path.GetFileNameWithoutExtension(file), file, target, "", ""))
            .ToList();
        AddDrafts(drafts);
    }


}


