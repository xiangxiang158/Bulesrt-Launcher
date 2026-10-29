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
    private int _runningVersion;

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

        RefreshRunningAsync();
    }

    /// <summary>
    /// 扫描放到后台线程：以前整张进程表（含系统进程）每 3 秒在 UI 线程上走一遍，
    /// 还要对每个进程读 MainModule，一次几十毫秒的卡顿就是它。现在只深挖项目库里出现过的
    /// 进程名，UI 线程只负责把绿点换掉。
    /// </summary>
    private async void RefreshRunningAsync()
    {
        _runningBusy = true;
        var version = ++_runningVersion;
        try
        {
            var items = _store.Db.Items.ToArray();
            if (items.Length == 0) return;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var (processName, _) = RunningService.ForItem(item);
                if (!string.IsNullOrEmpty(processName)) names.Add(processName);
            }
            if (names.Count == 0) return;

            var counts = await Task.Run(() =>
            {
                var snapshot = RunningService.Snapshot(names);
                var result = new int[items.Length];
                for (var i = 0; i < items.Length; i++)
                {
                    var (processName, exePath) = RunningService.ForItem(items[i]);
                    result[i] = RunningService.Lookup(snapshot, exePath, processName).Count;
                }
                return result;
            }).ConfigureAwait(true);

            if (version != _runningVersion) return;   // 期间又刷新过，这批结果丢掉

            for (var i = 0; i < items.Length; i++)
            {
                items[i].LiveInstances = counts[i];
                items[i].IsRunning = counts[i] > 0;
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
