using System.Runtime.InteropServices;
using System.Text;

namespace BulesrtLauncher.Services.Launching;

/// <summary>Resolves classic .lnk shortcuts into target / arguments / working directory.</summary>
public static class ShortcutResolver
{
    public sealed record ShortcutInfo(string Target, string Arguments, string WorkingDirectory, bool RunAsAdmin);

    public static ShortcutInfo? Resolve(string lnkPath)
    {
        object? link = null;
        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(lnkPath, 0);
            var linkW = (IShellLinkW)link;

            var target = new StringBuilder(1024);
            linkW.GetPath(target, target.Capacity, IntPtr.Zero, 0x4 /* SLGP_RAWPATH */);

            var args = new StringBuilder(1024);
            linkW.GetArguments(args, args.Capacity);

            var work = new StringBuilder(1024);
            linkW.GetWorkingDirectory(work, work.Capacity);

            return new ShortcutInfo(
                Environment.ExpandEnvironmentVariables(target.ToString()),
                args.ToString(),
                Environment.ExpandEnvironmentVariables(work.ToString()),
                HasRunAsAdminFlag(lnkPath));
        }
        catch
        {
            return null;
        }
        finally
        {
            if (link is not null) try { Marshal.FinalReleaseComObject(link); } catch { }
        }
    }

    /// <summary>
    /// .lnk 的 LinkFlags 在偏移 0x14（DWORD），第 0x20 位 = RunAsAdministrator。
    /// 资源管理器双击时会据此提权；启动器如果不继承，像 HiBit Uninstaller 这种必须管理员权限
    /// 的程序就会以普通权限跑起来，行为可能和双击不一致。
    /// </summary>
    private static bool HasRunAsAdminFlag(string lnkPath)
    {
        try
        {
            var bytes = System.IO.File.ReadAllBytes(lnkPath);
            if (bytes.Length < 0x18) return false;
            var flags = System.BitConverter.ToUInt32(bytes, 0x14);
            return (flags & 0x20) != 0;
        }
        catch
        {
            return false;
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
    }
}

