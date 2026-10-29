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
    private readonly DispatcherTimer _blurTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };

    /// <summary>动图逐帧烘焙的上限：超过这个帧数就按原样播放，避免烘焙吃光内存。</summary>
    private const int MaxBlurAnimationFrames = 80;
    private string? _backdropPath;
    private BitmapSource? _backdropImage;
    private AnimatedBackdrop? _animation;
    private int _frameIndex;

    // 预烘焙的模糊结果：静态图一张、动图每帧一张。播放阶段只换贴图，不再逐帧算模糊。
    private BitmapSource? _blurredImage;
    private AnimatedBackdrop? _blurredAnimation;
    private double _blurRadius;
    private int _blurVersion;

    /// <summary>拖动 / 缩放窗口期间为 true：此时关掉模糊与动图，退出后恢复。</summary>
    private bool _windowMoving;

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
            _blurredImage = null;
            _blurredAnimation = null;
            _frameIndex = 0;
            _blurVersion++;                    // 作废还在后台跑的烘焙任务
        }

        BgImage.Visibility = _backdropImage is null ? Visibility.Collapsed : Visibility.Visible;
        // 0 = absolutely clean picture; the scrims grow with the dim slider
        var dim = Math.Clamp(s.BackgroundDim, 0, 0.85);
        BgScrim.Opacity = _backdropImage is null ? 0 : dim;
        TopScrim.Opacity = _backdropImage is null ? 0 : Math.Min(0.55, dim * 1.25);
        LeftScrim.Opacity = _backdropImage is null ? 0 : Math.Min(0.50, dim * 1.15);
        ApplyBackdropBlur(_backdropImage is null ? 0 : s.BackgroundBlur);
        ApplyWindowTransparency();
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

    /// <summary>当前该显示的静态背景帧：烘焙好的模糊版优先。</summary>
    private BitmapSource? CurrentBackdropSource()
    {
        if (_windowMoving)
            return _animation is { Frames.Count: > 0 } ? _animation.Frames[0].Frame : _backdropImage;

        if (_blurredImage is not null) return _blurredImage;
        if (_animation is { Frames.Count: > 0 } raw)
        {
            _frameIndex %= raw.Frames.Count;
            return raw.Frames[_frameIndex].Frame;
        }
        return _backdropImage;
    }

    /// <summary>把当前该显示的背景丢给 Image（不做任何渲染期特效）。</summary>
    private void ApplyBackdropSource()
    {
        BgImage.Source = CurrentBackdropSource();
    }

    /// <summary>Starts / stops the GIF player depending on the setting.</summary>
    private void RefreshAnimation()
    {
        _animTimer.Stop();
        ApplyBackdropSource();

        if (_animation is null || _animation.Frames.Count < 2) return;
        // 滑块刚动过：烘焙还没回来，先停一帧，等结果到了再开始播
        if (_blurTimer.IsEnabled) return;
        if (!_store.Db.Settings.BackdropAnimation) return;

        var play = _blurredAnimation ?? _animation;
        _frameIndex %= play.Frames.Count;
        BgImage.Source = play.Frames[_frameIndex].Frame;
        _animTimer.Interval = play.Frames[_frameIndex].Delay;
        _animTimer.Start();
    }

    private void AnimationTick()
    {
        if (_animation is null || _animation.Frames.Count < 2) return;
        if (!IsVisible || WindowState == WindowState.Minimized) return;
        if (_windowMoving) return;

        var play = _blurredAnimation ?? _animation;
        _frameIndex = (_frameIndex + 1) % play.Frames.Count;
        var (frame, delay) = play.Frames[_frameIndex];
        BgImage.Source = frame;
        _animTimer.Interval = delay;
    }

    /// <summary>
    /// 背景模糊。以前是 XAML 里挂着的实时 <see cref="System.Windows.Media.Effects.BlurEffect"/>：
    /// 分层窗口没有 GPU 合成，每一帧都要 CPU 重算整窗高斯模糊，拖窗口 / 滚动 / 播动图必然掉帧。
    /// 现在改成滑块停下 120ms 后在后台烤一次位图，播放阶段只剩一次贴图。
    /// </summary>
    private void ApplyBackdropBlur(double radius)
    {
        _blurRadius = Math.Clamp(radius, 0, 60);

        if (_backdropImage is null || _blurRadius < 0.6)
        {
            _blurVersion++;
            _blurredImage = null;
            _blurredAnimation = null;
            _blurTimer.Stop();
            RefreshAnimation();
            return;
        }

        _blurTimer.Stop();
        _blurTimer.Start();
        RefreshAnimation();     // 先用现有的图顶住，拖动滑块时不会白屏
    }

    /// <summary>后台把「原图 + 模糊半径」烤成位图，回来时如果半径又变了就丢弃。</summary>
    private void BakeBackdropBlur()
    {
        _blurTimer.Stop();

        var still = _backdropImage;
        if (still is null) return;

        var version = ++_blurVersion;
        var radius = _blurRadius;
        var frames = _animation;
        var dispatcher = Dispatcher;
        var screenWidth = Math.Max(320, ActualWidth);   // 同一半径在不同窗口宽度下的观感才一致

        Task.Run(() =>
        {
            var bakedStill = BackgroundService.BakeBlur(still, radius, screenWidth);
            AnimatedBackdrop? bakedFrames = null;

            if (frames is not null && frames.Frames.Count > 1 && frames.Frames.Count <= MaxBlurAnimationFrames)
            {
                var list = new List<(BitmapSource Frame, TimeSpan Delay)>(frames.Frames.Count);
                foreach (var (frame, delay) in frames.Frames)
                {
                    if (version != _blurVersion) return;    // 又拖动了：别再烧 CPU
                    // 动图只烤 640 宽：帧一多，烘焙结果的内存和 CPU 都要压住
                    list.Add((BackgroundService.BakeBlur(frame, radius, screenWidth, 640) ?? frame, delay));
                }
                bakedFrames = new AnimatedBackdrop(list, frames.Truncated);
            }

            dispatcher.BeginInvoke(new Action(() =>
            {
                if (version != _blurVersion) return;
                _blurredImage = bakedStill;
                _blurredAnimation = bakedFrames;
                RefreshAnimation();
            }));
        });
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
        _blurTimer.Tick += (_, _) => BakeBackdropBlur();
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
