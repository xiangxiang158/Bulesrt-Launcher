using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BulesrtLauncher.Services.Ui;

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>Stable, theme-friendly gradient for letter tiles (name -> colour).</summary>
public sealed class LetterBrushConverter : IValueConverter
{
    private static readonly (string A, string B)[] Palettes =
    {
        ("#5A79E8", "#16265C"),
        ("#7C93F0", "#1B2748"),
        ("#4C6BD6", "#131C3C"),
        ("#6FB3E8", "#102A45"),
        ("#8FA6F2", "#1D2A55"),
        ("#3F7FD8", "#101E3E"),
        ("#A8BCF5", "#232C50"),
        ("#5E7FA8", "#141E33"),
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? "";
        var hash = 17;
        foreach (var ch in text) hash = hash * 31 + ch & 0x7FFFFFFF;
        var (a, b) = Palettes[hash % Palettes.Length];
        return new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(a),
            (Color)ColorConverter.ConvertFromString(b),
            new Point(0, 0), new Point(1, 1));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


