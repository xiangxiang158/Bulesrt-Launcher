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
    // ============================================================ misc
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // overlays / dialogs own the keyboard first
        if (SearchLayer.Visibility == Visibility.Visible || ModalLayer.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                if (SearchLayer.Visibility == Visibility.Visible) CloseSearch();
                else CloseModal();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            OpenSearch();
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is TextBox or System.Windows.Controls.Primitives.ButtonBase) return;

        // Ctrl+1..9 switches category
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key is >= Key.D1 and <= Key.D9)
        {
            var index = e.Key - Key.D1;
            if (index < NavList.Items.Count)
            {
                NavList.SelectedIndex = index;
                e.Handled = true;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                MoveSelection(-1, 0);
                e.Handled = true;
                break;
            case Key.Right:
                MoveSelection(1, 0);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(0, -1);
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(0, 1);
                e.Handled = true;
                break;
            case Key.Home:
                MoveSelectionEdge(false);
                e.Handled = true;
                break;
            case Key.End:
                MoveSelectionEdge(true);
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Space:
                if (_selected is not null)
                {
                    LaunchSelected();
                    e.Handled = true;
                }
                break;
            case Key.Delete:
                if (_selected is { } toDelete)
                {
                    var target = toDelete;
                    Confirm("删除项目", $"确定删除「{target.Name}」？", () =>
                    {
                        _store.DeleteItems(new[] { target.Id });
                        _selected = null;
                        RefreshList();
                        Toast($"已删除 · {target.Name}");
                    });
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                if (_selected is not null)
                {
                    SelectItem(null);
                    e.Handled = true;
                }
                break;
            case Key.F10 when (Keyboard.Modifiers & ModifierKeys.Shift) != 0:
                if (_selected is not null)
                {
                    var menu = BuildItemMenu(_selected);
                    menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                    menu.IsOpen = true;
                    e.Handled = true;
                }
                break;
        }
    }


}

