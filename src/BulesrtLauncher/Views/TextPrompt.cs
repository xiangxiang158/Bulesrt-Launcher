using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BulesrtLauncher.Services.Ui;

namespace BulesrtLauncher.Views;

/// <summary>Minimal themed text prompt (used for “新建分类”).</summary>
public sealed class TextPrompt : Window
{
    private readonly TextBox _box;

    public string Value => _box.Text.Trim();

    public TextPrompt(string title, string label, string initial)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent;
        AllowsTransparency = true;

        var panel = new Border
        {
            Background = SurfaceState.Instance.GetBrush("PanelBrush"),
            BorderBrush = Brush("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush")
        });
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            Margin = new Thickness(0, 14, 0, 8),
            Foreground = Brush("TextBrush")
        });
        _box = new TextBox
        {
            Text = initial,
            Style = (Style)Application.Current.FindResource("InputBox"),
            Height = 36
        };
        stack.Children.Add(_box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var cancel = new Button { Content = "取消", Style = (Style)Application.Current.FindResource("GhostButton") };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var ok = new Button
        {
            Content = "确定",
            Style = (Style)Application.Current.FindResource("PrimaryButton"),
            Margin = new Thickness(10, 0, 0, 0),
            IsDefault = true
        };
        ok.Click += (_, _) => { DialogResult = true; Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);

        panel.Child = stack;
        Content = panel;
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        };
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}

