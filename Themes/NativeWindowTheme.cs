using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace IslandDesk.Themes;

internal static class NativeWindowTheme
{
    internal static void Track(Window window)
    {
        if (IslandDesk.Presentation.ApplicationIconAssets.WindowIcon is { } icon) window.Icon = icon;
        var service = ThemeService.Active;
        void Update(object? sender, EventArgs args) => Apply(window, service?.IsDark ?? true);
        window.SourceInitialized += Update;
        if (service is not null) service.Changed += Update;
        window.Closed += (_, _) => { window.SourceInitialized -= Update; if (service is not null) service.Changed -= Update; };
    }
    private static void Apply(Window window, bool dark)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var hwnd = new WindowInteropHelper(window).Handle; if (hwnd == 0) return;
        var enabled = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int));
        // Explicit caption colors also honor a manual app theme when Windows differs.
        SetColor(35, "SettingsSurface"); SetColor(36, "Primary"); SetColor(34, "WindowBorder");
        void SetColor(uint attribute, string resource)
        {
            if (window.TryFindResource(resource) is not SolidColorBrush brush) return;
            var color = brush.Color.R | (brush.Color.G << 8) | (brush.Color.B << 16);
            _ = DwmSetWindowAttribute(hwnd, attribute, ref color, sizeof(int));
        }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, ref int value, int size);
}
