using System;
using System.Windows;
using System.Windows.Threading;
using BulesrtLauncher.Services.Platform;

namespace BulesrtLauncher.Views;

public partial class MainWindow
{
    // ============================================================ running / multi-instance watcher
    private readonly DispatcherTimer _runningTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _runningBusy;
    private bool _runningCleared;

    private void InitRunningWatcher()
    {
        _runningTimer.Tick += (_, _) => RefreshRunning();
        _runningTimer.Start();
        // give the first paint a moment, then mark whatever is already open
        Dispatcher.BeginInvoke(new Action(RefreshRunning), DispatcherPriority.Background);
    }

    /// <summary>
    /// One process-table pass per tick, then every item gets its "running" flag and instance count.
    /// This is the "多开检测": an item with two open copies shows 运行中 ×2.
    /// </summary>
    private void RefreshRunning()
    {
        if (_runningBusy) return;

        if (!_store.Db.Settings.ShowRunning)
        {
            if (!_runningCleared) ClearRunningFlags();
            return;
        }
        if (!IsVisible || WindowState == WindowState.Minimized) return;

        _runningBusy = true;
        try
        {
            var snapshot = RunningService.Snapshot();
            foreach (var item in _store.Db.Items)
            {
                var (processName, exePath) = RunningService.ForItem(item);
                var (count, _) = RunningService.Lookup(snapshot, exePath, processName);
                item.LiveInstances = count;
                item.IsRunning = count > 0;
            }
            _runningCleared = false;
        }
        catch
        {
            // a process can disappear mid-enumeration; next tick fixes it
        }
        finally
        {
            _runningBusy = false;
        }
    }

    private void ClearRunningFlags()
    {
        foreach (var item in _store.Db.Items)
        {
            item.IsRunning = false;
            item.LiveInstances = 0;
        }
        _runningCleared = true;
    }

    /// <summary>Focus the window of the already running copy instead of starting another one.</summary>
    private void ActivateRunning(Models.AppItem item)
    {
        var (processName, exePath) = RunningService.ForItem(item);
        if (exePath is null && processName is null) return;
        var (count, window) = RunningService.Lookup(RunningService.Snapshot(), exePath, processName);
        if (count == 0)
        {
            Toast("现在没有在运行");
            return;
        }
        if (window == IntPtr.Zero)
        {
            Toast($"有 {count} 个实例在运行，但都找不到主窗口（可能在托盘里）");
            return;
        }
        if (RunningService.Activate(window))
        {
            Status($"已切到 · {item.Name}");
            if (_store.Db.Settings.HideAfterLaunch) Hide();
        }
        else
        {
            Toast("切不过去：窗口属于更高权限的进程");
        }
    }

    private void CloseRunning(Models.AppItem item)
    {
        var (process, exePath) = RunningService.ForItem(item);
        var (count, _) = RunningService.Lookup(RunningService.Snapshot(), exePath, process);
        if (count == 0)
        {
            Toast("现在没有在运行");
            return;
        }

        var label = count > 1 ? $"{count} 个实例" : "1 个实例";
        Confirm("结束进程", $"确定结束「{item.Name}」的{label}？未保存的内容会丢失。", () =>
        {
            var closed = RunningService.CloseAll(exePath, process);
            Status($"已结束 {closed} 个进程 · {item.Name}");
            Toast($"已结束 {closed} 个进程");
            Dispatcher.BeginInvoke(new Action(RefreshRunning), DispatcherPriority.Background);
        });
    }
}
