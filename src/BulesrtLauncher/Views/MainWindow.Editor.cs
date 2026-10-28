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
    // ============================================================ editor
    private void BtnAdd_Click(object sender, RoutedEventArgs e) => OpenEditor(null);

    private void OpenEditor(AppItem? item)
    {
        _editing = item;
        EditorTitle.Text = item is null ? "添加项目" : "编辑项目";
        EdName.Text = item?.Name ?? "";
        EdPath.Text = item?.Target ?? "";
        EdArgs.Text = item?.Arguments ?? "";
        EdWorkDir.Text = item?.WorkingDirectory ?? "";
        _editorCategory = item?.CategoryId ?? (_current is "home" or "all" ? "misc" : _current);
        BuildCategoryChips(EdCategories, _editorCategory, id => _editorCategory = id);
        ShowPanel(PanelEditor, 700);
        EdName.Focus();
    }

    private void BuildCategoryChips(Panel host, string selected, Action<string> onSelect)
    {
        host.Children.Clear();
        var buttons = new List<RadioButton>();
        foreach (var cat in _store.Db.Categories)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Chip"),
                Content = cat.Name,
                GroupName = $"cate-{host.Name}",
                Margin = new Thickness(0, 0, 8, 8),
                IsChecked = cat.Id == selected,
                Tag = cat.Id
            };
            chip.Checked += (_, _) => onSelect((string)chip.Tag);
            host.Children.Add(chip);
            buttons.Add(chip);
        }
    }

    private void EdBrowse_Click(object sender, RoutedEventArgs e)
    {
        LaunchService.OpenFilePicker(this, false, files =>
        {
            if (files.Length == 0) return;
            var path = files[0];
            EdPath.Text = path;
            if (string.IsNullOrWhiteSpace(EdName.Text))
                EdName.Text = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(EdWorkDir.Text))
                EdWorkDir.Text = Path.GetDirectoryName(path) ?? "";
        });
    }

    private void EdBrowseDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择工作目录" };
        if (dialog.ShowDialog(this) == true)
            EdWorkDir.Text = dialog.FolderName;
    }

    private void EdSave_Click(object sender, RoutedEventArgs e)
    {
        var path = EdPath.Text.Trim();
        if (path.Length == 0)
        {
            MessageBox.Show(this, "请填写目标路径", "Bulesrt Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var name = EdName.Text.Trim();
        if (name.Length == 0) name = Path.GetFileNameWithoutExtension(path);
        if (_editing is null)
        {
            AddDrafts(new[] { new ItemDraft(name, path, _editorCategory, EdArgs.Text.Trim(), EdWorkDir.Text.Trim()) },
                () => CloseModal());
            return;
        }
        _editing.Name = name;
        _editing.Target = path;
        _editing.Arguments = EdArgs.Text.Trim();
        _editing.WorkingDirectory = EdWorkDir.Text.Trim();
        _editing.CategoryId = _editorCategory;
        _editing.Icon = null;
        _store.Save();
        CloseModal();
        RefreshList();
        LoadIcons();
        Status("已保存"); Toast("已保存");
    }


    // ============================================================ batch add
    private void BtnBatch_Click(object sender, RoutedEventArgs e)
    {
        BatchText.Text = "";
        _batchCategory = _current is "home" or "all" ? "misc" : _current;
        BuildCategoryChips(BatchCategories, _batchCategory, id => _batchCategory = id);
        ShowPanel(PanelBatch, 620);
    }

    private void BatchBrowse_Click(object sender, MouseButtonEventArgs e)
    {
        LaunchService.OpenFilePicker(this, true, files =>
        {
            if (files.Length == 0) return;
            BatchText.Text = (BatchText.Text.Length > 0 ? BatchText.Text.TrimEnd() + Environment.NewLine : "")
                             + string.Join(Environment.NewLine, files);
        });
    }

    private void BatchOk_Click(object sender, RoutedEventArgs e)
    {
        var lines = BatchText.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().Trim('"'))
            .Where(l => l.Length > 0)
            .ToList();
        if (lines.Count == 0)
        {
            MessageBox.Show(this, "请先填写路径", "Bulesrt Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var drafts = lines
            .Select(line => new ItemDraft(Path.GetFileNameWithoutExtension(line.TrimEnd('\\', '/')), line, _batchCategory, "", ""))
            .ToList();
        AddDrafts(drafts, () => CloseModal());
    }


}


