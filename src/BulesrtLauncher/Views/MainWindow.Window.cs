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
    // ============================================================ shell / chrome
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        try
        {
            var round = 2;
            DwmSetWindowAttribute(handle, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
            ApplyAcrylicTint();

            // WPF paints an opaque black composition surface by default, which would hide the
            // desktop where our layers are faded.
            if (PresentationSource.FromVisual(this) is HwndSource source)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
                source.AddHook(WndProc);
            }
        }
        catch
        {
            // cosmetic
        }
        RegisterHotkey();
        CreateTray();
    }


    // ============================================================ rounded / resize for the layered window
    /// <summary>Border does not clip its child to the corner radius, so the window shape is clipped by hand.</summary>
    private void UpdateRootClip()
    {
        var w = Root.ActualWidth;
        var h = Root.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var radius = WindowState == WindowState.Maximized ? 0 : 12;

        // 尺寸没变就别重新裁一次，缩放窗口时这个回调会被高频调用
        if (Root.Clip is RectangleGeometry current
            && Math.Abs(current.Rect.Width - w) < 0.5
            && Math.Abs(current.Rect.Height - h) < 0.5
            && Math.Abs(current.RadiusX - radius) < 0.01)
            return;

        Root.Clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
    }

    /// <summary>
    /// A layered window (AllowsTransparency) has no non-client frame, so Windows never offers resize
    /// borders or a work-area-aware maximize. Both are answered by hand here.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        const int WM_GETMINMAXINFO = 0x0024;
        const int WM_ENTERSIZEMOVE = 0x0231;
        const int WM_EXITSIZEMOVE = 0x0232;

        switch (msg)
        {
            // 拖动 / 缩放窗口时停掉模糊和动图：分层窗口每帧都要整窗重绘再合成，
            // 带着 Gaussian 模糊拖起来就是明显的掉帧。
            case WM_ENTERSIZEMOVE:
                EnterWindowInteraction();
                break;

            case WM_EXITSIZEMOVE:
                ExitWindowInteraction();
                break;

            case WM_NCHITTEST when ResizeMode == ResizeMode.CanResize && WindowState == WindowState.Normal:
            {
                var screen = new Point((short)(lParam.ToInt64() & 0xFFFF), (short)((lParam.ToInt64() >> 16) & 0xFFFF));
                var p = PointFromScreen(screen);
                const double grip = 6;
                var left = p.X <= grip;
                var right = p.X >= ActualWidth - grip;
                var top = p.Y <= grip;
                var bottom = p.Y >= ActualHeight - grip;

                int? hit = (left, right, top, bottom) switch
                {
                    (true, _, true, _) => 13,   // HTTOPLEFT
                    (_, true, true, _) => 14,   // HTTOPRIGHT
                    (true, _, _, true) => 16,   // HTBOTTOMLEFT
                    (_, true, _, true) => 17,   // HTBOTTOMRIGHT
                    (true, _, _, _) => 10,      // HTLEFT
                    (_, true, _, _) => 11,      // HTRIGHT
                    (_, _, true, _) => 12,      // HTTOP
                    (_, _, _, true) => 15,      // HTBOTTOM
                    _ => null
                };
                if (hit is null) break;
                handled = true;
                return (IntPtr)hit.Value;
            }

            case WM_GETMINMAXINFO:
            {
                var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
                var mi = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref mi))
                {
                    info.ptMaxPosition.X = mi.rcWork.Left - mi.rcMonitor.Left;
                    info.ptMaxPosition.Y = mi.rcWork.Top - mi.rcMonitor.Top;
                    info.ptMaxSize.X = mi.rcWork.Right - mi.rcWork.Left;
                    info.ptMaxSize.Y = mi.rcWork.Bottom - mi.rcWork.Top;
                    info.ptMaxTrackSize.X = info.ptMaxSize.X;
                    info.ptMaxTrackSize.Y = info.ptMaxSize.Y;
                    Marshal.StructureToPtr(info, lParam, false);
                    handled = true;
                }
                break;
            }
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public MinMaxPoint ptReserved;
        public MinMaxPoint ptMaxSize;
        public MinMaxPoint ptMaxPosition;
        public MinMaxPoint ptMinTrackSize;
        public MinMaxPoint ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public WinRect rcMonitor;
        public WinRect rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    /// <summary>The acrylic material follows the app theme: dark tint in dark mode, ivory in light.</summary>
    private void ApplyAcrylicTint()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            var dark = ThemeService.Current == "dark" ? 1 : 0;
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        }
        catch
        {
            // cosmetic
        }
    }


    private void RegisterHotkey()
    {
        var wanted = _store.Db.Settings.Hotkey;
        if (_hotkey.Register(this, wanted))
        {
            _hotkeyGesture = wanted;
        }
        else
        {
            foreach (var alt in new[] { "Ctrl+Alt+F9", "Ctrl+Shift+F9" })
            {
                if (_hotkey.Register(this, alt))
                {
                    _hotkeyGesture = alt;
                    Toast($"主热键被占用，已临时改用 {alt}");
                    break;
                }
            }
        }
        HotkeyHint.Text = $"{_hotkeyGesture} 唤起";
    }

    private void CreateTray()
    {
        try
        {
            var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "");
            if (icon is null) return;
            _tray = new WinForms.NotifyIcon
            {
                Icon = icon,
                Text = "Bulesrt Launcher",
                Visible = true
            };
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("显示主界面", null, (_, _) => ShowFromTray());
            menu.Items.Add("以管理员身份重新启动", null, (_, _) => RestartAsAdmin());
            menu.Items.Add("退出", null, (_, _) =>
            {
                _forceClose = true;
                Close();
            });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => ShowFromTray();
        }
        catch
        {
            // tray is optional
        }
    }

    private void RestartAsAdmin()
    {
        if (IntegrityService.Current == IntegrityService.Level.High ||
            IntegrityService.Current == IntegrityService.Level.System)
        {
            Toast("当前已经是管理员权限");
            return;
        }
        if (IntegrityService.RestartElevated())
        {
            _forceClose = true;
            Close();
        }
        else
        {
            Toast("提权被取消或失败", true);
        }
    }

    /// <summary>启动时检查权限级别：低权限会让部分程序（卸载器、系统工具）启动失败。</summary>
    private void WarnIfRestricted()
    {
        var level = IntegrityService.Current;
        try
        {
            var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "data");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "launch.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} 进程完整性级别 = {IntegrityService.Describe(level)}{Environment.NewLine}");
        }
        catch { /* logging must never break startup */ }

        if (level == IntegrityService.Level.Low)
        {
            Toast("当前以「低权限」运行，卸载器/系统工具类程序可能启动失败；右键托盘图标可「以管理员身份重新启动」", true);
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        NudgeRepaint();
    }

    private void ToggleFromHotkey()
    {
        if (IsVisible && WindowState != WindowState.Minimized && IsActive)
        {
            Hide();
        }
        else
        {
            ShowFromTray();
        }
    }

    /// <summary>窗口拖动 / 缩放前调用：只保留最朴素的绘制，别在移动循环里做模糊和逐帧动图。</summary>
    private void EnterWindowInteraction()
    {
        if (_windowMoving) return;
        _windowMoving = true;
        _animTimer.Stop();
        BgBlurEffect.Radius = 0;
        BgImage.RenderTransform = Transform.Identity;
        // 大图插值也是逐帧成本，拖动时降一档，肉眼几乎看不出差别
        RenderOptions.SetBitmapScalingMode(BgImage, BitmapScalingMode.LowQuality);
    }

    /// <summary>移动循环结束后恢复原来的模糊与动图。</summary>
    private void ExitWindowInteraction()
    {
        if (!_windowMoving) return;
        _windowMoving = false;
        RenderOptions.SetBitmapScalingMode(BgImage, BitmapScalingMode.HighQuality);
        ApplyBackdropBlur(_backdropImage is null ? 0 : _store.Db.Settings.BackgroundBlur);
        // 弹窗开着的时候背景动图本来就是停的，别在拖完窗口后把它叫回来
        if (IsVisible && WindowState != WindowState.Minimized && ModalLayer.Visibility != Visibility.Visible)
            RefreshAnimation();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        // DragMove 会进自己的模态循环，前后各兜一次（WM_ENTERSIZEMOVE 有时先到有时后到）
        EnterWindowInteraction();
        try { DragMove(); }
        catch { /* ignored */ }
        finally { ExitWindowInteraction(); }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        Root.Margin = new Thickness(0);
        UpdateRootClip();
    }

    private void BtnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnClose_Click(object sender, RoutedEventArgs e) => RouteClose();

    private bool _trayTipShown;

    private void RouteClose()
    {
        switch (_store.Db.Settings.CloseAction)
        {
            case "tray":
                HideToTray();
                break;
            case "exit":
                ExitApp();
                break;
            default:
                ShowCloseAsk();
                break;
        }
    }

    private void ShowCloseAsk()
    {
        CloseRemember.IsChecked = false;
        ShowPanel(PanelCloseAsk, 460);
    }

    private void HideToTray()
    {
        Hide();
        if (_tray is not null && !_trayTipShown)
        {
            _trayTipShown = true;
            try
            {
                _tray.ShowBalloonTip(2500, "Bulesrt Launcher", "已最小化到托盘，双击托盘图标可再次打开", WinForms.ToolTipIcon.Info);
            }
            catch
            {
                // balloons are optional
            }
        }
    }

    private void ExitApp()
    {
        _forceClose = true;
        Close();
    }

    private void CloseAskCancel_Click(object sender, RoutedEventArgs e) => CloseModal();

    private void CloseAskTray_Click(object sender, RoutedEventArgs e)
    {
        RememberCloseAction("tray");
        CloseModal();
        HideToTray();
    }

    private void CloseAskExit_Click(object sender, RoutedEventArgs e)
    {
        RememberCloseAction("exit");
        CloseModal();
        ExitApp();
    }

    private void RememberCloseAction(string action)
    {
        if (CloseRemember.IsChecked != true) return;
        _store.Db.Settings.CloseAction = action;
        _store.Save();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceClose)
        {
            switch (_store.Db.Settings.CloseAction)
            {
                case "tray":
                    e.Cancel = true;
                    HideToTray();
                    return;
                case "exit":
                    break;
                default:
                    e.Cancel = true;
                    ShowCloseAsk();
                    return;
            }
        }
        _tray?.Dispose();
        _hotkey.Dispose();
        if (!_uninstalling) _store.Save();   // 卸载时不能回写，否则刚删掉的 data\ 又会长出来
        base.OnClosing(e);
    }

}


