using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace BulesrtLauncher.Services.Platform;

/// <summary>
/// 读写当前进程的完整性级别。启动器如果是从低权限父进程（沙箱、部分远程桌面代理、
/// 某些安装器）里拉起来的，自己会继承 Low，之后启动的任何程序也都是 Low ——
/// 卸载器、系统工具这类需要读 HKLM 的程序就会崩。这里用于检测并提示一键提权重启。
/// </summary>
public static class IntegrityService
{
    public enum Level { Unknown = 0, Low = 0x1000, Medium = 0x2000, High = 0x3000, System = 0x4000 }

    private const int TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int size, out int returned);

    public static Level Current
    {
        get
        {
            var handle = IntPtr.Zero;
            var token = IntPtr.Zero;
            try
            {
                handle = OpenProcess(ProcessQueryLimitedInformation, false, Environment.ProcessId);
                if (handle == IntPtr.Zero) return Level.Unknown;
                if (!OpenProcessToken(handle, TokenQuery, out token)) return Level.Unknown;

                GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var size);
                if (size <= 0) return Level.Unknown;

                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _)) return Level.Unknown;
                    // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes } }
                    var sid = Marshal.ReadIntPtr(buffer);
                    if (sid == IntPtr.Zero) return Level.Unknown;
                    // SID: Revision(1) Count(1) Authority(6) SubAuthority[0](4)
                    var rid = Marshal.ReadInt32(sid, 8);
                    return rid switch
                    {
                        0x1000 => Level.Low,
                        0x2000 => Level.Medium,
                        0x3000 => Level.High,
                        0x4000 => Level.System,
                        _ => Level.Unknown
                    };
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch
            {
                return Level.Unknown;
            }
            finally
            {
                if (token != IntPtr.Zero) CloseHandle(token);
                if (handle != IntPtr.Zero) CloseHandle(handle);
            }
        }
    }

    public static string Describe(Level level) => level switch
    {
        Level.Low => "低权限（Low）",
        Level.Medium => "普通权限（Medium）",
        Level.High => "管理员（High）",
        Level.System => "系统（System）",
        _ => "未知"
    };

    /// <summary>以管理员身份重新拉起自己（会弹 UAC）。</summary>
    public static bool RestartElevated()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
