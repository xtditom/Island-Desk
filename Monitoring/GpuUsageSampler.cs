using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace IslandDesk.Monitoring;

internal sealed record GpuUsageReading(double? Percent, string? AdapterName, bool IsPending = false);
internal readonly record struct GpuEngineSample(string Instance, double Percent, uint Status);

/// <summary>One expanded-session PDH query; all calls run on the monitoring worker.</summary>
internal sealed class GpuUsageSampler : IDisposable
{
    private const uint MoreData = 0x800007D2;
    private const uint FormatDoubleUncapped = 0x00000200 | 0x00008000;
    private const uint MaximumBufferBytes = 16 * 1024 * 1024;
    private readonly GpuAdapter? _adapter;
    private SafePdhQueryHandle? _query;
    private nint _counter;
    private nint _buffer;
    private uint _bufferCapacity;
    private bool _hasBaseline;
    private bool _hasValidReading;
    private int _warmupAttempts;

    public GpuUsageSampler()
    {
        _adapter = GpuAdapterDiscovery.FindPrimaryHardwareAdapter();
        if (_adapter is null)
            return;
        try
        {
            if (PdhOpenQueryW(null, 0, out _query) != 0 || _query.IsInvalid)
                return;
            // One dynamic wildcard counter includes newly appearing process/engine instances.
            if (PdhAddEnglishCounterW(_query, @"\GPU Engine(*)\Utilization Percentage", 0, out _counter) != 0)
                _counter = 0;
        }
        catch (Exception error) when (error is ExternalException or DllNotFoundException or EntryPointNotFoundException)
        {
            System.Diagnostics.Trace.TraceWarning("GPU counters unavailable: {0}", error.Message);
            _counter = 0;
        }
    }

    public GpuUsageReading Read(CancellationToken cancellationToken)
    {
        double? percent = null;
        var pending = false;
        cancellationToken.ThrowIfCancellationRequested();
        if (_adapter is not null && _query is { IsInvalid: false } && _counter != 0)
        {
            if (PdhCollectQueryData(_query) != 0)
            {
                _hasBaseline = false;
                _hasValidReading = false;
                _warmupAttempts = 0;
            }
            else if (!_hasBaseline)
            {
                _hasBaseline = true; // Rate counters require two collections.
                pending = true;
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                percent = ReadEngineArray(cancellationToken, out var awaitingBaseline);
                _warmupAttempts++;
                if (percent.HasValue) _hasValidReading = true;
                // Newly appearing process counters may still need their own second sample.
                // Bound the grace period so a broken provider cannot remain "measuring" forever.
                pending = !_hasValidReading && awaitingBaseline && _warmupAttempts <= 2;
            }
        }
        return new(percent, _adapter?.Name, pending);
    }

