using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using BulesrtLauncher.Services.Launching;
using BulesrtLauncher.Services.Storage;

namespace BulesrtLauncher.Services.Icons;

/// <summary>
/// Shell icon extraction with a disk cache.
/// Shortcuts are resolved first (so we get the target's real icon without the shortcut arrow),
/// icons come from the same 256px source Explorer uses, and results are cached as PNGs so the
/// next start is instant.
/// </summary>
public static class IconService
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Memory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(4);
    private static readonly string CacheDir = Path.Combine(StoreService.DataDir, "icon-cache");

    public static async Task<ImageSource?> GetAsync(string path)    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (Memory.TryGetValue(path, out var cached)) return cached;

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var resolved = ResolveIconSource(path);
            var key = CacheKey(resolved ?? path);
            var png = Path.Combine(CacheDir, key + ".png");

            if (resolved is null)
            {
                Memory[path] = null;
                return null;
            }

            if (File.Exists(png))
            {
                var fromDisk = LoadPng(png);
                if (fromDisk is not null)
                {
                    Memory[path] = fromDisk;
                    return fromDisk;
                }
            }

            var icon = ExtractOnSta(resolved);
            if (icon is not null)
            {
                SavePng(icon, png);
                Memory[path] = icon;
                return icon;
            }

            // A dead shortcut resolves to nothing useful — remember it as "no icon".
            Memory[path] = null;
            return null;
        }
        catch
        {
            Memory[path] = null;
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Drop every cached icon (memory + disk) so the next load re-extracts.</summary>
    public static void ClearCache()
    {
        Memory.Clear();
        try
        {
            if (Directory.Exists(CacheDir))
                foreach (var file in Directory.GetFiles(CacheDir, "*.png"))
                    File.Delete(file);
        }
        catch
        {
            // ignored
        }
    }

    // ------------------------------------------------------------------ resolve
    private static string? ResolveIconSource(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".lnk")
            {
                if (!File.Exists(path)) return null;
                var target = ShortcutResolver.Resolve(path)?.Target;
                if (!string.IsNullOrWhiteSpace(target) && File.Exists(target))
                    return target;
                // Shell links (Recycle Bin, This PC, ...) have no file target but do carry an icon.
                return path;
            }

            if (ext == ".url")
            {
                if (!File.Exists(path)) return null;
                var iconFile = ReadUrlIcon(path);
                return iconFile is not null && File.Exists(iconFile) ? iconFile : null;
            }

            return File.Exists(path) || Directory.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadUrlIcon(string urlFile)
    {
        try
        {
            foreach (var line in File.ReadLines(urlFile))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase)) continue;
                var value = Environment.ExpandEnvironmentVariables(trimmed["IconFile=".Length..].Trim().Trim('"'));
                return value;
            }
        }
        catch
        {
            // ignored
        }
        return null;
    }

    private static string CacheKey(string path)
    {
        long stamp = 0;
        long size = 0;
        try
        {
            if (File.Exists(path))
            {
                stamp = File.GetLastWriteTimeUtc(path).Ticks;
                size = new FileInfo(path).Length;
            }
            else if (Directory.Exists(path))
            {
                stamp = Directory.GetLastWriteTimeUtc(path).Ticks;
            }
        }
        catch
        {
            // ignored
        }

        var raw = $"{path.ToLowerInvariant()}|{stamp}|{size}";
        uint hash = 2166136261;
        foreach (var ch in raw)
        {
            hash ^= ch;
            hash *= 16777619;
        }
        return hash.ToString("x8");
    }

    // ------------------------------------------------------------------ extract
    private static ImageSource? ExtractOnSta(string path)
    {
        ImageSource? result = null;
        var thread = new Thread(() =>
        {
            try { result = Extract(path); }
            catch { result = null; }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(5));

        if (result is not null && IsBlankDocument(result)) return null;
        return result;
    }

    private static ImageSource? Extract(string path)
    {
        var big = TryJumbo(path, 256);
        if (big is not null) return Trim(big);
        var small = TryShellIcon(path);
        return small is null ? null : Trim(small);
    }

    /// <summary>Cut away transparent padding so every tile icon has a similar visual weight.</summary>
    private static (int minX, int minY, int maxX, int maxY) Bounds(byte[] pixels, int w, int h, int alphaThreshold)
    {
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (var y = 0; y < h; y++)
        {
            var row = y * w * 4;
            for (var x = 0; x < w; x++)
            {
                if (pixels[row + x * 4 + 3] <= alphaThreshold) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        return (minX, minY, maxX, maxY);
    }

    private static ImageSource Trim(ImageSource source)
    {
        if (source is not BitmapSource bitmap) return source;
        try
        {
            var w = bitmap.PixelWidth;
            var h = bitmap.PixelHeight;
            if (w == 0 || h == 0) return bitmap;

            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[w * h * 4];
            converted.CopyPixels(pixels, w * 4, 0);

            var (minX, minY, maxX, maxY) = Bounds(pixels, w, h, 32);

            // Icons with a faint full-canvas glow keep the alpha>32 box at 100%. Retry with a
            // stricter threshold so the actual artwork can still be cropped.
            if (maxX < 0 || (maxX - minX + 1) >= w * 0.92 && (maxY - minY + 1) >= h * 0.92)
            {
                var strict = Bounds(pixels, w, h, 128);
                if (strict.maxX >= 0) (minX, minY, maxX, maxY) = strict;
            }

            if (maxX < 0) return source;

            var bw = maxX - minX + 1;
            var bh = maxY - minY + 1;
            if (bw >= w * 0.9 && bh >= h * 0.9) return source;

            var pad = Math.Max(1, Math.Min(w, h) / 64);
            minX = Math.Max(0, minX - pad);
            minY = Math.Max(0, minY - pad);
            maxX = Math.Min(w - 1, maxX + pad);
            maxY = Math.Min(h - 1, maxY + pad);
            bw = maxX - minX + 1;
            bh = maxY - minY + 1;

            var cropped = new CroppedBitmap(converted, new Int32Rect(minX, minY, bw, bh));
            cropped.Freeze();
            return cropped;
        }
        catch
        {
            return source;
        }
    }

    private static ImageSource? TryJumbo(string path, int px)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) != 0 || factory is null)
            return null;
        IntPtr hbm = IntPtr.Zero;
        try
        {
            if (factory.GetImage(new SIZE(px, px), SIIGBF.ICONONLY | SIIGBF.BIGGERSIZEOK, out hbm) != 0 || hbm == IntPtr.Zero)
                return null;
            var src = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
            if (factory is not null) Marshal.ReleaseComObject(factory);
        }
    }

    private static ImageSource? TryShellIcon(string path)
    {
        var info = new SHFILEINFO();
        const uint flags = SHGFI_ICON | SHGFI_LARGEICON;
        var result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    // ------------------------------------------------------------------ cache io
    private static ImageSource? LoadPng(string file)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(file, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static void SavePng(ImageSource icon, string file)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create((BitmapSource)icon));
            using var stream = File.Create(file);
            encoder.Save(stream);
        }
        catch
        {
            // cache is best effort
        }
    }

    // ------------------------------------------------------------------ helpers
    /// <summary>Windows returns a plain white "document" icon for dead targets.</summary>
    private static bool IsBlankDocument(ImageSource source)
    {
        try
        {
            if (source is not BitmapSource bitmap || bitmap.PixelWidth == 0) return false;
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var scale = new TransformedBitmap(converted,
                new ScaleTransform(32.0 / bitmap.PixelWidth, 32.0 / bitmap.PixelHeight));
            var pixels = new byte[32 * 32 * 4];
            scale.CopyPixels(pixels, 32 * 4, 0);
            int total = 0, white = 0, colored = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] < 40) continue;
                total++;
                byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
                if (r > 232 && g > 232 && b > 232) white++;
                if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > 30) colored++;
            }
            return total > 40 && white > total * 0.88 && colored < total * 0.04;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct SIZE
    {
        public readonly int cx;
        public readonly int cy;
        public SIZE(int x, int y) { cx = x; cy = y; }
    }

    [Flags]
    private enum SIIGBF
    {
        RESIZETOFIT = 0,
        BIGGERSIZEOK = 1,
        ICONONLY = 4
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi,
        uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}


