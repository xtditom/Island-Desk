using IslandDesk.Audio;
using IslandDesk.Media;
using IslandDesk.Monitoring;
using IslandDesk.Infrastructure;

namespace IslandDesk.Presentation;

internal enum Dashboard { Main, System }

internal sealed class IslandViewModel(SystemUsageViewModel usage, MediaViewModel media, VolumeViewModel volume,
    ClockViewModel clock) : ObservableObject
{
    public SystemUsageViewModel Usage { get; } = usage;
    public BatteryViewModel Battery { get; } = new();
    public MediaViewModel Media { get; } = media;
    public VolumeViewModel Volume { get; } = volume;
    public ClockViewModel Clock { get; } = clock;
    public DeviceViewModel Device { get; } = new();
    private string _title = "IslandDesk";
    private Dashboard _dashboard;
    public string Title { get => _title; set { if (_title == value) return; _title = value; Changed(); } }
    public Dashboard Dashboard
    {
        get => _dashboard;
        set { if (_dashboard == value) return; _dashboard = value; Changed(); Changed(nameof(IsSystem)); }
    }
    public bool IsSystem => Dashboard == Dashboard.System;
}
