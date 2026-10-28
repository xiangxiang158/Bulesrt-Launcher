using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace BulesrtLauncher.Services.Platform;

/// <summary>System wide hotkey bound to the main window message loop.</summary>
public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int Id = 0xB10;
    private HwndSource? _source;
    private bool _registered;

    public event Action? Pressed;
    public string Active { get; private set; } = "";

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public bool Register(Window window, string gesture)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return false;
        if (_source is null)
        {
            _source = HwndSource.FromHwnd(handle);
            _source?.AddHook(Hook);
        }
        if (_registered)
        {
            UnregisterHotKey(handle, Id);
            _registered = false;
        }
        if (!TryParse(gesture, out var mods, out var vk)) return false;
        if (!RegisterHotKey(handle, Id, mods, vk)) return false;
        _registered = true;
        Active = gesture;
        return true;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == Id)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public static bool TryParse(string gesture, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        foreach (var part in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= 0x0002; break;
                case "alt": mods |= 0x0001; break;
                case "shift": mods |= 0x0004; break;
                case "win": case "meta": mods |= 0x0008; break;
                case "space": vk = 0x20; break;
                case "tab": vk = 0x09; break;
                case "enter": vk = 0x0D; break;
                case "f1": vk = 0x70; break;
                case "f2": vk = 0x71; break;
                case "f3": vk = 0x72; break;
                case "f4": vk = 0x73; break;
                case "f5": vk = 0x74; break;
                case "f6": vk = 0x75; break;
                case "f7": vk = 0x76; break;
                case "f8": vk = 0x77; break;
                case "f9": vk = 0x78; break;
                case "f10": vk = 0x79; break;
                case "f11": vk = 0x7A; break;
                case "f12": vk = 0x7B; break;
                default:
                    if (part.Length == 1)
                    {
                        var c = char.ToUpperInvariant(part[0]);
                        if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') vk = c;
                    }
                    break;
            }
        }
        return mods != 0 && vk != 0;
    }

    public void Dispose()
    {
        if (_registered && _source is not null)
        {
            UnregisterHotKey(_source.Handle, Id);
            _registered = false;
        }
        _source?.RemoveHook(Hook);
    }
}

