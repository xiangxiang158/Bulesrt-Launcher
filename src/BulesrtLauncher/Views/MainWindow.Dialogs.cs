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
    // ============================================================ modal helpers
    private UIElement? _currentPanel;

    private void ShowPanel(UIElement panel, double width)
    {
        foreach (var child in new UIElement[] { PanelSettings, PanelEditor, PanelBatch, PanelConfirm, PanelUninstall, PanelCloseAsk, PanelConflict })
            child.Visibility = child == panel ? Visibility.Visible : Visibility.Collapsed;
        _currentPanel = panel;
        ModalCard.Width = width;
        ModalLayer.Visibility = Visibility.Visible;

        // 面板开着时没人看背景动图，停掉它，免得每 100ms 一次整窗重绘跟拖滑块抢渲染
        _animTimer.Stop();
    }

    private void CloseModal_Click(object sender, RoutedEventArgs e) => CloseModal();

    private void CloseModal()
    {
        ModalLayer.Visibility = Visibility.Collapsed;
        _editing = null;
        RefreshAnimation();
    }

    // ============================================================ duplicate handling

    private List<ItemDraft>? _dedupeDrafts;
    private List<(ItemDraft Draft, AppItem Existing)>? _dedupeConflicts;
    private Action? _afterDedupe;
    private UIElement? _panelBeforeConflict;
    private double _widthBeforeConflict;

    /// <summary>Adds items, asking the user what to do when a target already exists.</summary>
    private void AddDrafts(IEnumerable<ItemDraft> drafts, Action? onApplied = null)
    {
        var unique = drafts
            .Where(d => !string.IsNullOrWhiteSpace(d.Target))
            .GroupBy(d => StoreService.NormalizeTarget(d.Target))
            .Select(g => g.Last())
            .ToList();
        if (unique.Count == 0) return;

        var conflicts = unique
            .Select(d => (Draft: d, Existing: _store.FindByTarget(d.Target)))
            .Where(x => x.Existing is not null)
            .Select(x => (x.Draft, Existing: x.Existing!))
            .ToList();

        if (conflicts.Count == 0)
        {
            foreach (var draft in unique)
                _store.AddItem(draft.Name, draft.Target, draft.CategoryId, draft.Args, draft.WorkDir);
            onApplied?.Invoke();
            RefreshList();
            LoadIcons();
            Toast(unique.Count == 1 ? $"已添加 · {unique[0].Name}" : $"已添加 {unique.Count} 个项目");
            return;
        }

        _dedupeDrafts = unique;
        _dedupeConflicts = conflicts;
        _afterDedupe = onApplied;
        _panelBeforeConflict = _currentPanel;
        _widthBeforeConflict = ModalCard.Width;

        ConflictList.ItemsSource = conflicts.Select(c => new { c.Draft.Name, c.Draft.Target }).ToList();
        ConflictText.Text = conflicts.Count == 1
            ? $"「{conflicts[0].Draft.Name}」已经在列表里了。覆盖它（保留原位置、更新信息）还是跳过？"
            : $"有 {conflicts.Count} 个项目已经在列表里了。要覆盖它们还是跳过？";
        ShowPanel(PanelConflict, 540);
    }

    private void ConflictCancel_Click(object sender, RoutedEventArgs e)
    {
        _dedupeDrafts = null;
        _dedupeConflicts = null;
        _afterDedupe = null;
        if (_panelBeforeConflict is not null) ShowPanel(_panelBeforeConflict, _widthBeforeConflict);
        else CloseModal();
    }

    private void ConflictSkip_Click(object sender, RoutedEventArgs e) => ResolveConflicts(false);

    private void ConflictOverwrite_Click(object sender, RoutedEventArgs e) => ResolveConflicts(true);

    private void ResolveConflicts(bool overwrite)
    {
        var drafts = _dedupeDrafts;
        var conflicts = _dedupeConflicts ?? new List<(ItemDraft Draft, AppItem Existing)>();
        var after = _afterDedupe;
        _dedupeDrafts = null;
        _dedupeConflicts = null;
        _afterDedupe = null;
        if (drafts is null)
        {
            CloseModal();
            return;
        }

        var conflictKeys = conflicts.Select(c => StoreService.NormalizeTarget(c.Draft.Target)).ToHashSet();
        int added = 0, overwritten = 0, skipped = 0;

        foreach (var draft in drafts)
        {
            var key = StoreService.NormalizeTarget(draft.Target);
            if (conflictKeys.Contains(key))
            {
                if (!overwrite)
                {
                    skipped++;
                    continue;
                }
                var existing = conflicts.First(c => StoreService.NormalizeTarget(c.Draft.Target) == key).Existing;
                _store.ApplyTo(existing, draft.Name, draft.Target, draft.CategoryId, draft.Args, draft.WorkDir);
                existing.Icon = null;
                overwritten++;
            }
            else
            {
                _store.AddItem(draft.Name, draft.Target, draft.CategoryId, draft.Args, draft.WorkDir);
                added++;
            }
        }

        CloseModal();
        after?.Invoke();
        RefreshList();
        LoadIcons();

        var parts = new List<string>();
        if (added > 0) parts.Add($"新增 {added} 个");
        if (overwritten > 0) parts.Add($"覆盖 {overwritten} 个");
        if (skipped > 0) parts.Add($"跳过 {skipped} 个");
        Toast(parts.Count > 0 ? string.Join("，", parts) : "没有变更");
    }

    private void ModalLayer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == ModalLayer) CloseModal();
    }

    private void Confirm(string title, string text, Action onOk)
    {
        ConfirmTitle.Text = title;
        ConfirmText.Text = text;
        ConfirmOk.Click -= ConfirmOk_Click;
        ConfirmOk.Click += ConfirmOk_Click;
        void ConfirmOk_Click(object s, RoutedEventArgs args)
        {
            ConfirmOk.Click -= ConfirmOk_Click;
            CloseModal();
            onOk();
        }
        ShowPanel(PanelConfirm, 460);
    }


}

