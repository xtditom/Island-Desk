using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IslandDesk.Monitoring;

internal enum NetworkReadingState { Initializing, Available, Disconnected, Unavailable }
internal sealed record NetworkUsageReading(double? DownloadBytesPerSecond, double? UploadBytesPerSecond,
    NetworkReadingState State, string AdapterNames = "");
internal readonly record struct NetworkCounter(ulong Id, string Name, ulong Received, ulong Sent);

internal sealed class NetworkRateCalculator
{
    private Dictionary<ulong, NetworkCounter> _previous = new();
    private long _timestamp;

    public NetworkUsageReading Calculate(IReadOnlyList<NetworkCounter> counters, long timestamp)
    {
        var seconds = _timestamp == 0 ? 0 : Stopwatch.GetElapsedTime(_timestamp, timestamp).TotalSeconds;
        double received = 0, sent = 0;
        var pending = seconds <= 0;
        foreach (var counter in counters)
        {
            if (!_previous.TryGetValue(counter.Id, out var old) ||
                counter.Received < old.Received || counter.Sent < old.Sent)
            {
                pending = true;
                continue;
            }
            received += counter.Received - old.Received;
            sent += counter.Sent - old.Sent;
        }
        _previous = counters.ToDictionary(item => item.Id);
        _timestamp = timestamp;
        var names = string.Join(" + ", counters.Select(item => item.Name));
        if (counters.Count == 0)
            return new(null, null, NetworkReadingState.Disconnected);
        return pending ? new(null, null, NetworkReadingState.Initializing, names) :
            new(received / seconds, sent / seconds, NetworkReadingState.Available, names);
    }

    public void Reset() { _previous.Clear(); _timestamp = 0; }
}

/// <summary>Re-enumerates physical interfaces each sample, so connection changes need no polling service.</summary>
internal sealed class NetworkUsageSampler
{
    private readonly NetworkRateCalculator _rates = new();

    public NetworkUsageReading Read()
    {
        nint table = 0;
        try
        {
            if (GetIfTable2(out table) != 0 || table == 0)
                return Unavailable();
            var count = (uint)Marshal.ReadInt32(table);
            if (count > 4096)
                return Unavailable();
            var offset = (int)Marshal.OffsetOf<InterfaceTable>(nameof(InterfaceTable.FirstRow));
            var stride = Marshal.SizeOf<InterfaceRow>();
            var active = new List<NetworkCounter>();
            for (var index = 0; index < count; index++)
            {
                var row = Marshal.PtrToStructure<InterfaceRow>(table + offset + index * stride);
                // Hardware flag, Ethernet/Wi-Fi, operationally up, not a filter/tunnel/endpoint.
                if (IsActivePhysical(row.Type, row.Flags, row.OperStatus, row.MediaConnectState, row.TunnelType))
                    active.Add(new(row.Luid, row.Alias, row.InOctets, row.OutOctets));
            }
            return _rates.Calculate(active, Stopwatch.GetTimestamp());
        }
        catch (Exception error) when (error is ExternalException or DllNotFoundException or EntryPointNotFoundException)
        {
            return Unavailable();
        }
        finally { if (table != 0) FreeMibTable(table); }
    }

    internal static bool IsActivePhysical(uint type, byte flags, uint status, uint mediaState, uint tunnel) =>
        type is 6 or 71 && (flags & 1) != 0 && (flags & (2 | 16 | 128)) == 0 &&
        status == 1 && mediaState != 2 && tunnel == 0;

    private NetworkUsageReading Unavailable()
    {
        _rates.Reset();
        return new(null, null, NetworkReadingState.Unavailable);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceTable { public uint Count; public InterfaceRow FirstRow; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct InterfaceRow
    {
        public ulong Luid;
        public uint Index;
        public Guid Guid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Alias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Description;
        public uint PhysicalAddressLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PhysicalAddress;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PermanentPhysicalAddress;
        public uint Mtu, Type, TunnelType, MediaType, PhysicalMediumType, AccessType, DirectionType;
        public byte Flags;
        public uint OperStatus, AdminStatus, MediaConnectState;
        public Guid NetworkGuid;
        public uint ConnectionType;
        public ulong TransmitLinkSpeed, ReceiveLinkSpeed;
        public ulong InOctets, InUcastPkts, InNUcastPkts, InDiscards, InErrors, InUnknownProtos;
        public ulong InUcastOctets, InMulticastOctets, InBroadcastOctets;
        public ulong OutOctets, OutUcastPkts, OutNUcastPkts, OutDiscards, OutErrors;
        public ulong OutUcastOctets, OutMulticastOctets, OutBroadcastOctets, OutQLen;
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)] private static extern uint GetIfTable2(out nint table);
    [DllImport("iphlpapi.dll", ExactSpelling = true)] private static extern void FreeMibTable(nint table);
}
