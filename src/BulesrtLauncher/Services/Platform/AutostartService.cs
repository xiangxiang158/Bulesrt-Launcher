using Microsoft.Win32;

namespace BulesrtLauncher.Services.Platform;

public static class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RexeLauncher";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key is null) return;
            if (enabled)
            {
                var exe = Environment.ProcessPath ?? "";
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch
        {
            // ignored — autostart is best effort
        }
    }
}

