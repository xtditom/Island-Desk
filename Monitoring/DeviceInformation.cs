using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace IslandDesk.Monitoring;

internal sealed record DeviceInformation(string ComputerName, string Motherboard);

/// <summary>One cached native SMBIOS read, off the UI thread. No WMI dependency or polling.</summary>
internal static class DeviceInformationReader
{
    private static readonly Lazy<Task<DeviceInformation>> Cached = new(() => Task.Run(Read));
    public static Task<DeviceInformation> GetAsync() => Cached.Value;

    private static DeviceInformation Read()
    {
        string name;
        try { name = Environment.MachineName; }
        catch (InvalidOperationException) { name = "This PC"; }
        try
        {
            const uint rsmb = 0x52534D42;
            var size = GetSystemFirmwareTable(rsmb, 0, null, 0);
            if (size is < 8 or > 4 * 1024 * 1024) return new(name, "Motherboard unavailable");
            var data = new byte[size];
            var written = GetSystemFirmwareTable(rsmb, 0, data, size);
            if (written < 8 || written > size) return new(name, "Motherboard unavailable");
            var length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4));
            if (length > written - 8) return new(name, "Motherboard unavailable");
            return new(name, ParseBaseboard(data.AsSpan(8, (int)length)) ?? "Motherboard unavailable");
        }
        catch (Exception e) when (e is ExternalException or EntryPointNotFoundException or DllNotFoundException)
        { return new(name, "Motherboard unavailable"); }
    }

    internal static string? ParseBaseboard(ReadOnlySpan<byte> table)
    {
        string? fallback = null;
        for (var offset = 0; offset + 4 <= table.Length;)
        {
            var type = table[offset]; var length = table[offset + 1];
            if (length < 4 || offset + length > table.Length) break;
            var stringsStart = offset + length;
            var end = stringsStart;
            while (end + 1 < table.Length && (table[end] != 0 || table[end + 1] != 0)) end++;
            if (end + 1 >= table.Length) break;
            if (type == 2 && length >= 8)
            {
                var strings = table[stringsStart..(end + 1)];
                var manufacturer = ReadString(strings, table[offset + 4]);
                var model = ReadString(strings, table[offset + 5]);
                var board = manufacturer is null ? model : model is null ? manufacturer :
                    model.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase) ? model : $"{manufacturer} · {model}";
                // Prefer an actual motherboard (SMBIOS board type 0x0A), rather than a module.
                if (board is not null && (length < 14 || table[offset + 13] == 0x0A)) return board;
                fallback ??= board;
            }
            if (type == 127) break;
            offset = end + 2;
        }
        return fallback;
    }

    private static string? ReadString(ReadOnlySpan<byte> strings, int index)
    {
        if (index == 0) return null;
        var start = 0;
        for (var current = 1; start < strings.Length; current++)
        {
            var length = strings[start..].IndexOf((byte)0);
            if (length < 0 || length == 0) return null;
            if (current == index)
            {
                var text = Encoding.UTF8.GetString(strings.Slice(start, length)).Trim();
                if (string.IsNullOrWhiteSpace(text) || text.Equals("Default string", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || text.Equals("Not applicable", StringComparison.OrdinalIgnoreCase)) return null;
                return new string(text.Where(c => !char.IsControl(c)).ToArray());
            }
            start += length + 1;
        }
        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetSystemFirmwareTable(uint provider, uint tableId, [Out] byte[]? buffer, uint size);
}
