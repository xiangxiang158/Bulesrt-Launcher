using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace BulesrtSetup;

public partial class InstallWindow : Window
{
    private CancellationTokenSource? _cancel;
    private bool _createShortcuts = true;
    private bool _launchAfterInstall = true;
    private bool _offline;
    private bool _fresh;
    private bool _uninstall;
    private bool _quiet;

#if FRESH_DEFAULT
    private const bool FreshByDefault = true;
#else
    private const bool FreshByDefault = false;
#endif

    public InstallWindow()
    {
        InitializeComponent();
        DirBox.Text = Installer.DefaultDirectory;

        var bundled = Installer.ReadBundledUrl();
        UrlBox.Text = bundled ?? "";
        _offline = Installer.HasPayload() && string.IsNullOrWhiteSpace(bundled);

        var args = Environment.GetCommandLineArgs();
        var auto = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--url":
                case "-u":
                    if (i + 1 < args.Length) { UrlBox.Text = args[++i]; _offline = false; }
                    break;
                case "--dir":
                case "-d":
                    if (i + 1 < args.Length) DirBox.Text = args[++i];
                    break;
                case "--no-shortcuts":
                    _createShortcuts = false;
                    break;
                case "--no-launch":
                    _launchAfterInstall = false;
                    break;
                case "--auto":
                    auto = true;
                    break;
                case "--fresh":
                    _fresh = true;
                    break;
                case "--keep-data":
                    _fresh = false;
                    break;
                case "--uninstall":
                    _uninstall = true;
                    break;
                case "--quiet":
                    _quiet = true;
                    break;
            }
        }

        ApplyMode();
        _fresh = FreshByDefault || _fresh;
        FreshBox.IsChecked = _fresh;
        FreshHint.Visibility = _fresh ? Visibility.Visible : Visibility.Collapsed;
        FreshBox.Checked += (_, _) => { _fresh = true; FreshHint.Visibility = Visibility.Visible; };
        FreshBox.Unchecked += (_, _) => { _fresh = false; FreshHint.Visibility = Visibility.Collapsed; };

        if (!_offline && !_uninstall && string.IsNullOrWhiteSpace(UrlBox.Text))
            StatusText.Text = "请先填写安装包地址（或把 setup.json 放到本程序同目录）";

        // 已经装过的话，页脚给一个「卸载已安装的版本…」入口
        UninstallLink.Visibility = !_uninstall && Installer.IsInstalled(Installer.DefaultDirectory)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_uninstall && _quiet) StartUninstallAutomatically();
        else if (auto) StartAutomatically();
    }

    /// <summary>页脚按钮：切到卸载模式（目录预填默认安装位置）。</summary>
    private void UninstallLink_Click(object sender, RoutedEventArgs e)
    {
        _uninstall = true;
        DirBox.Text = Installer.DefaultDirectory;
        ApplyMode();
        UninstallLink.Visibility = Visibility.Collapsed;
    }

    private void ApplyMode()
    {
        if (_uninstall)
        {
            UrlSection.Visibility = Visibility.Collapsed;
            OfflineSection.Visibility = Visibility.Collapsed;
            FreshBox.Visibility = Visibility.Collapsed;
            FreshHint.Visibility = Visibility.Collapsed;
            UninstallSection.Visibility = Visibility.Visible;
            UninstallLink.Visibility = Visibility.Collapsed;
            InstallButton.Content = "卸载";
            Title = "卸载 Bulesrt Launcher";
            HeaderHint.Text = "会删掉程序、快捷方式和「应用和功能」里的条目";

            var version = Installer.InstalledVersion(DirBox.Text);
            VersionText.Text = version is null ? "" : $"已安装 v{version}";
            StatusText.Text = Installer.IsInstalled(DirBox.Text)
                ? "准备就绪 · 点「卸载」开始"
                : "默认目录里没找到已安装的启动器；如果装在别处，先点「浏览…」选一下";
            return;
        }

        UrlSection.Visibility = _offline ? Visibility.Collapsed : Visibility.Visible;
        OfflineSection.Visibility = _offline ? Visibility.Visible : Visibility.Collapsed;
        var fresh = _fresh || FreshByDefault;
        InstallButton.Content = _offline ? (fresh ? "全新安装（清空项目）" : "安装空项目版") : "开始安装";
        Title = _offline
            ? (fresh ? "安装 Bulesrt Launcher · 全新安装" : "安装 Bulesrt Launcher · 离线空项目版")
            : "安装 Bulesrt Launcher";
        HeaderHint.Text = _offline
            ? (fresh ? "内置安装包 · 全新安装 · 旧数据会先备份" : "内置安装包 · 无需联网 · 装完是空项目库")
            : "下载最新版本并安装到本机";
        if (_offline) StatusText.Text = "准备就绪 · 内置安装包，不联网";
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch { }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _cancel?.Cancel();
        Close();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择安装位置" };
        if (dialog.ShowDialog(this) != true) return;

        var picked = dialog.FolderName;
        var suggested = Installer.SuggestPath(picked);
        if (suggested is not null)
        {
            picked = suggested;
            StatusText.Text = "桌面/文档/下载根目录不适合直接装，已换成子文件夹：" + picked;
        }
        DirBox.Text = picked;
    }

    private void DefaultDir_Click(object sender, RoutedEventArgs e)
    {
        DirBox.Text = Installer.DefaultDirectory;
        StatusText.Text = "已恢复到默认位置";
    }

    /// <summary>Starts an install without UI interaction (used by --auto).</summary>
    public void StartAutomatically()
    {
        Loaded += async (_, _) =>
        {
            await Task.Delay(400);
            await RunInstallAsync();
        };
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_uninstall) await RunUninstallAsync();
        else await RunInstallAsync();
    }

    /// <summary>Starts an uninstall without UI interaction (used by --uninstall --quiet).</summary>
    private void StartUninstallAutomatically()
    {
        Loaded += async (_, _) =>
        {
            await Task.Delay(300);
            await RunUninstallAsync();
        };
    }

    private async Task RunUninstallAsync()
    {
        InstallButton.IsEnabled = false;
        CancelButton.Content = "关闭";
        Bar.Value = 0;
        StatusText.Text = "正在准备…";

        var progress = new Progress<(double Percent, string Status)>(p =>
        {
            Bar.Value = p.Percent;
            StatusText.Text = p.Status;
        });

        var result = await Installer.RunUninstallAsync(DirBox.Text, RemoveDataBox.IsChecked == true, progress);

        InstallButton.IsEnabled = true;
        if (!result.Ok)
        {
            StatusText.Text = result.Message;
            Bar.Value = 0;
            return;
        }

        Bar.Value = 100;
        StatusText.Text = result.Message;
        await Task.Delay(_quiet ? 400 : 1200);
        Close();
    }

    private async Task RunInstallAsync()
    {
        InstallButton.IsEnabled = false;
        CancelButton.Content = "取消";
        Bar.Value = 0;
        StatusText.Text = "正在准备…";

        _cancel = new CancellationTokenSource();
        var progress = new Progress<(double Percent, string Status)>(p =>
        {
            Bar.Value = p.Percent;
            StatusText.Text = p.Status;
        });

        var result = _offline
            ? await Installer.RunOfflineAsync(DirBox.Text, progress, _cancel.Token, _createShortcuts, FreshBox.IsChecked == true)
            : await Installer.RunAsync(UrlBox.Text, DirBox.Text, progress, _cancel.Token, _createShortcuts, FreshBox.IsChecked == true);

        InstallButton.IsEnabled = true;
        if (!result.Ok)
        {
            StatusText.Text = result.Message;
            Bar.Value = 0;
            return;
        }

        StatusText.Text = result.Message + "，正在启动…";
        Bar.Value = 100;
        try
        {
            if (_launchAfterInstall && result.InstalledExe is not null)
                Process.Start(new ProcessStartInfo(result.InstalledExe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(result.InstalledExe)! });
        }
        catch
        {
            // launching is optional
        }
        await Task.Delay(900);
        Close();
    }
}




