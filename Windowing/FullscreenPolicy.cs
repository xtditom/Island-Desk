using System.Runtime.InteropServices;

namespace IslandDesk.Windowing;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public bool IsValid => Right > Left && Bottom > Top;
    public bool Intersects(PixelRect other) => Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
    public bool Covers(PixelRect other, int tolerance = 2) => IsValid && other.IsValid &&
        Left <= other.Left + tolerance && Top <= other.Top + tolerance &&
        Right >= other.Right - tolerance && Bottom >= other.Bottom - tolerance;
}

internal sealed record FullscreenCandidate(PixelRect Frame, PixelRect Client, long Style,
    bool IsMaximized, bool IsVisible = true, bool IsMinimized = false, bool IsCloaked = false,
    bool IsOwnProcess = false, bool IsShellWindow = false, string WindowClass = "", string ProcessName = "");

internal static class FullscreenPolicy
{
    private const long Caption = 0x00C00000, ThickFrame = 0x00040000;
    public static bool IsExcluded(FullscreenCandidate candidate) => candidate.IsOwnProcess || candidate.IsShellWindow ||
        !candidate.IsVisible || candidate.IsMinimized || candidate.IsCloaked ||
        candidate.WindowClass is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" ||
        candidate.ProcessName.StartsWith("MyDock", StringComparison.OrdinalIgnoreCase) ||
        candidate.ProcessName.StartsWith("MyFinder", StringComparison.OrdinalIgnoreCase);

    public static bool IsFullscreen(FullscreenCandidate candidate, PixelRect islandMonitor)
    {
        if (IsExcluded(candidate) || !candidate.Frame.Covers(islandMonitor)) return false;
        // Normal maximization can cover the whole monitor with an auto-hidden taskbar.
        if (candidate.IsMaximized && (candidate.Style & (Caption | ThickFrame)) != 0) return false;
        // Retained native chrome must not leave a caption/border occupying monitor space.
        if ((candidate.Style & (Caption | ThickFrame)) != 0 && !candidate.Client.Covers(islandMonitor)) return false;
        return true;
    }
}