    private double? ReadEngineArray(CancellationToken cancellationToken, out bool awaitingBaseline)
    {
        awaitingBaseline = false;
        // Instances can change between the sizing and data calls. Retry once with a fresh size.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint bytes = 0;
            if (PdhGetFormattedCounterArrayW(_counter, FormatDoubleUncapped, ref bytes, out _, 0) != MoreData ||
                bytes == 0 || bytes > MaximumBufferBytes)
                return null;
            if (bytes > _bufferCapacity)
            {
                var replacement = Marshal.AllocHGlobal((int)bytes);
                if (_buffer != 0)
                    Marshal.FreeHGlobal(_buffer);
                _buffer = replacement;
                _bufferCapacity = bytes;
            }
            bytes = _bufferCapacity;
            var status = PdhGetFormattedCounterArrayW(_counter, FormatDoubleUncapped, ref bytes, out var count, _buffer);
            if (status == MoreData)
                continue;
            var stride = Marshal.SizeOf<CounterValueItem>();
            if (status == 0xC0000BC6) awaitingBaseline = true; // PDH_INVALID_DATA (rate baseline).
            if (status != 0 || count == 0 || count > _bufferCapacity / stride)
                return null;

            var samples = new List<GpuEngineSample>((int)count);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = Marshal.PtrToStructure<CounterValueItem>(_buffer + index * stride);
                var name = Marshal.PtrToStringUni(item.Name);
                if (name is not null)
                    samples.Add(new(name, item.Value.Double, item.Value.Status));
            }
            return GpuEngineAggregation.Calculate(samples, _adapter!, out awaitingBaseline);
        }
        return null;
    }

    public void Dispose()
    {
        _query?.Dispose(); // Also releases the wildcard counter.
        _query = null;
        _counter = 0;
        if (_buffer != 0)
            Marshal.FreeHGlobal(_buffer);
        _buffer = 0;
        _bufferCapacity = 0;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct FormattedValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Double;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValueItem
    {
        public nint Name;
        public FormattedValue Value;
    }

    private sealed class SafePdhQueryHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafePdhQueryHandle() : base(true) { }
        protected override bool ReleaseHandle() => PdhCloseQuery(handle) == 0;
    }

    [DllImport("pdh.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? source, nuint userData, out SafePdhQueryHandle query);
    [DllImport("pdh.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(SafePdhQueryHandle query, string path, nuint userData, out nint counter);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(SafePdhQueryHandle query);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bytes,
        out uint count, nint buffer);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(nint query);
}

/// <summary>Sum only process contributions to the same physical engine, then take the maximum.</summary>
internal static partial class GpuEngineAggregation
{
    public static double? Calculate(IEnumerable<GpuEngineSample> samples, GpuAdapter adapter)
        => Calculate(samples, adapter, out _);

    public static double? Calculate(IEnumerable<GpuEngineSample> samples, GpuAdapter adapter, out bool awaitingBaseline)
    {
        awaitingBaseline = false;
        var engines = new Dictionary<(uint Physical, uint Engine), double>();
        var seen = new HashSet<(uint Process, uint Physical, uint Engine)>();
        var adapterToken = $"_luid_0x{adapter.LuidHigh:x8}_0x{adapter.LuidLow:x8}_";
        foreach (var sample in samples)
        {
            if (!sample.Instance.Contains(adapterToken, StringComparison.OrdinalIgnoreCase))
                continue;
            var match = InstancePattern().Match(sample.Instance);
            if (!match.Success ||
                !uint.TryParse(match.Groups["high"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var high) ||
                !uint.TryParse(match.Groups["low"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var low) ||
                high != adapter.LuidHigh || low != adapter.LuidLow)
                return null; // Unknown layout for this adapter cannot safely be partially aggregated.

            if (sample.Status == 0xC0000BBA) // PDH_CSTATUS_INVALID_DATA; not yet formattable.
            {
                awaitingBaseline = true;
                return null;
            }
            if (!uint.TryParse(match.Groups["pid"].Value, out var pid) ||
                !uint.TryParse(match.Groups["phys"].Value, out var physical) ||
                !uint.TryParse(match.Groups["eng"].Value, out var engine) ||
                sample.Status > 1 || !double.IsFinite(sample.Percent) || sample.Percent < 0 || sample.Percent > 100.5 ||
                !seen.Add((pid, physical, engine)))
                return null; // Ambiguous, invalid, or not-yet-sampled data is not an idle GPU.

            var key = (physical, engine);
            engines.TryGetValue(key, out var total);
            total += sample.Percent;
            if (total > 100.5)
                return null; // Do not hide inconsistent/duplicated provider data by capping a large sum.
            engines[key] = total;
        }
        return engines.Count == 0 ? null : Math.Clamp(engines.Values.Max(), 0, 100);
    }

    [GeneratedRegex(@"^pid_(?<pid>\d+)_luid_0x(?<high>[0-9a-f]{8})_0x(?<low>[0-9a-f]{8})_phys_(?<phys>\d+)_eng_(?<eng>\d+)_engtype_.+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstancePattern();
}
