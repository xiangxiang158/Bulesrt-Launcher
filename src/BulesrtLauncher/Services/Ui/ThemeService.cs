using System;
using System.Windows;

namespace BulesrtLauncher.Services.Ui;

/// <summary>Swaps the merged colour dictionary at runtime (dark / light) and applies glassiness.</summary>
public static class ThemeService
{
    private static string _theme = "dark";
    private static double _surface;

    public static string Current => _theme;

    public static void Apply(string theme, double surfaceOpacity = -1)
    {
        var name = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        _theme = name.ToLowerInvariant();
        if (surfaceOpacity >= 0) _surface = Math.Clamp(surfaceOpacity, 0, 1);

        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{name}.xaml", UriKind.Absolute)
        };

        var app = Application.Current;
        if (app is not null)
        {
            var merged = app.Resources.MergedDictionaries;
            if (merged.Count == 0) merged.Add(dict);
            else merged[0] = dict;
        }

        // 玻璃画笔由 SurfaceState 自己持有（字典里的会被 WPF 冻结，改不了颜色）
        SurfaceState.Instance.LoadTheme(dict);
        SurfaceState.Instance.SetGlass(_surface);
    }

    /// <summary>Only the glass value changed — repaint the shared brushes, never rebuild the dictionary.</summary>
    public static void SetSurface(double surfaceOpacity)
    {
        surfaceOpacity = Math.Clamp(surfaceOpacity, 0, 1);
        if (Math.Abs(surfaceOpacity - _surface) < 0.001) return;
        _surface = surfaceOpacity;
        SurfaceState.Instance.SetGlass(_surface);
    }
}
