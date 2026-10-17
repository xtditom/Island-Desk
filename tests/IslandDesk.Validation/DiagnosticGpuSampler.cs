using System.Runtime.InteropServices;
using System.IO;
using System.Text.Json;

// A diagnostic query restricted to this validation process. Never used by the app.
internal sealed class DiagnosticGpuSampler : IDisposable
{
    private nint _query, _counter, _buffer;
    private uint _capacity;
    private bool _baseline;
    private readonly string _prefix;
    public DiagnosticGpuSampler(int processId)
    {
        _prefix = $"pid_{processId}_";
        if (PdhOpenQueryW(null, 0, out _query) == 0)
            if (PdhAddEnglishCounterW(_query, @"\GPU Engine(*)\Utilization Percentage", 0, out _counter) != 0) _counter = 0;
    }
    public static int Probe(int processId, int seconds, string file)
    {
        using var sampler = new DiagnosticGpuSampler(processId); var values = new List<double>(); sampler.Read();
        for (var i = 0; i < seconds; i++) { Thread.Sleep(1000); if (sampler.Read() is { } value) values.Add(value); }
        File.WriteAllText(file, JsonSerializer.Serialize(values)); return 0;
    }
    public double? Read()
    {
        if (_counter == 0 || PdhCollectQueryData(_query) != 0) return null;
        if (!_baseline) { _baseline = true; return null; }
        uint length = 0;
        if (PdhGetFormattedCounterArrayW(_counter, 0x8200, ref length, out _, 0) != 0x800007D2 || length == 0 || length > 16 * 1024 * 1024) return null;
        if (length > _capacity) { if (_buffer != 0) Marshal.FreeHGlobal(_buffer); _buffer = Marshal.AllocHGlobal((int)length); _capacity = length; }
        length = _capacity;
        if (PdhGetFormattedCounterArrayW(_counter, 0x8200, ref length, out var count, _buffer) != 0) return null;
        var stride = Marshal.SizeOf<Item>(); if (count == 0 || count > _capacity / stride) return null;
        double? maximum = null;
        for (var i = 0; i < count; i++)
        {
            var item = Marshal.PtrToStructure<Item>(_buffer + i * stride);
            var name = Marshal.PtrToStringUni(item.Name);
            if (name is null || !name.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (item.Value.Status > 1 || !double.IsFinite(item.Value.Number) || item.Value.Number < 0 || item.Value.Number > 100.5) continue;
            maximum = Math.Max(maximum ?? 0, Math.Clamp(item.Value.Number, 0, 100));
        }
        return maximum;
    }
    public void Dispose() { if (_query != 0) PdhCloseQuery(_query); if (_buffer != 0) Marshal.FreeHGlobal(_buffer); _query = _buffer = _counter = 0; }
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct Value { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Number; }
    [StructLayout(LayoutKind.Sequential)] private struct Item { public nint Name; public Value Value; }
    [DllImport("pdh.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, nuint data, out nint query);
    [DllImport("pdh.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(nint query, string path, nuint data, out nint counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bytes, out uint count, nint buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(nint query);
}
