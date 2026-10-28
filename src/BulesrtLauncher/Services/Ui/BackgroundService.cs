using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BulesrtLauncher.Services.Storage;

namespace BulesrtLauncher.Services.Ui;

/// <summary>Custom backdrop image: import, resolve, decode (capped) and clean up.</summary>
public static class BackgroundService
{
    private const int MaxWidth = 2560;

    public static string Dir => Path.Combine(StoreService.DataDir, "background");

    /// <summary>Copies a user-picked picture into data\background and returns its stored file name.</summary>
    public static string Import(string sourcePath)
    {
        Directory.CreateDirectory(Dir);
        var name = Sanitize(Path.GetFileName(sourcePath));
        var dest = Path.Combine(Dir, name);
        File.Copy(sourcePath, dest, true);

        // one backdrop at a time keeps the folder tidy
        foreach (var old in Directory.GetFiles(Dir))
            if (!string.Equals(old, dest, StringComparison.OrdinalIgnoreCase)) TryDelete(old);

        return name;
    }

    /// <summary>Extracts the bundled artwork so the user can use it as a backdrop with one click.</summary>
    public static string? ImportArtwork()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var dest = Path.Combine(Dir, "artwork.png");
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/art.png"));
            if (info is null) return null;
            using (var target = File.Create(dest))
                info.Stream.CopyTo(target);
            foreach (var old in Directory.GetFiles(Dir))
                if (!string.Equals(old, dest, StringComparison.OrdinalIgnoreCase)) TryDelete(old);
            return Path.GetFileName(dest);
        }
        catch
        {
            return null;
        }
    }

    public static string? Resolve(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var path = Path.Combine(Dir, fileName);
        return File.Exists(path) ? path : null;
    }

    public static void Clear()
    {
        if (!Directory.Exists(Dir)) return;
        foreach (var file in Directory.GetFiles(Dir)) TryDelete(file);
    }

    /// <summary>Decodes a backdrop, downscaling huge photos so blurring stays cheap.</summary>
    public static BitmapSource? Decode(string path)
    {
        try
        {
            int width;
            using (var probe = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                width = decoder.Frames[0].PixelWidth;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            if (width > MaxWidth) image.DecodePixelWidth = MaxWidth;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True when the file is a multi-frame GIF (APNG / WebP need extra codecs, so they stay still).</summary>
    public static bool IsAnimated(string path)
    {
        if (!path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            return decoder.Frames.Count > 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Decodes every GIF frame (downscaled, frozen) together with its delay. Frames are capped so a
    /// 500-frame sticker GIF cannot eat gigabytes.
    /// </summary>
    public static AnimatedBackdrop? LoadAnimation(string path, int maxWidth = 960, int maxFrames = 160)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count < 2) return null;

            var first = decoder.Frames[0];
            var scale = first.PixelWidth > maxWidth ? (double)maxWidth / first.PixelWidth : 1;
            var pixels = (long)Math.Max(1, (int)(first.PixelWidth * scale)) *
                         Math.Max(1, (int)(first.PixelHeight * scale)) * 4;
            var budget = 160L * 1024 * 1024 / Math.Max(1, pixels);   // ~160 MB cap
            var count = (int)Math.Clamp(budget, 2, Math.Min(maxFrames, decoder.Frames.Count));

            var frames = new List<(BitmapSource Frame, TimeSpan Delay)>(count);
            for (var i = 0; i < count; i++)
            {
                var frame = decoder.Frames[i];
                BitmapSource output = frame;
                if (scale < 1)
                {
                    var scaled = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                    scaled.Freeze();
                    output = scaled;
                }
                else
                {
                    frame.Freeze();
                }
                frames.Add((output, DelayOf(frame)));
            }
            return new AnimatedBackdrop(frames, count < decoder.Frames.Count);
        }
        catch
        {
            return null;
        }
    }

    private static TimeSpan DelayOf(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata meta)
            {
                var raw = meta.GetQuery("/grctlext/Delay");
                var delay = raw switch
                {
                    ushort u => u,
                    byte b => b,
                    int i => i,
                    short s => s,
                    _ => 0
                };
                // browsers treat 0 / 1 (10ms) as "slow it down", GIFs are 1/100 s units
                if (delay <= 1) return TimeSpan.FromMilliseconds(100);
                return TimeSpan.FromMilliseconds(Math.Min(1000, delay * 10));
            }
        }
        catch
        {
            // metadata is optional
        }
        return TimeSpan.FromMilliseconds(100);
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        if (name.Length > 60)
        {
            var ext = Path.GetExtension(name);
            name = name[..Math.Max(1, 60 - ext.Length)] + ext;
        }
        return string.IsNullOrWhiteSpace(name) ? "background.png" : name;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* locked file — keep going */ }
    }
}

/// <summary>Decoded GIF frames ready for the UI timer player.</summary>
public sealed class AnimatedBackdrop
{
    public AnimatedBackdrop(IReadOnlyList<(BitmapSource Frame, TimeSpan Delay)> frames, bool truncated)
    {
        Frames = frames;
        Truncated = truncated;
    }

    public IReadOnlyList<(BitmapSource Frame, TimeSpan Delay)> Frames { get; }

    /// <summary>True when the GIF had more frames than the memory budget allowed.</summary>
    public bool Truncated { get; }
}
