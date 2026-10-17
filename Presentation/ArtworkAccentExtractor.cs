using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IslandDesk.Presentation;

internal static class ArtworkAccentExtractor
{
    private static readonly Dictionary<string, Color?> Cache = new();
    private static readonly Queue<string> Keys = new();
    // Called only for frozen, bounded thumbnails on a worker thread. Cache retains no bitmap.
    internal static Color? Extract(BitmapSource image, CancellationToken token)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        var key = Convert.ToHexString(SHA256.HashData(pixels));
        lock (Cache) if (Cache.TryGetValue(key, out var cached)) return cached;
        var bins = new (int Count, long R, long G, long B)[4096];
        for (var i = 0; i < pixels.Length; i += 8)
        {
            token.ThrowIfCancellationRequested();
            var b = pixels[i]; var g = pixels[i + 1]; var r = pixels[i + 2];
            var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b));
            if (pixels[i + 3] < 128 || max < 40 || min > 225 || max - min < 16) continue;
            var bin = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
            bins[bin].Count++; bins[bin].R += r; bins[bin].G += g; bins[bin].B += b;
        }
        var best = bins.MaxBy(bin => bin.Count);
        Color? accent = best.Count == 0 ? null : EnsureContrast(Color.FromRgb((byte)(best.R / best.Count), (byte)(best.G / best.Count), (byte)(best.B / best.Count)));
        lock (Cache)
        {
            if (!Cache.ContainsKey(key))
            {
                if (Cache.Count >= 32) Cache.Remove(Keys.Dequeue());
                Cache[key] = accent; Keys.Enqueue(key);
            }
        }
        return accent;
    }
    internal static Color EnsureContrast(Color color, bool dark = true)
    {
        // Contrast against the opaque charcoal palette; transparent desktop colors can affect the result.
        while (dark ? Luminance(color) < .4 : Luminance(color) > .06)
            color = IslandPalette.Mix(color, dark ? Colors.White : Colors.Black, dark ? .08 : .12);
        color.A = 255; return color;
    }
    private static double Luminance(Color c)
    {
        static double Linear(byte channel) { var value = channel / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
        return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
    }
}
