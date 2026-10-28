using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace BulesrtSetup;

public partial class App : Application
{
    private static readonly string LogFile = Path.Combine(AppContext.BaseDirectory, "setup-error.log");

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Log(e.Exception);
        DispatcherUnhandledException += OnDispatcherException;
    }

    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        MessageBox.Show("安装程序出错：" + e.Exception.Message, "Bulesrt Launcher 安装程序",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void Log(Exception? ex)
    {
        try
        {
            File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss} {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // logging must never throw
        }
    }
}

