using System.Runtime.InteropServices;

namespace BulesrtLauncher.Services.Launching;

/// <summary>
/// Thin wrapper around ShellExecuteExW — the same call Explorer makes when you double-click.
/// .NET's <see cref="System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo)"/>
/// does not reliably forward the working directory to the shell, so we call the API directly.
/// </summary>
public static class ShellLauncher
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteExW(ref SHELLEXECUTEINFO info);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;
    private const uint SEE_MASK_FLAG_NO_UI = 0x00000400;
    private const int SW_SHOWNORMAL = 1;

    public static (bool Ok, int Error) Execute(string file, string? arguments, string? workingDirectory, string? verb)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI,
            hwnd = IntPtr.Zero,
            lpVerb = string.IsNullOrWhiteSpace(verb) ? null : verb,
            lpFile = file,
            lpParameters = string.IsNullOrWhiteSpace(arguments) ? null : arguments,
            lpDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
            nShow = SW_SHOWNORMAL
        };

        var ok = ShellExecuteExW(ref info);
        var error = ok ? 0 : Marshal.GetLastWin32Error();
        if (info.hProcess != IntPtr.Zero)
            CloseHandle(info.hProcess);
        return (ok, error);
    }
}

