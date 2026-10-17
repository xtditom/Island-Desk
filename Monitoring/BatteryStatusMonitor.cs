using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

namespace IslandDesk.Monitoring;

internal enum BatteryPresence { Unavailable, None, Present }
internal enum BatteryFlow { Unknown, Charging, Discharging, PluggedIn, Full }
internal sealed record BatteryReading(BatteryPresence Presence, int? Percent = null,
    BatteryFlow Flow = BatteryFlow.Unknown, uint? RemainingSeconds = null)
{
    public static BatteryReading Unavailable { get; } = new(BatteryPresence.Unavailable);
}

/// <summary>Initial read plus coalesced native power notifications; no sampling timer.</summary>
internal sealed class BatteryStatusMonitor
{
    private static readonly Guid PercentageSetting = new("A7AD8041-B45A-4CAE-87A3-EECBB468A9E1");
    private static readonly Guid PowerSourceSetting = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");
    private readonly Channel<byte> _requests = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Func<BatteryReading> _read;

    public BatteryStatusMonitor(Func<BatteryReading>? read = null) => _read = read ?? Read;
    public void RequestRefresh() => _requests.Writer.TryWrite(0);
    public void PowerChanged(nint eventId, nint data)
    {
        if (eventId is 0xA or 0x7 or 0x12) RequestRefresh(); // Status / resume.
        else if (eventId == 0x8013 && data != 0)
        {
            var setting = Marshal.PtrToStructure<Guid>(data);
            if (setting == PercentageSetting || setting == PowerSourceSetting) RequestRefresh();
        }
    }
    public Task RunAsync(nint window, Func<BatteryReading, Task> publish, CancellationToken token) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        using var percentage = Register(window, PercentageSetting);
        using var source = Register(window, PowerSourceSetting);
        RequestRefresh();
        try
        {
            await foreach (var _ in _requests.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                var reading = _read();
                token.ThrowIfCancellationRequested();
                await publish(reading).ConfigureAwait(false);
            }
        }
        finally { _requests.Writer.TryComplete(); }
    }, token);

    private static PowerNotification? Register(nint window, Guid setting)
    {
        if (window == 0) return null;
        var handle = RegisterPowerSettingNotification(window, in setting, 0);
        if (handle != 0) return new(handle);
        // Basic WM_POWERBROADCAST status/resume messages still work if registration fails.
        System.Diagnostics.Trace.TraceWarning("Power notification registration failed: {0}", Marshal.GetLastWin32Error());
        return null;
    }
    public static BatteryReading Read() => GetSystemPowerStatus(out var status) ? FromNative(status) : BatteryReading.Unavailable;
    internal static BatteryReading FromNative(NativePowerStatus status)
    {
        // 255 means unknown, not the combined bits for 'charging' and 'no battery'.
        if (status.BatteryFlag == 255) return BatteryReading.Unavailable;
        if ((status.BatteryFlag & 128) != 0) return new(BatteryPresence.None);
        int? percent = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : null;
        var flow = (status.BatteryFlag & 8) != 0 ? BatteryFlow.Charging : status.AcLineStatus switch
        { 0 => BatteryFlow.Discharging, 1 when percent == 100 => BatteryFlow.Full, 1 => BatteryFlow.PluggedIn, _ => BatteryFlow.Unknown };
        uint? remaining = flow == BatteryFlow.Discharging && status.BatteryLifeTime != uint.MaxValue ? status.BatteryLifeTime : null;
        return new(BatteryPresence.Present, percent, flow, remaining);
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePowerStatus
    {
        public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }
    private sealed class PowerNotification : SafeHandleZeroOrMinusOneIsInvalid
    {
        public PowerNotification(nint value) : base(true) => SetHandle(value);
        protected override bool ReleaseHandle() => UnregisterPowerSettingNotification(handle);
    }
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out NativePowerStatus status);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint RegisterPowerSettingNotification(nint recipient, in Guid setting, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(nint handle);
}
