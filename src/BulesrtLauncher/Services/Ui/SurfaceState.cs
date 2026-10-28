using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace BulesrtLauncher.Services.Ui;

/// <summary>
/// 「玻璃」画笔的唯一持有者。
///
/// 为什么要自己持有：WPF 把 Freezable（画笔就是）放进被合并的资源字典时会**冻结**它，
/// 冻结后改 <see cref="SolidColorBrush.Color"/> 会抛 <see cref="InvalidOperationException"/>，
/// 于是「图标盒子透明」滑块看起来完全没反应。所以这几个画笔不进字典，
/// XAML 里用 <c>{ui:Glass CardBrush}</c> 直接拿这个单例里的实例，改颜色立刻生效。
///
/// 字母磁贴的淡化（TileFade）也放在这里，靠绑定刷新，同样不碰资源字典。
/// </summary>
public sealed class SurfaceState : INotifyPropertyChanged
{
    public static SurfaceState Instance { get; } = new();

    /// <summary>会随「图标盒子透明」变化的画笔。输入框不在此列，100% 透明时还得能看见。</summary>
    public static readonly string[] GlassKeys = { "PanelBrush", "CardBrush", "Card2Brush", "TileBrush" };

    private readonly Dictionary<string, SolidColorBrush> _brushes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Color> _baseColors = new(StringComparer.Ordinal);
    private double _glass;
    private double _tileFade = 1;

    /// <summary>字母磁贴的不透明度：1 = 正常，0 = 完全隐形。</summary>
    public double TileFade
    {
        get => _tileFade;
        private set
        {
            if (Math.Abs(_tileFade - value) < 0.001) return;
            _tileFade = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TileFade)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // 给 XAML 里 Style.Setter / Trigger 用的绑定口子：
    // 这类地方如果直接塞画笔对象，Style 密封时会把 Freezable 冻结，所以走 Binding 拿实例。
    public Brush CardBrush => GetBrush("CardBrush");
    public Brush Card2Brush => GetBrush("Card2Brush");
    public Brush TileBrush => GetBrush("TileBrush");

    /// <summary>拿共享画笔。主题还没加载时先给个兜底色，加载后会被改掉。</summary>
    public Brush GetBrush(string key)
    {
        if (_brushes.TryGetValue(key, out var brush)) return brush;

        var color = _baseColors.TryGetValue(key, out var known) ? known : Color.FromArgb(0xFF, 0x12, 0x1B, 0x3A);
        brush = new SolidColorBrush(color);
        _brushes[key] = brush;
        return brush;
    }

    /// <summary>从主题字典读基准色（字典里的画笔是冻结的，只读 Color 没问题），然后重新上色。</summary>
    public void LoadTheme(ResourceDictionary theme)
    {
        foreach (var key in GlassKeys)
        {
            if (theme[key] is SolidColorBrush source) _baseColors[key] = source.Color;
        }
        Repaint();
    }

    /// <summary>图标盒子/卡片的透明程度：0 = 完全不透明，1 = 方块整个消失。</summary>
    public void SetGlass(double glass)
    {
        _glass = Math.Clamp(glass, 0, 1);
        Repaint();
    }

    private void Repaint()
    {
        var alpha = (byte)Math.Round(255 * (1 - _glass));
        foreach (var key in GlassKeys)
        {
            if (!_baseColors.TryGetValue(key, out var baseColor)) continue;
            if (GetBrush(key) is not SolidColorBrush brush || brush.IsFrozen) continue;
            brush.Color = Color.FromArgb((byte)(baseColor.A * alpha / 255), baseColor.R, baseColor.G, baseColor.B);
        }

        TileFade = Math.Round(1 - _glass, 3);
    }
}
