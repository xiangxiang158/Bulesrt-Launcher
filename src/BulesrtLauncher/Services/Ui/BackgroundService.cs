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

    /// <summary>
    /// 把「模糊」提前烤进位图。
    ///
    /// 原来 XAML 里挂的是实时 Gaussian <c>BlurEffect</c>：分层窗口（AllowsTransparency）没有 GPU 合成，
    /// 每一帧都要在 CPU 上把整窗重新模糊一遍——拖窗口、滚动、动图播放全都掉帧。改成加载时先降采样、
    /// 再做三遍盒式模糊（≈高斯），播放阶段就只剩一次贴图。
    /// </summary>
    /// <param name="source">原图（冻结位图，可在后台线程安全使用）</param>
    /// <param name="radius">滑块上的模糊值，单位是屏幕像素</param>
    /// <param name="screenWidth">窗口宽度（DIP）：按它折算盒式半径，不同窗口尺寸下模糊观感才一致</param>
    /// <param name="maxWidth">烘焙后的最大宽度：屏幕上已经糊成一片，不需要原分辨率</param>
    public static BitmapSource? BakeBlur(BitmapSource? source, double radius, double screenWidth = 1200, int maxWidth = 1280)
    {
        if (source is null) return null;
        radius = Math.Clamp(radius, 0, 60);
        if (radius < 0.6) return source;

        // 0.33 是和旧渲染（BlurEffect + 1+r/90 放大）做像素级比对标定出来的
        const double BoxFactor = 0.33;

        try
        {
            var w0 = source.PixelWidth;
            var h0 = source.PixelHeight;
            if (w0 < 4 || h0 < 4) return source;

            // 旧版为了藏住模糊边缘，把图放大过 1 + radius/90；这里直接裁同样的中心区域，
            // 取景和以前完全一致，却不需要渲染期再做缩放。
            var zoom = 1 + radius / 90.0;
            BitmapSource baseImage = source;
            if (zoom > 1.0001)
            {
                var cw = Math.Max(4, (int)Math.Round(w0 / zoom));
                var ch = Math.Max(4, (int)Math.Round(h0 / zoom));
                baseImage = new CroppedBitmap(source, new Int32Rect((w0 - cw) / 2, (h0 - ch) / 2, cw, ch));
            }

            var sizeW = baseImage.PixelWidth;
            var scale = sizeW > maxWidth ? (double)maxWidth / sizeW : 1.0;
            BitmapSource sized = scale < 1.0
                ? new TransformedBitmap(baseImage, new ScaleTransform(scale, scale))
                : baseImage;
            BitmapSource bgra = sized.Format == PixelFormats.Bgra32
                ? sized
                : new FormatConvertedBitmap(sized, PixelFormats.Bgra32, null, 0);

            var w = bgra.PixelWidth;
            var h = bgra.PixelHeight;
            if (w < 4 || h < 4) return source;

            var stride = w * 4;
            var pixels = new byte[stride * h];
            bgra.CopyPixels(pixels, stride, 0);

            // 盒式模糊三遍的 sigma ≈ 半径本身；半径按「屏幕像素 → 烘焙图像素」换算，
            // 滑块看到的模糊强度才和以前一致。
            var box = (int)Math.Round(radius * BoxFactor * w / Math.Max(1, screenWidth));
            box = Math.Clamp(box, 1, Math.Max(1, Math.Min(w, h) / 3));

            Premultiply(pixels);
            for (var pass = 0; pass < 3; pass++)
            {
                BoxPass(pixels, w, h, box, horizontal: true);
                BoxPass(pixels, w, h, box, horizontal: false);
            }
            Unpremultiply(pixels);

            var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
        catch
        {
            return source;
        }
    }

    /// <summary>预乘 alpha：直接模糊 A 通道会在半透明边缘留黑边，先乘后除就没这问题。</summary>
    private static void Premultiply(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            if (a == 255) continue;
            pixels[i] = (byte)(pixels[i] * a / 255);
            pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
            pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
        }
    }

    private static void Unpremultiply(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            if (a == 255 || a == 0) continue;
            pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
            pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
            pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
        }
    }

    /// <summary>滑窗盒式模糊的一遍（横或纵），边界按边缘像素延伸。</summary>
    private static void BoxPass(byte[] pixels, int w, int h, int r, bool horizontal)
    {
        var len = horizontal ? w : h;
        var outer = horizontal ? h : w;
        var count = 2 * r + 1;
        var line = new byte[len * 4];
        var done = new byte[len * 4];

        for (var o = 0; o < outer; o++)
        {
            for (var i = 0; i < len; i++)
            {
                var p = horizontal ? (o * w + i) * 4 : (i * w + o) * 4;
                var q = i * 4;
                line[q] = pixels[p];
                line[q + 1] = pixels[p + 1];
                line[q + 2] = pixels[p + 2];
                line[q + 3] = pixels[p + 3];
            }

            int sb = 0, sg = 0, sr = 0, sa = 0;
            for (var i = -r; i <= r; i++)
            {
                var idx = Math.Clamp(i, 0, len - 1) * 4;
                sb += line[idx];
                sg += line[idx + 1];
                sr += line[idx + 2];
                sa += line[idx + 3];
            }

            for (var i = 0; i < len; i++)
            {
                var q = i * 4;
                done[q] = (byte)(sb / count);
                done[q + 1] = (byte)(sg / count);
                done[q + 2] = (byte)(sr / count);
                done[q + 3] = (byte)(sa / count);

                var add = Math.Min(len - 1, i + r + 1) * 4;
                var sub = Math.Max(0, i - r) * 4;
                sb += line[add] - line[sub];
                sg += line[add + 1] - line[sub + 1];
                sr += line[add + 2] - line[sub + 2];
                sa += line[add + 3] - line[sub + 3];
            }

            for (var i = 0; i < len; i++)
            {
                var p = horizontal ? (o * w + i) * 4 : (i * w + o) * 4;
                var q = i * 4;
                pixels[p] = done[q];
                pixels[p + 1] = done[q + 1];
                pixels[p + 2] = done[q + 2];
                pixels[p + 3] = done[q + 3];
            }
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
