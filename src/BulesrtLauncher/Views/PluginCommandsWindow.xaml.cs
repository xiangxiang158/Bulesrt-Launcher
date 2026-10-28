using System;
using System.Windows;
using System.Windows.Input;
using Bulesrt.Sdk;
using BulesrtLauncher.Services.Plugins;

namespace BulesrtLauncher.Views;

public partial class PluginCommandsWindow : Window
{
    private readonly IPluginHost _host;

    public PluginCommandsWindow(IPluginHost host)
    {
        InitializeComponent();
        _host = host;
        CommandList.ItemsSource = PluginService.Commands;
        HintText.Text = PluginService.Commands.Count == 0 ? "还没有插件提供命令。" : $"{PluginService.Commands.Count} 条命令";
    }

    private void CommandList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => RunSelected();

    private void Run_Click(object sender, RoutedEventArgs e) => RunSelected();

    private void RunSelected()
    {
        if (CommandList.SelectedItem is not IPluginCommand command) return;
        try
        {
            command.Execute(_host);
            HintText.Text = $"已运行：{command.Title}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "命令执行失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
