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
using Microsoft.Win32;
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
    private bool _suppressSettingEvents;

    // ============================================================ settings
    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var s = _store.Db.Settings;
        SetAutostart.IsChecked = s.Autostart;
        SetHide.IsChecked = s.HideAfterLaunch;
        CloseAsk.IsChecked = s.CloseAction != "tray" && s.CloseAction != "exit";
        CloseMin.IsChecked = s.CloseAction == "tray";
        CloseExit.IsChecked = s.CloseAction == "exit";
        SetSoftware.IsChecked = s.SoftwareRender;
        SetShowRunning.IsChecked = s.ShowRunning;
        ThemeDark.IsChecked = s.Theme != "light";
        ThemeLight.IsChecked = s.Theme == "light";
        ViewGrid.IsChecked = s.View != "list";
        ViewList.IsChecked = s.View == "list";
        Scale100.IsChecked = s.Scale == 100;
        Scale110.IsChecked = s.Scale == 110;
        Scale125.IsChecked = s.Scale == 125;
        SetHotkey.Text = s.Hotkey;
        SetDataDir.Text = StoreService.DataDir;
        SyncBackgroundControls();
        SyncAboutInfo();
        ShowPanel(PanelSettings, 900);
    }

    // ============================================================ about / copyright

    /// <summary>版权人。产品名与版本号取自程序集，改了 csproj 这里自动跟着变。</summary>
    private const string CopyrightOwner = "xiang";
    private const string CopyrightYear = "2026";

    private void SyncAboutInfo()
    {
        try
        {
            var asm = typeof(MainWindow).Assembly;
            var version = asm.GetName().Version;
            var product = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyProductAttribute), false)
                .OfType<System.Reflection.AssemblyProductAttribute>()
                .Select(a => a.Product)
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "Bulesrt Launcher";

            AboutProduct.Text = product;
            AboutVersion.Text = version is null
                ? "版本 2.0.0"
                : $"版本 {version.Major}.{version.Minor}.{version.Build}";
            AboutCopyright.Text = $"© {CopyrightYear} {CopyrightOwner} · 版权人 {CopyrightOwner} · 保留所有权利";
            AboutCopyright.ToolTip = $"{product} 的版权归 {CopyrightOwner} 所有";
        }
        catch
        {
            // 关于信息只是展示，取不到就保留 XAML 里的默认文字
        }
    }

    private void SetHotkey_GotFocus(object sender, RoutedEventArgs e)
    {
        SetHotkey.SelectAll();
    }

    private void SetHotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (e.Key == Key.Back || e.Key == Key.Delete)
        {
            SetHotkey.Text = "";
            return;
        }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var parts = new List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(key == Key.Space ? "Space" : key.ToString());
        SetHotkey.Text = string.Join("+", parts);
    }

    private void SettingsApply_Click(object sender, RoutedEventArgs e) => ApplySettings(false);

    private void SettingsOk_Click(object sender, RoutedEventArgs e) => ApplySettings(true);

    private void ApplySettings(bool close)
    {
        var s = _store.Db.Settings;
        s.Autostart = SetAutostart.IsChecked == true;
        s.HideAfterLaunch = SetHide.IsChecked == true;
        s.CloseAction = CloseMin.IsChecked == true ? "tray" : CloseExit.IsChecked == true ? "exit" : "ask";
        var renderChanged = s.SoftwareRender != (SetSoftware.IsChecked == true);
        s.SoftwareRender = SetSoftware.IsChecked == true;
        s.ShowRunning = SetShowRunning.IsChecked == true;
        s.Theme = ThemeLight.IsChecked == true ? "light" : "dark";
        s.View = ViewList.IsChecked == true ? "list" : "grid";
        s.Scale = Scale125.IsChecked == true ? 125 : Scale110.IsChecked == true ? 110 : 100;
        s.Hotkey = string.IsNullOrWhiteSpace(SetHotkey.Text) ? "Ctrl+Alt+Space" : SetHotkey.Text.Trim();
        s.BackgroundBlur = Math.Clamp(SetBgBlur.Value, 0, 60);
        s.BackgroundDim = Math.Clamp(SetBgDim.Value, 0, 85) / 100.0;
        s.SurfaceOpacity = Math.Clamp(SetSurface.Value, 0, 100) / 100.0;
        s.WindowTransparency = Math.Clamp(SetWindowTrans.Value, 0, 100) / 100.0;
        s.BackdropAnimation = SetAnimation.IsChecked == true;
        _store.Save();

        ThemeService.Apply(s.Theme, s.SurfaceOpacity);
        ApplyAcrylicTint();
        ApplyBackdrop();
        ApplyView();
        ApplyScale(s.Scale);
        AutostartService.Apply(s.Autostart);
        RegisterHotkey();
        Status(renderChanged ? "设置已保存（渲染模式重启后生效）" : "设置已保存");
        if (close) CloseModal();
    }

    // ============================================================ backdrop settings
    private void SyncBackgroundControls()
    {
        var s = _store.Db.Settings;
        _suppressSettingEvents = true;
        SetBgBlur.Value = Math.Clamp(s.BackgroundBlur, 0, 60);
        SetBgDim.Value = Math.Clamp(s.BackgroundDim * 100, 0, 85);
        SetSurface.Value = Math.Clamp(s.SurfaceOpacity * 100, 0, 100);
        SetWindowTrans.Value = Math.Clamp(s.WindowTransparency * 100, 0, 100);
        SetAnimation.IsChecked = s.BackdropAnimation;
        _suppressSettingEvents = false;
        UpdateBackgroundLabels();
    }

    private void UpdateBackgroundLabels()
    {
        if (BgName is null) return;
        var s = _store.Db.Settings;
        var name = s.BackgroundImage;
        if (string.IsNullOrWhiteSpace(name))
        {
            BgName.Text = "未设置 · 使用内置深蓝背景";
        }
        else if (BackgroundService.Resolve(name) is null)
        {
            BgName.Text = "背景图缺失：" + name;
        }
        else
        {
            var extra = BackgroundService.IsAnimated(name)
                ? $" · 动图 {(_animation?.Frames.Count ?? 1)} 帧" + (_animation?.Truncated == true ? "（已截断）" : "")
                : "";
            BgName.Text = name + extra;
        }

        BgBlurVal.Text = ((int)Math.Round(s.BackgroundBlur)).ToString();
        BgDimVal.Text = (int)Math.Round(s.BackgroundDim * 100) + "%";
        SurfaceVal.Text = (int)Math.Round(s.SurfaceOpacity * 100) + "%";
        WindowTransVal.Text = (int)Math.Round(s.WindowTransparency * 100) + "%";
    }

    private void PickBackground_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择背景图片",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var name = BackgroundService.Import(dialog.FileName);
            var s = _store.Db.Settings;
            s.BackgroundImage = name;
            if (s.BackgroundBlur < 1) s.BackgroundBlur = 12;
            if (s.BackgroundDim < 0.05) s.BackgroundDim = 0.3;
            if (s.SurfaceOpacity < 0.05) s.SurfaceOpacity = 0.35;
            _store.Save();
            ApplyBackdrop(true);
            SyncBackgroundControls();
            ThemeService.SetSurface(s.SurfaceOpacity);
            Status("背景图已设置：" + name);
        }
        catch (Exception ex)
        {
            Toast("背景图设置失败：" + ex.Message);
        }
    }

    private void ClearBackground_Click(object sender, RoutedEventArgs e)
    {
        BackgroundService.Clear();
        var s = _store.Db.Settings;
        s.BackgroundImage = "";
        s.BackdropChosen = true;      // an explicit choice — do not re-apply the default artwork
        _store.Save();
        ApplyBackdrop(true);
        SyncBackgroundControls();
        Status("已恢复内置深蓝背景");
    }

    private void UseArtwork_Click(object sender, RoutedEventArgs e)
    {
        var name = BackgroundService.ImportArtwork();
        if (name is null)
        {
            Toast("内置插画不可用");
            return;
        }

        var s = _store.Db.Settings;
        s.BackgroundImage = name;
        s.BackdropChosen = true;
        s.BackgroundBlur = 26;
        s.BackgroundDim = 0.5;
        s.SurfaceOpacity = 0.4;
        _store.Save();
        ApplyBackdrop(true);
        SyncBackgroundControls();
        ThemeService.SetSurface(s.SurfaceOpacity);
        Status("已使用插画作为背景（可继续调整模糊 / 压暗 / 透明）");
    }

    /// <summary>One click reproduces the look from the docs: artwork wallpaper + fully transparent icon boxes.</summary>
    private void PresetGlass_Click(object sender, RoutedEventArgs e)
    {
        var name = BackgroundService.ImportArtwork();
        if (name is null)
        {
            Toast("内置插画不可用");
            return;
        }

        var s = _store.Db.Settings;
        s.BackgroundImage = name;
        s.BackdropChosen = true;
        s.BackgroundBlur = 18;
        s.BackgroundDim = 0.3;
        s.SurfaceOpacity = 1.0;
        s.WindowTransparency = 0;
        s.BackdropAnimation = true;
        s.Theme = "dark";                 // the artwork is dark, the dark shell keeps text readable
        _store.Save();

        ThemeService.Apply(s.Theme, s.SurfaceOpacity);
        ApplyAcrylicTint();
        ApplyBackdrop(true);
        SyncBackgroundControls();
        ThemeDark.IsChecked = true;
        ThemeLight.IsChecked = false;
        Status("已套用预设：插画壁纸 + 透明盒子");
    }

    private void SetShowRunning_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingEvents) return;
        _store.Db.Settings.ShowRunning = SetShowRunning.IsChecked == true;
        _store.Save();
        RefreshRunning();
    }

    private void BgBlur_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSettingEvents) return;
        _store.Db.Settings.BackgroundBlur = e.NewValue;
        ApplyBackdropBlur(e.NewValue);
        BgBlurVal.Text = ((int)Math.Round(e.NewValue)).ToString();
        ScheduleBackdropSave();
    }

    private void BgDim_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSettingEvents) return;
        var dim = Math.Clamp(e.NewValue, 0, 100) / 100.0;
        _store.Db.Settings.BackgroundDim = dim;
        BgScrim.Opacity = BgImage.Source is null ? 0 : dim;
        TopScrim.Opacity = BgImage.Source is null ? 0 : Math.Min(0.55, dim * 1.25);
        LeftScrim.Opacity = BgImage.Source is null ? 0 : Math.Min(0.50, dim * 1.15);
        BgDimVal.Text = (int)Math.Round(e.NewValue) + "%";
        ScheduleBackdropSave();
    }

    private void Surface_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSettingEvents) return;
        SurfaceVal.Text = (int)Math.Round(e.NewValue) + "%";
        ScheduleSurfaceApply(e.NewValue);
        ScheduleBackdropSave();
    }

    private void WindowTrans_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSettingEvents) return;
        _store.Db.Settings.WindowTransparency = Math.Clamp(e.NewValue, 0, 100) / 100.0;
        ApplyWindowTransparency();
        WindowTransVal.Text = (int)Math.Round(e.NewValue) + "%";
        ScheduleBackdropSave();
    }

    private void SetAnimation_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingEvents) return;
        _store.Db.Settings.BackdropAnimation = SetAnimation.IsChecked == true;
        RefreshAnimation();
        ScheduleBackdropSave();
    }

    private void SetDataDirLink_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = StoreService.DataDir,
                UseShellExecute = true
            });
        }
        catch { /* ignored */ }
    }

    private async void Reseed_Click(object sender, MouseButtonEventArgs e)
    {
        CloseModal();
        var existing = _store.Db.Items.Select(i => i.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = Importer.Collect(80).Where(r => !existing.Contains(r.Path)).ToList();
        foreach (var row in rows)
            _store.AddItem(row.Name, row.Path, row.Category);
        RefreshList();
        await LoadIconsAsync();
        Status($"已导入 {rows.Count} 个项目"); Toast($"已导入 {rows.Count} 个项目");
    }


    private void PluginsPanel_Click(object sender, MouseButtonEventArgs e) => ShowPluginsWindow();

    private void PluginCommands_Click(object sender, MouseButtonEventArgs e) => ShowPluginCommands();

    private void PluginFolder_Click(object sender, MouseButtonEventArgs e) => OpenPluginsFolder();
}
