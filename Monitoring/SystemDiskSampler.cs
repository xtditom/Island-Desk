using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace IslandDesk.Monitoring;

internal sealed record SystemDiskUsage(ulong UsedBytes, ulong TotalBytes, string Root)
{
    public double Percent => 100.0 * UsedBytes / TotalBytes;
}

internal sealed class SystemDiskSampler
{
    private static readonly Lazy<string?> SystemRoot = new(DetectSystemVolume);
    private readonly string? _root = SystemRoot.Value;
    public SystemDiskUsage? Read()
    {
        if (_root is null || GetDiskSpaceInformation(_root, out var space) != 0) return null;
        try
        {
            var unitBytes = checked((ulong)space.SectorsPerAllocationUnit * space.BytesPerSector);
            if (unitBytes == 0 || space.ActualTotalAllocationUnits == 0 ||
                space.ActualAvailableAllocationUnits > space.ActualTotalAllocationUnits) return null;
            return new(checked((space.ActualTotalAllocationUnits - space.ActualAvailableAllocationUnits) * unitBytes),
                checked(space.ActualTotalAllocationUnits * unitBytes), _root);
        }
        catch (OverflowException) { return null; }
    }

    private static string? DetectSystemVolume()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windows)) return null;
        var root = new StringBuilder(32768);
        // A Windows installation may use another drive letter or a mounted volume.
        if (GetVolumePathName(windows, root, root.Capacity)) return root.ToString();
        return Path.GetPathRoot(windows);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DiskSpaceInformation
    {
        public ulong ActualTotalAllocationUnits, ActualAvailableAllocationUnits, ActualPoolUnavailableAllocationUnits;
        public ulong CallerTotalAllocationUnits, CallerAvailableAllocationUnits, CallerPoolUnavailableAllocationUnits;
        public ulong UsedAllocationUnits, TotalReservedAllocationUnits, VolumeStorageReserveAllocationUnits;
        public ulong AvailableCommittedAllocationUnits, PoolAvailableAllocationUnits;
        public uint SectorsPerAllocationUnit, BytesPerSector;
    }
    [DllImport("kernel32.dll", EntryPoint = "GetDiskSpaceInformationW", CharSet = CharSet.Unicode)]
    private static extern int GetDiskSpaceInformation(string root, out DiskSpaceInformation information);
    [DllImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathName(string path, StringBuilder root, int length);
}
