using System.Globalization;
using System.Windows.Threading;
using IslandDesk.Infrastructure;

namespace IslandDesk.Presentation;

// Configuration is separate from the view; a settings page can supply this later.
internal sealed record ClockOptions(bool Use24Hour = true, bool ShowSeconds = false);

internal sealed class ClockViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _timer;
    private ClockOptions _options;
    private DateTime _now;
    private bool _timeVisible, _dateVisible;
    public string Time => _now.ToString(_options.Use24Hour
        ? (_options.ShowSeconds ? "HH:mm:ss" : "HH:mm")
        : (_options.ShowSeconds ? "h:mm:ss tt" : "h:mm tt"), CultureInfo.CurrentCulture);
    public string Weekday => _now.ToString("dddd", CultureInfo.CurrentCulture);
    public string Date => _now.ToString("d MMM, yyyy", CultureInfo.CurrentCulture);
    public bool IsRunning => _timer.IsEnabled;

    public ClockViewModel(Dispatcher dispatcher, ClockOptions? options = null)
    {
        _options = options ?? new();
        _timer = new(DispatcherPriority.Background, dispatcher);
        _timer.Tick += Tick;
        Refresh();
    }

    public void SetVisible(bool timeVisible, bool dateVisible = false)
    {
        _timer.Stop();
        _timeVisible = timeVisible; _dateVisible = dateVisible;
        if (!timeVisible && !dateVisible) return;
        Refresh();
        Schedule();
    }

    public void RefreshAndReschedule()
    {
        if (_timer.IsEnabled) SetVisible(_timeVisible, _dateVisible);
    }

    public void Configure(ClockOptions options)
    {
        if (_options == options) return;
        _options = options;
        Changed(nameof(Time));
        RefreshAndReschedule();
    }

    public void Refresh()
    {
        var oldTime = Time; var oldWeekday = Weekday; var oldDate = Date;
        _now = DateTime.Now;
        if (oldTime != Time) Changed(nameof(Time));
        if (oldWeekday != Weekday) Changed(nameof(Weekday));
        if (oldDate != Date) Changed(nameof(Date));
    }

    private void Tick(object? sender, EventArgs e) { _timer.Stop(); Refresh(); Schedule(); }
    private void Schedule()
    {
        var now = DateTime.Now;
        var next = !_timeVisible ? now.Date.AddDays(1) : _options.ShowSeconds
            ? now.AddSeconds(1).AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond))
            : now.AddMinutes(1).AddTicks(-(now.Ticks % TimeSpan.TicksPerMinute));
        _timer.Interval = next.ToUniversalTime() - now.ToUniversalTime() + TimeSpan.FromMilliseconds(20);
        _timer.Start();
    }
    public void Dispose() { _timer.Stop(); _timer.Tick -= Tick; }
}
