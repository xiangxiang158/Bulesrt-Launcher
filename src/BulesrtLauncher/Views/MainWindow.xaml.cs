using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BulesrtLauncher.Models;
using BulesrtLauncher.Services.Icons;
using BulesrtLauncher.Services.Launching;
using BulesrtLauncher.Services.Platform;
using BulesrtLauncher.Services.Storage;
using BulesrtLauncher.Services.Ui;
using BulesrtLauncher.Services;
using WinForms = System.Windows.Forms;

namespace BulesrtLauncher.Views;

public partial class MainWindow : Window
{
    private readonly StoreService _store = App.Store;
    private readonly ObservableCollection<NavEntry> _nav = new();
    private readonly ObservableCollection<AppItem> _shown = new();
    private readonly HotkeyService _hotkey = new();
    private string _current = "all";
    private bool _gridView = true;
    private AppItem? _editing;
    private string _editorCategory = "misc";
    private string _batchCategory = "misc";
    private WinForms.NotifyIcon? _tray;
    private bool _forceClose;
    private string _hotkeyGesture = "Ctrl+Alt+Space";

    public MainWindow()
    {
        InitializeComponent();
        NavList.ItemsSource = _nav;
        GridItems.ItemsSource = _shown;
        ListItems.ItemsSource = _shown;

        _hotkey.Pressed += ToggleFromHotkey;
        BuildNav();
        InitBackdrop();
        InitRunningWatcher();
        InitPlugins();
        Root.SizeChanged += (_, _) => UpdateRootClip();
        UpdateRootClip();
        ApplySettingsToUi();
        RefreshList();
        Loaded += async (_, _) =>
        {
            Activate();
            NudgeRepaint();
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                NudgeRepaint();
            };
            timer.Start();
            await LoadIconsAsync();

            if (Environment.GetCommandLineArgs().Any(a => a.Equals("--plugins", StringComparison.OrdinalIgnoreCase)))
                ShowPluginsWindow();

            // --settings：直接打开设置面板（截图 / 快速进设置用）
            if (Environment.GetCommandLineArgs().Any(a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
                BtnSettings_Click(this, new RoutedEventArgs());
        };
        ContentRendered += (_, _) => NudgeRepaint();
        Activated += (_, _) => NudgeRepaint();
        PreviewKeyDown += Window_PreviewKeyDown;
    }

    // ============================================================ repaint guards
    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    /// <summary>
    /// Some GPU / remote-session setups leave the first frame black until the window is shown
    /// again. Forcing a layout + render pass (and a DWM flush) makes the first paint reliable.
    /// </summary>
    private void NudgeRepaint()
    {
        try
        {
            InvalidateVisual();
            UpdateLayout();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                InvalidateVisual();
                Root.InvalidateVisual();
                try { DwmFlush(); } catch { }
            }), System.Windows.Threading.DispatcherPriority.Render);
        }
        catch
        {
            // cosmetic only
        }
    }


    private void Status(string text) => StatusText.Text = text;
}

