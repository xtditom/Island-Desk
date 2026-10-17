using System.Globalization;
using IslandDesk.Infrastructure;

namespace IslandDesk.Monitoring;

internal sealed class BatteryViewModel : ObservableObject
{
    private BatteryReading? _reading;
    public string Value => _reading is null ? "—" : _reading.Percent is { } percent
        ? percent.ToString(CultureInfo.CurrentCulture) + "%" : _reading.Presence == BatteryPresence.None ? "—" : "N/A";
    public double Progress => _reading?.Percent ?? 0;
    public bool HasPercentage => _reading?.Percent is not null;
    public string Status => _reading?.Presence switch
    {
        null => "Reading battery…",
        BatteryPresence.None => "No battery detected",
        BatteryPresence.Unavailable => "Battery status unavailable",
        _ => _reading.Flow switch
        {
            BatteryFlow.Charging => "Charging",
            BatteryFlow.Discharging => "Discharging",
            BatteryFlow.PluggedIn => "Plugged in · Not charging",
            BatteryFlow.Full => "Fully charged",
            _ => "Power status unavailable"
        }
    };
    public string Remaining => _reading?.RemainingSeconds is { } seconds ? FormatRemaining(seconds) : "";
    public void Reset() { if (_reading is null) return; _reading = null; Changed(null); }
    public void Update(BatteryReading reading) { if (_reading == reading) return; _reading = reading; Changed(null); }
    internal static string FormatRemaining(uint seconds)
    {
        var minutes = seconds / 60;
        return minutes == 0 ? "Less than 1 min remaining" : minutes < 60 ? $"{minutes} min remaining" :
            minutes % 60 == 0 ? $"{minutes / 60} hr remaining" : $"{minutes / 60} hr {minutes % 60} min remaining";
    }
}
