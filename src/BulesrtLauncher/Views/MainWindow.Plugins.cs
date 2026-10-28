using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Bulesrt.Sdk;
using BulesrtLauncher.Models;
using BulesrtLauncher.Services.Plugins;
using BulesrtLauncher.Services.Storage;

namespace BulesrtLauncher.Views;

public partial class MainWindow
{
    private PluginHost? _pluginHost;
    private int[] _lastItemIds = Array.Empty<int>();

    // ============================================================ plugin wiring
    private void InitPlugins()
    {
        try
        {
            _pluginHost = new PluginHost(this, PluginService.Folder);
            var list = PluginService.LoadAll(dir => new PluginHost(this, dir));
            var ok = list.Count(p => p.Ok);
            var failed = list.Count(p => !p.Ok);
            if (ok > 0 || failed > 0)
            {
                var message = $"插件 {ok} 个已加载" + (failed > 0 ? $"，{failed} 个失败" : "");
                Status(message);
                Toast(message);
            }
        }
        catch (Exception ex)
        {
            Status("插件加载失败：" + ex.Message);
        }
    }

    private static void LogPluginError(string where, Exception ex)
    {
        try
        {
            File.AppendAllText(Path.Combine(StoreService.DataDir, "plugins.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{where}] {ex}{Environment.NewLine}");
        }
        catch
        {
            // best effort
        }
    }

    private void OpenPluginsFolder()
    {
        try
        {
            Directory.CreateDirectory(PluginService.Folder);
            Process.Start(new ProcessStartInfo(PluginService.Folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Toast("打不开插件目录：" + ex.Message, true);
        }
    }

    private void ShowPluginsWindow()
    {
        try
        {
            var window = new PluginsWindow { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            LogPluginError("PluginsWindow", ex);
            Toast("插件面板打开失败：" + ex.Message, true);
        }
    }

    private void ShowPluginCommands()
    {
        if (PluginService.Commands.Count == 0)
        {
            Toast("还没有插件提供命令");
            return;
        }
        try
        {
            var window = new PluginCommandsWindow(_pluginHost!) { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            LogPluginError("PluginCommandsWindow", ex);
            Toast("命令面板打开失败：" + ex.Message, true);
        }
    }

    private readonly Dictionary<string, FrameworkElement> _pluginViews = new();

    /// <summary>Shows a plugin page in the content area (built lazily, cached per page id).</summary>
    private void ShowPluginPage(string pageId)
    {
        var page = PluginService.Pages.FirstOrDefault(p => p.Id == pageId);
        if (page is null)
        {
            HidePluginPage();
            RefreshList();
            return;
        }

        if (!_pluginViews.TryGetValue(pageId, out var view))
        {
            try
            {
                view = page.CreateView(_pluginHost!);
            }
            catch (Exception ex)
            {
                LogPluginError("Page:" + pageId, ex);
                Toast("插件页面创建失败：" + ex.Message, true);
                return;
            }
            if (view is null) return;
            _pluginViews[pageId] = view;
        }

        PluginPageArea.Content = view;
        PluginPageArea.Visibility = Visibility.Visible;
        ContentScroll.Visibility = Visibility.Collapsed;
        EmptyHint.Visibility = Visibility.Collapsed;
        HeaderTitle.Text = page.Title;
        HeaderCount.Text = "插件页面";
        BreadcrumbText.Text = "插件 / " + page.Title;
        StatusText.Text = "插件页面 · " + page.Title;
    }

    private void HidePluginPage()
    {
        PluginPageArea.Visibility = Visibility.Collapsed;
        PluginPageArea.Content = null;
        ContentScroll.Visibility = Visibility.Visible;
    }

    /// <summary>Appends the actions every plugin registered for this item.</summary>
    private void AppendPluginItemActions(System.Windows.Controls.ContextMenu menu, AppItem item)
    {
        var actions = PluginService.ItemActions;
        if (actions.Count == 0) return;

        var reference = new ItemRef(item);
        var added = false;
        foreach (var action in actions)
        {
            bool applies;
            try { applies = action.AppliesTo(reference); }
            catch { applies = false; }
            if (!applies) continue;

            if (!added)
            {
                menu.Items.Add(new System.Windows.Controls.Separator());
                added = true;
            }
            var captured = action;
            menu.Items.Add(MenuItem(captured.Title, () =>
            {
                try { captured.Execute(reference, _pluginHost!); }
                catch (Exception ex) { Toast("插件动作失败：" + ex.Message, true); }
            }));
        }
    }

    /// <summary>Notifies plugins when the library changed by other means.</summary>
    private void RaiseItemsChangedIfNeeded()
    {
        var ids = _store.Db.Items.Select(i => i.Id).ToArray();
        if (ids.SequenceEqual(_lastItemIds)) return;
        _lastItemIds = ids;
        PluginService.RaiseItemsChanged();
    }

    // ============================================================ the host object
    private sealed class PluginHost : IPluginHost
    {
        private readonly MainWindow _window;
        private readonly string _folder;

        public PluginHost(MainWindow window, string folder)
        {
            _window = window;
            _folder = folder;
        }

        public string Version => typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";
        public string DataDirectory => StoreService.DataDir;
        public string PluginDirectory => _folder;
        public string Theme => _window._store.Db.Settings.Theme;

        public IReadOnlyList<IItemRef> Items =>
            _window._store.Db.Items.Select(i => (IItemRef)new ItemRef(i)).ToList();

        public IReadOnlyList<string> Categories =>
            _window._store.Db.Categories.Select(c => c.Id).ToList();

        public IItemRef? AddItem(string name, string target, string categoryId = "misc")
        {
            if (string.IsNullOrWhiteSpace(target)) return null;
            if (_window._store.FindByTarget(target) is not null) return null;

            var item = _window._store.AddItem(name, target, categoryId);
            _window.Dispatcher.Invoke(() =>
            {
                _window.RefreshList();
                _window.Status($"插件添加了 · {item.Name}");
            });
            return new ItemRef(item);
        }

        public bool RemoveItem(int id)
        {
            var item = _window._store.Db.Items.FirstOrDefault(i => i.Id == id);
            if (item is null) return false;
            _window._store.DeleteItems(new[] { id });
            _window.Dispatcher.Invoke(_window.RefreshList);
            return true;
        }

        public void Toast(string message) => _window.Dispatcher.Invoke(() => _window.Toast(message));

        public void Status(string message) => _window.Dispatcher.Invoke(() => _window.Status(message));

        public void Log(string message)
        {
            try
            {
                File.AppendAllText(Path.Combine(StoreService.DataDir, "plugins.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Path.GetFileName(_folder)}] {message}{Environment.NewLine}");
            }
            catch
            {
                // logging is best effort
            }
        }

        public void RegisterCommand(IPluginCommand command) => PluginService.Commands.Add(command);

        public void RegisterItemAction(IPluginItemAction action) => PluginService.ItemActions.Add(action);

        public void RegisterPage(IPluginPage page)
        {
            if (string.IsNullOrWhiteSpace(page.Id)) return;
            if (PluginService.Pages.Any(p => string.Equals(p.Id, page.Id, StringComparison.OrdinalIgnoreCase)))
                return; // first plugin wins on duplicate page ids

            PluginService.Pages.Add(page);
            _window.Dispatcher.BeginInvoke(new Action(() => _window.BuildNav()));
        }

        public event Action<LaunchEvent>? Launching
        {
            add => PluginService.Launching += value;
            remove => PluginService.Launching -= value;
        }

        public event Action? ItemsChanged
        {
            add => PluginService.ItemsChanged += value;
            remove => PluginService.ItemsChanged -= value;
        }

        public void Refresh() => _window.Dispatcher.Invoke(_window.RefreshList);
    }

    /// <summary>Read-only adapter from the model to the SDK contract.</summary>
    private sealed class ItemRef : IItemRef
    {
        private readonly AppItem _item;
        public ItemRef(AppItem item) => _item = item;

        public int Id => _item.Id;
        public string Name => _item.Name;
        public string Target => _item.Target;
        public string CategoryId => _item.CategoryId;
    }
}
