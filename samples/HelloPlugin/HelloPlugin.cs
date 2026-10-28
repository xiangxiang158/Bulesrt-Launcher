using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Bulesrt.Sdk;

namespace HelloPlugin;

/// <summary>
/// A tiny demo plugin: it logs every launch, adds an item-menu entry, contributes one command
/// (reachable from Ctrl+K and 插件命令), and adds a sidebar page.
/// Copy the built dll + plugin.json into <c>plugins\hello\</c> and restart the launcher.
/// </summary>
public sealed class HelloPlugin : IPlugin
{
    private IPluginHost? _host;

    public void Initialize(IPluginHost host)
    {
        _host = host;
        host.Log($"HelloPlugin 已加载（启动器 {host.Version}，{host.Items.Count} 个项目）");

        host.RegisterItemAction(new CopyNameAction());
        host.RegisterCommand(new PingCommand());
        host.RegisterPage(new StatsPage());
        host.Launching += OnLaunching;
        host.Toast("HelloPlugin 已加载");
    }

    public void Shutdown()
    {
        if (_host is not null) _host.Launching -= OnLaunching;
        _host?.Log("HelloPlugin 已卸载");
        _host = null;
    }

    private void OnLaunching(LaunchEvent e)
    {
        _host?.Log($"准备启动 {e.Name} → {e.Target}{(e.Admin ? "（管理员）" : "")}");
        // 想拦截某次启动，把下面这行打开：
        // e.Cancel = true;
    }

    private sealed class CopyNameAction : IPluginItemAction
    {
        public string Title => "复制名字（HelloPlugin）";

        public bool AppliesTo(IItemRef item) => !string.IsNullOrWhiteSpace(item.Name);

        public void Execute(IItemRef item, IPluginHost host)
        {
            try
            {
                Clipboard.SetText(item.Name);
                host.Toast($"已复制：{item.Name}");
            }
            catch (Exception ex)
            {
                host.Toast("复制失败：" + ex.Message);
            }
        }
    }

    private sealed class PingCommand : IPluginCommand
    {
        public string Title => "HelloPlugin · 统计一下";
        public string Description => "弹一条提示，说明当前项目数量和主题（Ctrl+K 里输入 hello 也能找到）。";

        public void Execute(IPluginHost host)
        {
            host.Toast($"现在有 {host.Items.Count} 个项目，主题 {host.Theme}");
            host.Status("HelloPlugin 跑完了");
        }
    }

    /// <summary>A sidebar page: shows a few numbers and a button.</summary>
    private sealed class StatsPage : IPluginPage
    {
        public string Id => "hello-stats";
        public string Title => "Hello 面板";
        public string Icon => "\uE9D9";

        public FrameworkElement CreateView(IPluginHost host)
        {
            var panel = new StackPanel { Margin = new Thickness(4, 6, 4, 4) };

            panel.Children.Add(new TextBlock
            {
                Text = "HelloPlugin",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"启动器 {host.Version} · 当前 {host.Items.Count} 个项目 · 分类 {host.Categories.Count} 个 · 主题 {host.Theme}",
                FontSize = 12.5,
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xA5, 0xB2, 0xD2))
            });

            var open = new Button
            {
                Content = "打开数据目录",
                Margin = new Thickness(0, 18, 0, 0),
                Padding = new Thickness(16, 6, 16, 6),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            open.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(host.DataDirectory) { UseShellExecute = true }); }
                catch (Exception ex) { host.Toast("打不开：" + ex.Message); }
            };
            panel.Children.Add(open);

            var refresh = new Button
            {
                Content = "刷新这个页面",
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(16, 6, 16, 6),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var info = new TextBlock { FontSize = 12, Margin = new Thickness(0, 10, 0, 0) };
            refresh.Click += (_, _) =>
            {
                info.Text = $"刚才刷新：{DateTime.Now:HH:mm:ss}，项目 {host.Items.Count} 个";
            };
            panel.Children.Add(refresh);
            panel.Children.Add(info);

            return panel;
        }
    }
}
