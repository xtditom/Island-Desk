using System.Runtime.InteropServices;
using System.IO;

namespace IslandDesk.Monitoring;

internal sealed record PhysicalMemoryUsage(ulong UsedBytes, ulong TotalBytes)
{
    public double Percent => 100.0 * UsedBytes / TotalBytes;
}

internal sealed record SystemUsageReading(
    double? CpuPercent, bool CpuPending, PhysicalMemoryUsage? Memory, GpuUsageReading? Gpu = null,
    NetworkUsageReading? Network = null, string? CpuName = null, SystemDiskUsage? Disk = null, bool DiskPending = false);

internal sealed record MonitoringSelection(bool Cpu = true, bool Gpu = true, bool Ram = true, bool Disk = true, bool Network = true)
{
    public bool Any => Cpu || Gpu || Ram || Disk || Network;
}

/// <summary>One cancellable sampling session; no dedicated thread or UI work.</summary>
internal sealed class SystemUsageMonitor
{
    public Task RunAsync(Func<SystemUsageReading, Task> publish, CancellationToken cancellationToken,
        bool includeDetails = true, MonitoringSelection? selection = null) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sensors = selection ?? new MonitoringSelection(Gpu: includeDetails, Disk: includeDetails, Network: includeDetails);
            // A fresh baseline prevents hidden/collapsed time from entering the next CPU reading.
            var sampler = new SystemUsageSampler();
            using var gpuSampler = sensors.Gpu ? new GpuUsageSampler() : null;
            var networkSampler = sensors.Network ? new NetworkUsageSampler() : null;
            var diskSampler = sensors.Disk ? new SystemDiskSampler() : null;
            var cpuName = sensors.Cpu ? CpuHardwareName.Value : null;
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reading = sampler.Read(sensors.Cpu, sensors.Ram);
                var gpu = gpuSampler?.Read(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                reading = reading with { Gpu = gpu, Network = networkSampler?.Read(), CpuName = cpuName, Disk = diskSampler?.Read() };
                cancellationToken.ThrowIfCancellationRequested();
                await publish(reading).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }, cancellationToken);

    private static readonly Lazy<string> CpuHardwareName = new(() =>
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Processor";
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        { return "Processor"; }
    });
}

internal sealed class SystemUsageSampler
{
    private CpuTimes? _previous;
    // GetSystemTimes covers only the calling thread's group on multi-group machines.
    // Avoid presenting a partial group as whole-machine CPU usage.
    private readonly bool _supportsCpu = GetActiveProcessorGroupCount() == 1;

    public SystemUsageReading Read(bool includeCpu = true, bool includeMemory = true)
    {
        double? cpu = null;
        var pending = false;
        if (includeCpu && _supportsCpu && GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var current = new CpuTimes(idle.Value, kernel.Value, user.Value);
            pending = _previous is null;
            if (_previous is { } previous)
            {
                cpu = CalculateCpuPercent(previous, current);
                pending = cpu is null && current == previous;
            }
            _previous = current;
        }
        else
        {
            _previous = null;
        }

        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        PhysicalMemoryUsage? physicalMemory = null;
        if (includeMemory && GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0 &&
            memory.AvailablePhysical <= memory.TotalPhysical)
            physicalMemory = new(memory.TotalPhysical - memory.AvailablePhysical, memory.TotalPhysical);

        return new(cpu, pending, physicalMemory);
    }

    internal static double? CalculateCpuPercent(CpuTimes previous, CpuTimes current)
    {
        if (current.Idle < previous.Idle || current.Kernel < previous.Kernel || current.User < previous.User)
            return null;

        // Kernel time already includes idle time; do not add idle to the denominator.
        var total = (current.Kernel - previous.Kernel) + (double)(current.User - previous.User);
        var idle = current.Idle - previous.Idle;
        if (total <= 0 || idle > total)
            return null;
        return Math.Clamp(100.0 * (total - idle) / total, 0, 100);
    }

    internal readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel,
        out NativeFileTime user);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);

    [DllImport("kernel32.dll")]
    private static extern ushort GetActiveProcessorGroupCount();
}
