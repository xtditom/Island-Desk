using System.Collections;
using System.Runtime.InteropServices;

namespace IslandDesk.Windowing;

internal sealed record BrightnessStatus(bool IsAvailable, double Percent, string Detail);

// One probe on entry/monitor change, debounced writes, no hardware poll. All WMI
// and DDC/CI operations are serialized off the dispatcher. Handles never escape.
internal sealed class ScreenBrightnessService : IDisposable
{
    private readonly object _gate = new();
    private volatile bool _disposed;
    private PhysicalMonitor[] _physical = [];
    private nint _selected;
    private uint _minimum, _maximum;
    private WmiBrightness? _integrated;
    private sealed record WmiBrightness(string Instance, byte[] Levels, byte Current);
    public static nint MonitorForWindow(nint window) => MonitorFromWindow(window, 2);
    public Task<BrightnessStatus> ProbeAsync(nint monitor) => Task.Run(() =>
    {
        lock (_gate)
        {
            ReleaseMonitors(); _integrated = null;
            if (_disposed) return Unavailable();
            try
            {
                var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    var device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
                    if (EnumDisplayDevices(info.Device, 0, ref device, 1))
                    {
                        // EDD_GET_DEVICE_INTERFACE_NAME maps this HMONITOR to the
                        // exact WMI instance, avoiding changes to another display.
                        var parts = (device.Id ?? "").Split('#');
                        if (parts.Length >= 3)
                        {
                            var prefix = parts[0].Replace(@"\\?\", "") + "\\" + parts[1] + "\\" + parts[2];
                            _integrated = QueryWmi("SELECT * FROM WmiMonitorBrightness WHERE Active=TRUE", item =>
                            {
                                dynamic row = item;
                                var instance = Convert.ToString(row.InstanceName) ?? "";
                                if (!instance.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase) && !instance.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return null;
                                var levels = ((Array)row.Level).Cast<object>().Select(Convert.ToByte).Distinct().Order().ToArray();
                                return new WmiBrightness(instance, levels, Convert.ToByte(row.CurrentBrightness));
                            });
                            if (_integrated is not null) return new BrightnessStatus(true, _integrated.Current, "Integrated display brightness");
                        }
                    }
                }
                if (_disposed || !GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count is 0 or > 16) return Unavailable();
                _physical = new PhysicalMonitor[count];
                if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, _physical)) { ReleaseMonitors(); return Unavailable(); }
                foreach (var physical in _physical)
                {
                    if (_disposed) break;
                    // Do not read/write brightness unless the monitor advertises it.
                    if (!GetMonitorCapabilities(physical.Handle, out var capabilities, out _) || (capabilities & 0x2) == 0) continue;
                    if (!GetMonitorBrightness(physical.Handle, out var minimum, out var current, out var maximum) || maximum <= minimum || current < minimum || current > maximum) continue;
                    _selected = physical.Handle; _minimum = minimum; _maximum = maximum;
                    return new BrightnessStatus(true, 100.0 * (current - minimum) / (maximum - minimum), "Monitor hardware brightness (DDC/CI)");
                }
            }
            catch (Exception error) when (IsHardwareError(error)) { }
            ReleaseMonitors();
            return Unavailable();
        }
    });
    public Task<BrightnessStatus> SetAsync(double percent) => Task.Run(() =>
    {
        lock (_gate)
        {
            if (_disposed || !double.IsFinite(percent)) return Unavailable();
            percent = Math.Clamp(percent, 0, 100);
            try
            {
                if (_integrated is { } integrated)
                {
                    var value = integrated.Levels.Length == 0 ? (byte)Math.Round(percent) : integrated.Levels.MinBy(level => Math.Abs(level - percent));
                    var result = QueryWmi("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE", item =>
                    {
                        dynamic row = item;
                        if (!string.Equals(Convert.ToString(row.InstanceName), integrated.Instance, StringComparison.OrdinalIgnoreCase)) return null;
                        var status = Convert.ToUInt32(row.WmiSetBrightness(0u, value));
                        return new BrightnessStatus(status == 0, value, status == 0 ? "Brightness updated." : "The integrated display rejected the brightness change.");
                    });
                    return result ?? Unavailable();
                }
                if (_selected != 0)
                {
                    var native = (uint)Math.Clamp(Math.Round(_minimum + percent * (_maximum - _minimum) / 100), _minimum, _maximum);
                    if (SetMonitorBrightness(_selected, native))
                        return new BrightnessStatus(true, 100.0 * (native - _minimum) / (_maximum - _minimum), "Brightness updated.");
                }
            }
            catch (Exception error) when (IsHardwareError(error)) { }
            return new BrightnessStatus(false, 0, "Windows or the monitor rejected the brightness change. Open Display Settings.");
        }
    });
    private static BrightnessStatus Unavailable() => new(false, 0, "Hardware brightness unavailable for this monitor. Check DDC/CI support or open Display Settings.");
    private static bool IsHardwareError(Exception error) => error is COMException or InvalidOperationException or UnauthorizedAccessException
        or NotSupportedException or ArgumentException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException;
    private static T? QueryWmi<T>(string query, Func<object, T?> select) where T : class
    {
        object? locator = null, services = null, results = null;
        IEnumerator? iterator = null;
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (type is null) return null;
            locator = Activator.CreateInstance(type);
            services = ((dynamic)locator!).ConnectServer(".", @"root\WMI");
            results = ((dynamic)services).ExecQuery(query);
            iterator = ((IEnumerable)results).GetEnumerator();
            while (iterator.MoveNext())
            {
                var item = iterator.Current;
                try { if (select(item) is { } found) return found; }
                finally { ReleaseCom(item); }
            }
            return null;
        }
        catch (Exception error) when (IsHardwareError(error)) { return null; }
        finally { ReleaseCom(iterator); ReleaseCom(results); ReleaseCom(services); ReleaseCom(locator); }
    }
    private static void ReleaseCom(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    private void ReleaseMonitors()
    {
        foreach (var physical in _physical) if (physical.Handle != 0) DestroyPhysicalMonitor(physical.Handle);
        _physical = []; _selected = 0;
    }
    public void Dispose()
    {
        _disposed = true;
        // A slow in-flight DDC call must not block UI teardown; its gate ensures
        // all handles are destroyed only after the native call has returned.
        _ = Task.Run(() => { lock (_gate) ReleaseMonitors(); });
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct PhysicalMonitor
    { public nint Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    { public uint Size; public NativeRect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayDevices(string device, uint number, ref DisplayDevice display, uint flags);
    [DllImport("dxva2.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint monitor, out uint count);
    [DllImport("dxva2.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPhysicalMonitorsFromHMONITOR(nint monitor, uint count, [Out] PhysicalMonitor[] physical);
    [DllImport("dxva2.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyPhysicalMonitor(nint monitor);
    [DllImport("dxva2.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorCapabilities(nint monitor, out uint capabilities, out uint temperatures);
    [DllImport("dxva2.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorBrightness(nint monitor, out uint minimum, out uint current, out uint maximum);
    [DllImport("dxva2.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetMonitorBrightness(nint monitor, uint value);
}
