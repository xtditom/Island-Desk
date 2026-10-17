using System.Runtime.InteropServices;

namespace IslandDesk.Windowing;

internal static class MonitorRefreshReader
{
    // Reports the configured display mode, not instantaneous VRR/compositor FPS.
    public static int? Read(nint monitor)
    {
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>(), Device = "" };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info)) return null;
        var mode = new DeviceMode { Size = (ushort)Marshal.SizeOf<DeviceMode>() };
        if (!EnumDisplaySettings(info.Device, uint.MaxValue, ref mode) || (mode.Fields & 0x00400000) == 0)
            return null;
        // 0/1 mean the default driver rate, not measured Hz.
        return mode.Frequency is > 1 and <= 1000 ? (int)mode.Frequency : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    // DEVMODEW display fields; no printer union or driver-private storage is needed.
    [StructLayout(LayoutKind.Explicit, Size = 220)]
    private struct DeviceMode
    {
        [FieldOffset(68)] public ushort Size;
        [FieldOffset(72)] public uint Fields;
        [FieldOffset(184)] public uint Frequency;
    }
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string device, uint mode, ref DeviceMode settings);
}
