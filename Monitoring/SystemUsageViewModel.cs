using System.ComponentModel;
using System.Globalization;

namespace IslandDesk.Monitoring;

/// <summary>UI-thread presentation state, independent of native sampling.</summary>
internal sealed class SystemUsageViewModel : INotifyPropertyChanged
{
    private SystemUsageReading _reading = new(null, true, null, DiskPending: true);
    public event PropertyChangedEventHandler? PropertyChanged;

    public string CpuText => _reading.CpuPercent.HasValue ? FormatPercent(_reading.CpuPercent) :
        _reading.CpuPending ? "—" : "N/A";
    public double CpuProgress => _reading.CpuPercent ?? 0;
    public string CpuDetail => _reading.CpuPending ? "Sampling…" :
        _reading.CpuPercent.HasValue ? "System usage" : "Unavailable";
    public string CpuName => _reading.CpuName ?? CpuDetail;
    public string MemoryText => _reading.Memory is { } memory ? FormatPercent(memory.Percent) :
        _reading.CpuPending ? "—" : "N/A";
    public double MemoryProgress => _reading.Memory?.Percent ?? 0;
    public string MemoryDetail => _reading.Memory is { } memory
        ? string.Format(CultureInfo.CurrentCulture, "{0:0.0} / {1:0.0} GB",
            memory.UsedBytes / 1_073_741_824.0, memory.TotalBytes / 1_073_741_824.0)
        : "Unavailable";
    public string GpuText => _reading.Gpu?.Percent is { } percent ? FormatPercent(percent) :
        (_reading.Gpu?.IsPending ?? _reading.CpuPending) ? "—" : "N/A";
    public double GpuProgress => _reading.Gpu?.Percent ?? 0;
    public string DiskText => _reading.Disk is { } disk ? FormatPercent(disk.Percent) : _reading.DiskPending ? "—" : "N/A";
    public double DiskProgress => _reading.Disk?.Percent ?? 0;
    public string DiskDetail => _reading.Disk is { } disk ? string.Format(CultureInfo.CurrentCulture,
        "{0:0.0} / {1:0.0} GB", disk.UsedBytes / 1_073_741_824.0, disk.TotalBytes / 1_073_741_824.0) :
        _reading.DiskPending ? "Sampling…" : "Storage unavailable";
    public string DiskLabel => _reading.Disk is { } disk ? $"DISK · {disk.Root.TrimEnd('\\')}" : "SYSTEM DISK";
    public string GpuName => _reading.Gpu?.AdapterName ??
        ((_reading.Gpu?.IsPending ?? _reading.CpuPending) ? "Sampling…" : "GPU unavailable");
    public string DownloadText => FormatSpeed(_reading.Network?.DownloadBytesPerSecond, _reading.Network?.State);
    public string UploadText => FormatSpeed(_reading.Network?.UploadBytesPerSecond, _reading.Network?.State);
    public string NetworkDetail => _reading.Network?.State switch
    {
        NetworkReadingState.Available => _reading.Network.AdapterNames,
        NetworkReadingState.Initializing => "Sampling…",
        NetworkReadingState.Disconnected => "Disconnected",
        NetworkReadingState.Unavailable => "Network unavailable",
        _ => "Sampling…"
    };

    public void Reset() => Update(new(null, true, null, DiskPending: true));

    public void Update(SystemUsageReading reading)
    {
        var oldCpuText = CpuText;
        var oldCpuProgress = CpuProgress;
        var oldCpuDetail = CpuDetail;
        var oldCpuName = CpuName;
        var oldMemoryText = MemoryText;
        var oldMemoryProgress = MemoryProgress;
        var oldMemoryDetail = MemoryDetail;
        var oldGpuText = GpuText;
        var oldGpuProgress = GpuProgress;
        var oldGpuName = GpuName;
        var oldDownload = DownloadText;
        var oldUpload = UploadText;
        var oldNetworkDetail = NetworkDetail;
        var oldDiskText = DiskText; var oldDiskProgress = DiskProgress;
        var oldDiskDetail = DiskDetail; var oldDiskLabel = DiskLabel;
        _reading = reading;
        NotifyIfChanged(nameof(CpuText), oldCpuText, CpuText);
        NotifyIfChanged(nameof(CpuProgress), oldCpuProgress, CpuProgress);
        NotifyIfChanged(nameof(CpuDetail), oldCpuDetail, CpuDetail);
        NotifyIfChanged(nameof(CpuName), oldCpuName, CpuName);
        NotifyIfChanged(nameof(MemoryText), oldMemoryText, MemoryText);
        NotifyIfChanged(nameof(MemoryProgress), oldMemoryProgress, MemoryProgress);
        NotifyIfChanged(nameof(MemoryDetail), oldMemoryDetail, MemoryDetail);
        NotifyIfChanged(nameof(GpuText), oldGpuText, GpuText);
        NotifyIfChanged(nameof(GpuProgress), oldGpuProgress, GpuProgress);
        NotifyIfChanged(nameof(GpuName), oldGpuName, GpuName);
        NotifyIfChanged(nameof(DownloadText), oldDownload, DownloadText);
        NotifyIfChanged(nameof(UploadText), oldUpload, UploadText);
        NotifyIfChanged(nameof(NetworkDetail), oldNetworkDetail, NetworkDetail);
        NotifyIfChanged(nameof(DiskText), oldDiskText, DiskText);
        NotifyIfChanged(nameof(DiskProgress), oldDiskProgress, DiskProgress);
        NotifyIfChanged(nameof(DiskDetail), oldDiskDetail, DiskDetail);
        NotifyIfChanged(nameof(DiskLabel), oldDiskLabel, DiskLabel);
    }

    private void NotifyIfChanged<T>(string name, T previous, T current)
    {
        if (!EqualityComparer<T>.Default.Equals(previous, current))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static string FormatPercent(double? value) => value is { } percent
        ? percent.ToString("0", CultureInfo.CurrentCulture) + "%" : "—";

    internal static string FormatSpeed(double? bytesPerSecond, NetworkReadingState? state) =>
        bytesPerSecond is { } speed
            ? (speed >= 1_000_000 ? (speed / 1_000_000).ToString("0.0", CultureInfo.CurrentCulture) + " MB/s"
                : (speed / 1_000).ToString("0.0", CultureInfo.CurrentCulture) + " KB/s")
            : state == NetworkReadingState.Unavailable ? "N/A" : "—";
}
