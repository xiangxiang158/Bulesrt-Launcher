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
using System.Windows.Media.Imaging;
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
    // ============================================================ view
    private readonly DispatcherTimer _surfaceTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly DispatcherTimer _backdropSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly DispatcherTimer _animTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private string? _backdropPath;
    private BitmapSource? _backdropImage;
    private AnimatedBackdrop? _animation;
    private int _frameIndex;

    /// <summary>拖动 / 缩放窗口期间为 true：此时关掉模糊与动图，退出后恢复。</summary>
    private bool _windowMoving;

    /// <summary>模糊时用来放大背景的那个变换，复用一个实例，拖模糊滑块时不再每次 new。</summary>
    private readonly ScaleTransform _bgZoom = new(1, 1);

    private void ApplySettingsToUi()
    {
        var s = _store.Db.Settings;
        _gridView = s.View != "list";
        ViewGridChip.IsChecked = _gridView;
        ViewListChip.IsChecked = !_gridView;
        ApplyView();
        ApplyScale(s.Scale);
        BtnTheme.Content = s.Theme == "light" ? "\uE708" : "\uE706";
    }

    private void ApplyView()
    {
        GridItems.Visibility = _gridView ? Visibility.Visible : Visibility.Collapsed;
        ListItems.Visibility = _gridView ? Visibility.Collapsed : Visibility.Visible;
        ViewGridChip.IsChecked = _gridView;
        ViewListChip.IsChecked = !_gridView;
        BtnView.Content = _gridView ? "\uE8FD" : "\uE80A";
    }

    private void BtnView_Click(object sender, RoutedEventArgs e)
    {
        _gridView = !_gridView;
        _store.Db.Settings.View = _gridView ? "grid" : "list";
        _store.Save();
        ApplyView();
    }

    private void ViewChip_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewGridChip is null || ViewListChip is null) return;
        _gridView = ViewGridChip.IsChecked == true;
        _store.Db.Settings.View = _gridView ? "grid" : "list";
        _store.Save();
        ApplyView();
    }

    private void ApplyScale(int scale)
    {
        var factor = Math.Clamp(scale, 80, 150) / 100.0;
        Root.LayoutTransform = factor == 1.0 ? null : new ScaleTransform(factor, factor);
    }

    private void BtnTheme_Click(object sender, RoutedEventArgs e)
    {
        var s = _store.Db.Settings;
        s.Theme = s.Theme == "light" ? "dark" : "light";
        _store.Save();
        ThemeService.Apply(s.Theme, s.SurfaceOpacity);
        ApplyAcrylicTint();
        BtnTheme.Content = s.Theme == "light" ? "\uE708" : "\uE706";
    }

    // ============================================================ backdrop
    /// <summary>Loads the user picture (cached), applies dim + blur and refreshes the settings labels.</summary>
    private void ApplyBackdrop(bool forceReload = false)
    {
        var s = _store.Db.Settings;
        var path = BackgroundService.Resolve(s.BackgroundImage);
        if (forceReload || !string.Equals(path, _backdropPath, StringComparison.OrdinalIgnoreCase))
        {
            _backdropPath = path;
            _backdropImage = path is null ? null : BackgroundService.Decode(path);
            _animation = path is not null && BackgroundService.IsAnimated(path)
                ? BackgroundService.LoadAnimation(path)
                : null;
        }

        BgImage.Source = _backdropImage;
        BgImage.Visibility = _backdropImage is null ? Visibility.Collapsed : Visibility.Visible;
        // 0 = absolutely clean picture; the scrims grow with the dim slider
        var dim = Math.Clamp(s.BackgroundDim, 0, 0.85);
        BgScrim.Opacity = _backdropImage is null ? 0 : dim;
        TopScrim.Opacity = _backdropImage is null ? 0 : Math.Min(0.55, dim * 1.25);
        LeftScrim.Opacity = _backdropImage is null ? 0 : Math.Min(0.50, dim * 1.15);
        ApplyBackdropBlur(_backdropImage is null ? 0 : s.BackgroundBlur);
        ApplyWindowTransparency();
        RefreshAnimation();
        UpdateBackgroundLabels();
    }

    private void ApplyWindowTransparency()
    {
        // The shell is composited as glass (WindowChrome GlassFrameThickness="-1"), so fading the
        // backdrop layer reveals the desktop / acrylic behind it — the "主题页透明" effect.
        // Dialogs live outside that layer, so they stay fully solid.
        var transparent = Math.Clamp(_store.Db.Settings.WindowTransparency, 0, 1);
        Backdrop.Opacity = 1 - transparent;
    }

    /// <summary>Starts / stops the GIF player depending on the setting.</summary>
    private void RefreshAnimation()
    {
        _animTimer.Stop();
        _frameIndex = 0;
        if (_animation is null || _animation.Frames.Count < 2) return;

        BgImage.Source = _animation.Frames[0].Frame;
        if (!_store.Db.Settings.BackdropAnimation) return;
        _animTimer.Interval = _animation.Frames[0].Delay;
        _animTimer.Start();
    }

    private void AnimationTick()
    {
        if (_animation is null || _animation.Frames.Count < 2) return;
        if (!IsVisible || WindowState == WindowState.Minimized) return;
        _frameIndex = (_frameIndex + 1) % _animation.Frames.Count;
        var (frame, delay) = _animation.Frames[_frameIndex];
        BgImage.Source = frame;
        _animTimer.Interval = delay;
    }

    /// <summary>Blur bleeds at the edges, so the picture is scaled up a touch before blurring.</summary>
    private void ApplyBackdropBlur(double radius)
    {
        radius = Math.Clamp(radius, 0, 60);
        BgBlurEffect.Radius = radius;

        var zoom = radius > 0.5 ? 1 + radius / 90.0 : 1;
        if (zoom <= 1.0001)
        {
            if (BgImage.RenderTransform is not null && BgImage.RenderTransform != Transform.Identity)
                BgImage.RenderTransform = Transform.Identity;
            return;
        }

        _bgZoom.ScaleX = zoom;
        _bgZoom.ScaleY = zoom;
        BgImage.RenderTransformOrigin = new Point(0.5, 0.5);
        if (!ReferenceEquals(BgImage.RenderTransform, _bgZoom)) BgImage.RenderTransform = _bgZoom;
    }

    private void ScheduleSurfaceApply(double value)
    {
        _store.Db.Settings.SurfaceOpacity = Math.Clamp(value, 0, 100) / 100.0;
        _surfaceTimer.Stop();
        _surfaceTimer.Start();
    }

    private void ScheduleBackdropSave()
    {
        _backdropSaveTimer.Stop();
        _backdropSaveTimer.Start();
    }

    private void InitBackdrop()
    {
        _animTimer.Tick += (_, _) => AnimationTick();
        _surfaceTimer.Tick += (_, _) =>
        {
            _surfaceTimer.Stop();
            ThemeService.SetSurface(_store.Db.Settings.SurfaceOpacity);
        };
        _backdropSaveTimer.Tick += (_, _) =>
        {
            _backdropSaveTimer.Stop();
            _store.Save();
        };
        EnsureDefaultBackdrop();
        ApplyBackdrop();
    }

    /// <summary>
    /// First run (no backdrop ever chosen): installs the bundled blue artwork as the default
    /// wallpaper with the "插画 + 透明盒子" look. Existing profiles are left untouched.
    /// </summary>
    private void EnsureDefaultBackdrop()
    {
        var s = _store.Db.Settings;
        if (s.BackdropChosen) return;

        if (string.IsNullOrWhiteSpace(s.BackgroundImage) || BackgroundService.Resolve(s.BackgroundImage) is null)
        {
            var name = BackgroundService.ImportArtwork();
            if (name is not null)
            {
                s.BackgroundImage = name;
                s.BackgroundBlur = 18;
                s.BackgroundDim = 0.30;
                s.SurfaceOpacity = 1.0;
                s.WindowTransparency = 0;
                s.BackdropAnimation = true;
                s.Theme = "dark";
            }
        }

        s.BackdropChosen = true;
        _store.Save();
    }
}
