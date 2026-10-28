using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BulesrtLauncher.Models;
using BulesrtLauncher.Services.Launching;

namespace BulesrtLauncher.Services.Platform;

/// <summary>Snapshot of the process table, keyed by executable path so generic hosts
/// (electron.exe, python.exe, node.exe …) are not lumped together.</summary>
public sealed class ProcessSnapshot
{
    /// <summary>exe path → (processes, windows that actually show up, first window handle)</summary>
    public Dictionary<string, (int Processes, int Windows, IntPtr Window)> ByPath { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, (int Processes, int Windows, IntPtr Window)> ByName { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Detects whether an item is already running — and how many instances of it are open, which is
/// what "多开" looks like from the outside.
/// </summary>
public static class RunningService
{
    private static readonly Dictionary<string, (string? Name, string? Path)> TargetCache =
        new(StringComparer.OrdinalIgnoreCase);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    /// <summary>
    /// Maps a launch target (.exe / .lnk / folder / url) to its process name and full exe path.
    /// Counting is done on the path: matching by name alone invents "×8" on shared hosts.
    /// </summary>
    public static (string? Name, string? Path) Resolve(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return (null, null);
        if (TargetCache.TryGetValue(target, out var cached)) return cached;

        string? name = null;
        string? path = null;
        try
        {
            var resolved = target;
            if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                resolved = ShortcutResolver.Resolve(target)?.Target ?? target;

            if (resolved.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                path = resolved;
                name = Path.GetFileNameWithoutExtension(resolved);
            }
        }
        catch
        {
            name = null;
            path = null;
        }

        TargetCache[target] = (name, path);
        return (name, path);
    }

    public static string? ProcessNameFor(string target) => Resolve(target).Name;

    public static (string? Name, string? Path) ForItem(AppItem item) => Resolve(item.Target);

    /// <summary>One pass over the process table.</summary>
    public static ProcessSnapshot Snapshot()
    {
        var snapshot = new ProcessSnapshot();
        Process[] all;
        try { all = Process.GetProcesses(); }
        catch { return snapshot; }

        foreach (var proc in all)
        {
            try
            {
                var name = proc.ProcessName;
                if (string.IsNullOrEmpty(name)) continue;

                IntPtr window;
                try { window = proc.MainWindowHandle; } catch { window = IntPtr.Zero; }
                var hasWindow = window != IntPtr.Zero;

                string? path = null;
                try
                {
                    var module = proc.MainModule;
                    if (module is not null) path = module.FileName;
                }
                catch
                {
                    // elevated / protected process — path unavailable, name still known
                }

                if (!string.IsNullOrEmpty(path))
                {
                    snapshot.ByPath.TryGetValue(path, out var byPath);
                    snapshot.ByPath[path] = (byPath.Processes + 1,
                        byPath.Windows + (hasWindow ? 1 : 0),
                        byPath.Window == IntPtr.Zero ? window : byPath.Window);
                }

                snapshot.ByName.TryGetValue(name, out var byName);
                snapshot.ByName[name] = (byName.Processes + 1,
                    byName.Windows + (hasWindow ? 1 : 0),
                    byName.Window == IntPtr.Zero ? window : byName.Window);
            }
            catch
            {
                // a process may vanish between enumeration and inspection
            }
            finally
            {
                proc.Dispose();
            }
        }
        return snapshot;
    }

    /// <summary>
    /// Exact-path match first. The count is the number of processes that actually have a window,
    /// because browsers and Electron apps fork half a dozen window-less helpers — counting raw
    /// processes turned a single Edge window into "×12". A process with no window (tray app) counts 1.
    /// Falls back to the process name for aliases (Store notepad, launchers that re-exec themselves).
    /// </summary>
    public static (int Count, IntPtr Window) Lookup(ProcessSnapshot snapshot, string? exePath, string? processName = null)
    {
        if (!string.IsNullOrEmpty(exePath) && snapshot.ByPath.TryGetValue(exePath, out var byPath))
            return (byPath.Windows > 0 ? byPath.Windows : 1, byPath.Window);

        if (!string.IsNullOrEmpty(processName) && snapshot.ByName.TryGetValue(processName, out var byName))
            return (byName.Windows > 0 ? byName.Windows : 1, byName.Window);

        return (0, IntPtr.Zero);
    }

    /// <summary>Bring the running instance to the foreground (restores it if minimised).</summary>
    public static bool Activate(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        try
        {
            ShowWindow(window, 9 /* SW_RESTORE */);
            return SetForegroundWindow(window);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Closes every instance of that exact executable. Returns how many were asked to exit.</summary>
    public static int CloseAll(string? exePath, string? processName)
    {
        if (string.IsNullOrEmpty(processName)) return 0;
        var closed = 0;
        Process[] procs;
        try { procs = Process.GetProcessesByName(processName); }
        catch { return 0; }

        foreach (var proc in procs)
        {
            try
            {
                if (proc.HasExited) continue;

                // never touch a different executable that happens to share the process name
                if (!string.IsNullOrEmpty(exePath))
                {
                    string? path = null;
                    try { path = proc.MainModule?.FileName; } catch { }
                    if (path is not null && !string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                proc.CloseMainWindow();
                if (!proc.WaitForExit(1500)) proc.Kill();
                closed++;
            }
            catch
            {
                // protected process — skip
            }
            finally
            {
                proc.Dispose();
            }
        }
        return closed;
    }
}
