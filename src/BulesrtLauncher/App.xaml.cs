using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using BulesrtLauncher.Services.Storage;
using BulesrtLauncher.Services.Ui;

namespace BulesrtLauncher;

public partial class App : Application
{
    public static StoreService Store { get; } = new();
    private Mutex? _singleInstance;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    protected override void OnStartup(StartupEventArgs e)
    {
        // One instance per install folder: a portable copy on the desktop must be able to run
        // next to the installed one (each has its own data.json).
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(AppContext.BaseDirectory.ToLowerInvariant())))[..12];

        _singleInstance = new Mutex(true, "BulesrtLauncher.SingleInstance." + fingerprint, out var isFirst);
        if (!isFirst)
        {
            // Second launch of *this* copy: bring its window back instead of opening another
            // process that would fight over the same data.json.
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        Store.Load();

        // Remote sessions / virtual GPUs occasionally lose the DWM redirection surface and the
        // window stays black until it is re-shown. Software rendering avoids that class of bug.
        try
        {
            var forceSoftware = Environment.GetEnvironmentVariable("REXE_SOFTWARE_RENDER") == "1"
                                || Store.Db.Settings.SoftwareRender;
            var weakGpu = System.Windows.Media.RenderCapability.Tier >> 16 < 2;
            if (forceSoftware || SystemParameters.IsRemoteSession || weakGpu)
                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        }
        catch
        {
            // rendering mode is best-effort
        }

        ThemeService.Apply(Store.Db.Settings.Theme, Store.Db.Settings.SurfaceOpacity);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Bulesrt Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
    }

    /// <summary>Focus the already running window of this same install folder.</summary>
    private static void ActivateExistingInstance()
    {
        try
        {
            var self = Environment.ProcessPath;
            foreach (var process in Process.GetProcessesByName("BulesrtLauncher"))
            {
                string? path = null;
                try { path = process.MainModule?.FileName; } catch { }
                if (path is null || !string.Equals(path, self, StringComparison.OrdinalIgnoreCase)) continue;
                if (process.MainWindowHandle == IntPtr.Zero) continue;

                ShowWindow(process.MainWindowHandle, 9 /* SW_RESTORE */);
                SetForegroundWindow(process.MainWindowHandle);
                return;
            }
        }
        catch
        {
            // focusing is best effort
        }
    }
}


