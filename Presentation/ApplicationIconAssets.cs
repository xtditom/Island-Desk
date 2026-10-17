using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using DrawingIcon = System.Drawing.Icon;

namespace IslandDesk.Presentation;

internal static class ApplicationIconAssets
{
    private static readonly Uri IconUri = new("/IslandDesk;component/Assets/IslandDesk.ico", UriKind.Relative);
    private static readonly Lazy<BitmapFrame?> CachedWindowIcon = new(LoadWindowIcon);
    public static BitmapFrame? WindowIcon => CachedWindowIcon.Value;
    private static Stream? Open()
    {
        try { return Application.GetResourceStream(IconUri)?.Stream; }
        catch (IOException) { return null; } // Optional until the custom ICO is supplied.
    }
    private static BitmapFrame? LoadWindowIcon()
    {
        try
        {
            using var stream = Open(); if (stream is null) return null;
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var image = decoder.Frames.OrderByDescending(frame => frame.PixelWidth).First();
            image.Freeze(); return image;
        }
        catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException or FileFormatException)
        { return null; }
    }
    public static DrawingIcon? CreateTrayIcon()
    {
        try
        {
            using var stream = Open(); if (stream is null) return null;
            using var icon = new DrawingIcon(stream);
            return (DrawingIcon)icon.Clone();
        }
        catch (Exception error) when (error is IOException or ArgumentException) { return null; }
    }
}
