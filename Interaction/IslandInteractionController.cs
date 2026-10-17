using System.Windows.Threading;

namespace IslandDesk.Interaction;

// Independent policies supplied by the existing settings controller.
internal sealed record IslandInteractionOptions
{
    public bool ExpandOnHover { get; init; } = true;
    public bool ExpandOnClick { get; init; } = true;
    public bool CollapseOnHover { get; init; } = true;
    public bool CollapseOnClick { get; init; } = true;
    public TimeSpan CollapseDelay { get; init; } = TimeSpan.FromMilliseconds(250);
}

/// <summary>UI-thread interaction policy. The timer runs only while a collapse is pending.</summary>
internal sealed class IslandInteractionController : IDisposable
{
    private IslandInteractionOptions _options;
    private readonly Func<bool> _isExpanded;
    private readonly Action<bool> _setExpanded;
    private readonly DispatcherTimer _collapseTimer;
    private bool _active;
    private bool _pointerInside;
    private bool _suppressHoverUntilLeave;
    private bool _interacting;

    public IslandInteractionController(Dispatcher dispatcher, Func<bool> isExpanded,
        Action<bool> setExpanded, IslandInteractionOptions options)
    {
        _options = options;
        _isExpanded = isExpanded;
        _setExpanded = setExpanded;
        _collapseTimer = new DispatcherTimer(DispatcherPriority.Input, dispatcher)
        {
            Interval = SafeDelay(options.CollapseDelay)
        };
        _collapseTimer.Tick += CollapseTimer_Tick;
    }

    public void SetActive(bool active)
    {
        _active = active;
        if (active)
            return;
        _collapseTimer.Stop();
        _pointerInside = false;
        _suppressHoverUntilLeave = false;
        _interacting = false;
    }

    public void Configure(IslandInteractionOptions options)
    {
        _options = options;
        _collapseTimer.Stop();
        _collapseTimer.Interval = SafeDelay(options.CollapseDelay);
        ScheduleCollapse();
    }

    public void PointerEntered(bool allowExpansion = true)
    {
        if (!_active)
            return;
        _pointerInside = true;
        _collapseTimer.Stop();
        if (allowExpansion && _options.ExpandOnHover && !_suppressHoverUntilLeave)
            ChangeExpansion(true);
    }

    public void PointerLeft()
    {
        _pointerInside = false;
        _suppressHoverUntilLeave = false;
        ScheduleCollapse();
    }

    public void SetInteractionInProgress(bool interacting)
    {
        if (!_active) return;
        _interacting = interacting;
        if (interacting) _collapseTimer.Stop();
        else ScheduleCollapse();
    }

    private void ScheduleCollapse()
    {
        if (_active && !_pointerInside && !_interacting && _options.CollapseOnHover && _isExpanded() && !_collapseTimer.IsEnabled)
            _collapseTimer.Start();
    }

    public void Click(bool allowWhileInteracting = false)
    {
        var expand = !_isExpanded();
        if (!_active || (_interacting && !allowWhileInteracting) || (expand ? !_options.ExpandOnClick : !_options.CollapseOnClick))
            return;
        _collapseTimer.Stop();
        // A deliberate click-close stays closed until the next pointer visit.
        _suppressHoverUntilLeave = !expand && _pointerInside;
        ChangeExpansion(expand);
    }

    private void CollapseTimer_Tick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        if (_active && !_pointerInside && !_interacting && _options.CollapseOnHover)
            ChangeExpansion(false);
    }

    private void ChangeExpansion(bool expanded)
    {
        if (_isExpanded() != expanded)
            _setExpanded(expanded);
    }

    private static TimeSpan SafeDelay(TimeSpan delay) => TimeSpan.FromMilliseconds(
        double.IsFinite(delay.TotalMilliseconds) ? Math.Clamp(delay.TotalMilliseconds, 150, 600) : 250);

    public void Dispose()
    {
        SetActive(false);
        _collapseTimer.Tick -= CollapseTimer_Tick;
    }
}
