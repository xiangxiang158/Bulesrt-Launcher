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
    // ============================================================ search
    private sealed class SearchRow
    {
        public required AppItem Item { get; init; }
        public required string CategoryName { get; init; }

        /// <summary>Set when this row is a plugin command rather than library item.</summary>
        public Bulesrt.Sdk.IPluginCommand? Command { get; init; }
    }

    private void BtnSearch_Click(object sender, RoutedEventArgs e) => OpenSearch();

    private void OpenSearch()
    {
        SearchLayer.Visibility = Visibility.Visible;
        SearchBox.Text = "";
        RefreshSearch();
        SearchBox.Focus();
    }

    private void CloseSearch()
    {
        SearchLayer.Visibility = Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshSearch();

    private void RefreshSearch()
    {
        var q = SearchBox.Text.Trim().ToLowerInvariant();
        var rows = _store.Db.Items
            .Select(item => (item, score: Score(item, q)))
            .Where(pair => pair.score > 0)
            .OrderByDescending(pair => pair.score)
            .ThenByDescending(pair => pair.item.RunCount)
            .Take(40)
            .Select(pair => new SearchRow { Item = pair.item, CategoryName = CategoryName(pair.item.CategoryId) })
            .ToList();

        // plugin commands share the same list; "/" or any keyword can reach them
        if (q.Length > 0)
        {
            foreach (var command in Services.Plugins.PluginService.Commands)
            {
                var title = (command.Title ?? "").ToLowerInvariant();
                var description = (command.Description ?? "").ToLowerInvariant();
                if (!title.Contains(q) && !description.Contains(q)) continue;

                rows.Insert(Math.Min(rows.Count, 0), new SearchRow
                {
                    Item = new AppItem { Name = command.Title ?? "", Target = command.Description ?? "" },
                    CategoryName = "插件命令",
                    Command = command
                });
            }
        }

        SearchList.ItemsSource = rows;
        if (rows.Count > 0) SearchList.SelectedIndex = 0;
    }

    private static int Score(AppItem item, string q)
    {
        if (q.Length == 0) return 1;
        var name = item.Name.ToLowerInvariant();
        var target = item.Target.ToLowerInvariant();
        if (name.StartsWith(q)) return 100;
        if (name.Contains(q)) return 60;
        if (target.Contains(q)) return 30;
        var i = 0;
        foreach (var ch in name)
        {
            if (ch == q[i]) i++;
            if (i >= q.Length) return 15;
        }
        return 0;
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseSearch();
                e.Handled = true;
                break;
            case Key.Down:
                if (SearchList.Items.Count > 0)
                    SearchList.SelectedIndex = Math.Min(SearchList.SelectedIndex + 1, SearchList.Items.Count - 1);
                e.Handled = true;
                break;
            case Key.Up:
                if (SearchList.Items.Count > 0)
                    SearchList.SelectedIndex = Math.Max(SearchList.SelectedIndex - 1, 0);
                e.Handled = true;
                break;
            case Key.Enter:
                LaunchSelectedSearch();
                e.Handled = true;
                break;
        }
    }

    private void SearchList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LaunchSelectedSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseSearch();
            e.Handled = true;
        }
    }

    private void SearchList_DoubleClick(object sender, MouseButtonEventArgs e) => LaunchSelectedSearch();

    private void LaunchSelectedSearch()
    {
        if (SearchList.SelectedItem is not SearchRow row) return;

        if (row.Command is not null)
        {
            CloseSearch();
            try
            {
                row.Command.Execute(_pluginHost!);
                Status($"插件命令 · {row.Command.Title}");
            }
            catch (Exception ex)
            {
                Toast("插件命令失败：" + ex.Message, true);
            }
            return;
        }

        CloseSearch();
        Launch(row.Item, false);
    }

    private void SearchLayer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == SearchLayer) CloseSearch();
    }


}

