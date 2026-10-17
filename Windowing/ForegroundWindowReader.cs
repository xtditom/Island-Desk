using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace IslandDesk.Windowing;

internal sealed record ForegroundReading(nint Window, uint ProcessId, bool HideIsland, bool TrackLocation, bool NeedsFallback);

/// <summary>Read-only native geometry queries, serialized by FullscreenObserver.</summary>
internal sealed class ForegroundWindowReader
{
    private uint _lastProcess;
    private nint _lastWindow;
    private string _processName = "";

    public ForegroundReading Read(nint island)
    {
        // DWM bounds are physical pixels. Keep all other rectangles in the same space,
        // even when this thread-pool thread has a different DPI context from WPF.
        var previousContext = SetThreadDpiAwarenessContext(new nint(-4));
        try
        {
            var window = GetForegroundWindow();
            var empty = new ForegroundReading(window, 0, false, false, false);
            if (window == 0 || !IsWindowVisible(window) || IsIconic(window)) return empty;
            GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId == Environment.ProcessId || window == GetShellWindow() || window == GetDesktopWindow()) return empty;
            var monitor = MonitorFromWindow(island, 2);
            var information = new MonitorInformation { Size = (uint)Marshal.SizeOf<MonitorInformation>() };
            if (monitor == 0 || !GetMonitorInfo(monitor, ref information) || !GetWindowRect(window, out var frame)) return empty;
            if (DwmGetWindowAttribute(window, 9, out PixelRect visibleFrame, 16) == 0 && visibleFrame.IsValid) frame = visibleFrame;
            var cloaked = DwmGetWindowAttribute(window, 14, out uint cloak, 4) == 0 && cloak != 0;
            var classBuffer = new StringBuilder(256);
            GetClassName(window, classBuffer, classBuffer.Capacity);
            if (processId != _lastProcess || window != _lastWindow)
            {
                _lastProcess = processId; _lastWindow = window;
                _processName = ReadProcessName(processId);
            }
            var client = default(PixelRect);
            if (GetClientRect(window, out var local))
            {
                var topLeft = new NativePoint { X = local.Left, Y = local.Top };
                var bottomRight = new NativePoint { X = local.Right, Y = local.Bottom };
                if (ClientToScreen(window, ref topLeft) && ClientToScreen(window, ref bottomRight))
                    client = new(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
            }
            var candidate = new FullscreenCandidate(frame, client, (uint)GetWindowLong(window, -16), IsZoomed(window),
                IsCloaked: cloaked, WindowClass: classBuffer.ToString(), ProcessName: _processName);
            if (FullscreenPolicy.IsExcluded(candidate)) return empty;
            return new(window, processId, FullscreenPolicy.IsFullscreen(candidate, information.Monitor),
                true, frame.Intersects(information.Monitor));
        }
        finally { if (previousContext != 0) SetThreadDpiAwarenessContext(previousContext); }
    }

    private static string ReadProcessName(uint processId)
    {
        using var process = OpenProcess(0x1000, false, processId); // Limited read-only query; no elevation.
        if (process.IsInvalid) return "";
        var path = new StringBuilder(1024); var length = (uint)path.Capacity;
        return QueryFullProcessImageName(process, 0, path, ref length) ? Path.GetFileNameWithoutExtension(path.ToString()) : "";
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInformation { public uint Size; public PixelRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInformation info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out PixelRect rectangle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint window, out PixelRect rectangle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(nint window, ref NativePoint point);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out PixelRect value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out uint value, int size);
    [DllImport("kernel32.dll")] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);
}
