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
    // ============================================================ toast
    private void Toast(string message, bool error = false)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Background = SurfaceState.Instance.GetBrush("CardBrush"),
            BorderBrush = (Brush)FindResource(error ? "DangerBrush" : "LineBrush"),
            BorderThickness = new Thickness(1),
            Opacity = 0,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 2,
                Opacity = 0.35,
                Color = Colors.Black
            }
        };
        card.Child = new TextBlock
        {
            Text = message,
            FontSize = 12.5,
            Foreground = (Brush)FindResource("TextBrush")
        };

        ToastHost.Children.Add(card);
        while (ToastHost.Children.Count > 4) ToastHost.Children.RemoveAt(0);

        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
            fade.Completed += (_, _) => ToastHost.Children.Remove(card);
            card.BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
    }


}

