using System.Windows.Threading;
using IslandDesk.Infrastructure;
using IslandDesk.Presentation;

namespace IslandDesk.Settings;

// UI-owned state; registry/file work is asynchronous. The debounce timer stops when idle.
internal sealed partial class SettingsController : ObservableObject, IDisposable
{
    private static readonly ExpansionMode[] SelectableExpansionModes = { ExpansionMode.Hover, ExpansionMode.Click };
    private readonly DispatcherTimer _saveTimer;
    private readonly PreferencesStore _store;
    private readonly IStartupRegistration _startup;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private int _revision, _savedRevision;
    private bool _startupBusy;
    private Task _startupTask = Task.CompletedTask;
    public AppSettings Current { get; private set; } = new();
    public event EventHandler? SettingsChanged;
    public string Status { get; private set; } = "Changes apply immediately";
    public bool CanChangeStartup => !_startupBusy;
    public Array ExpansionModes => SelectableExpansionModes;
    public Array AnimationStyles => Enum.GetValues<TransitionStyle>();
    public Array Alignments => Enum.GetValues<IslandAlignment>();
    private bool _displayInitialized;
    public int? MonitorRefreshRate { get; private set; }
    public FrameRateOption[] FrameRateOptions { get; private set; } = FrameRateOption.ForDisplay(null);
    public FrameRateOption SelectedFrameRate
    {
        get => FrameRateOptions.First(option => option.Rate == Current.AnimationFrameRate);
        set { if (value is { IsAvailable: true }) Update(Current with { AnimationFrameRate = value.Rate }); }
    }
    public int? EffectiveFrameRate => Current.AnimationFrameRate is { } rate && MonitorRefreshRate is { } hz && rate <= hz
        ? rate : MonitorRefreshRate;
    public string RefreshRateDescription => MonitorRefreshRate is { } hz
        ? $"Current display: {hz} Hz · Max follows its configured refresh rate."
        : "Display refresh rate unavailable · Max uses WPF's default pacing.";

    public void UpdateDisplayRefreshRate(int? refreshRate)
    {
        refreshRate = refreshRate is > 1 and <= 1000 ? refreshRate : null;
        if (_displayInitialized && MonitorRefreshRate == refreshRate) return;
        _displayInitialized = true;
        MonitorRefreshRate = refreshRate;
        FrameRateOptions = FrameRateOption.ForDisplay(refreshRate);
        if (Current.AnimationFrameRate is { } rate && (refreshRate is null || rate > refreshRate))
            Update(Current with { AnimationFrameRate = null });
        Changed(nameof(FrameRateOptions)); Changed(nameof(SelectedFrameRate));
        Changed(nameof(MonitorRefreshRate)); Changed(nameof(EffectiveFrameRate)); Changed(nameof(RefreshRateDescription));
    }

    public SettingsController(Dispatcher dispatcher, PreferencesStore? store = null, IStartupRegistration? startup = null)
    {
        _store = store ?? new(); _startup = startup ?? new StartupRegistration();
        _saveTimer = new(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(450) };
        _saveTimer.Tick += SaveTimer_Tick;
    }
    public async Task InitializeAsync(int? legacyTopOffset = null)
    {
        Current = await _store.LoadAsync(legacyTopOffset);
        try { Current = Current with { LaunchOnStartup = await Task.Run(_startup.IsEnabled) }; }
        catch (Exception e) { SetStatus("Windows startup status unavailable: " + e.Message); }
        Changed(null); SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Update(AppSettings value)
    {
        _saveTimer.Dispatcher.VerifyAccess();
        value = value.Normalize() with { Title = PreferencesStore.LimitTitleInput(value.Title) };
        if (_displayInitialized && value.AnimationFrameRate is { } rate && (MonitorRefreshRate is null || rate > MonitorRefreshRate))
            value = value with { AnimationFrameRate = null };
        if (Current == value) return;
        Current = value; _revision++;
        Changed(null); SettingsChanged?.Invoke(this, EventArgs.Empty);
        SetStatus("Saving…"); _saveTimer.Stop(); _saveTimer.Start();
    }
    private async void SaveTimer_Tick(object? sender, EventArgs e) => await FlushAsync();
    public async Task<bool> FlushAsync()
    {
        _saveTimer.Stop();
        await _startupTask;
        await _saveGate.WaitAsync();
        try
        {
            if (_revision == _savedRevision) return true;
            var revision = _revision;
            var saved = await _store.SaveAsync(Current);
            if (saved) _savedRevision = revision;
            SetStatus(saved ? (revision == _revision ? "All changes saved" : "Saving…") : "Couldn't save preferences. Check folder access and try again.");
            return saved;
        }
        finally { _saveGate.Release(); }
    }
    private void SetStatus(string text) { Status = text; Changed(nameof(Status)); }
    private async Task<bool> ChangeStartupAsync(bool enabled)
    {
        _startupBusy = true; Changed(nameof(CanChangeStartup));
        try { await Task.Run(() => _startup.SetEnabled(enabled)); Update(Current with { LaunchOnStartup = enabled }); return true; }
        catch (Exception e) { SetStatus("Couldn't change Windows startup: " + e.Message); Changed(nameof(LaunchOnStartup)); return false; }
        finally { _startupBusy = false; Changed(nameof(CanChangeStartup)); }
    }
    public async Task RestoreDefaultsAsync()
    {
        await _startupTask;
        var removed = await ChangeStartupAsync(false);
        Update(new AppSettings { LaunchOnStartup = Current.LaunchOnStartup });
        await FlushAsync();
        if (!removed) SetStatus("Defaults restored, but Windows startup couldn't be removed. Try switching it off again.");
    }
    public string Title { get => Current.Title; set => Update(Current with { Title = value }); }
    public bool LaunchOnStartup { get => Current.LaunchOnStartup; set { if (!_startupBusy && value != Current.LaunchOnStartup) _startupTask = ChangeStartupAsync(value); } }
    public ExpansionMode ExpansionMode { get => Current.ExpansionMode; set => Update(Current with { ExpansionMode = value }); }
    public ExpansionMode CollapseMode { get => Current.CollapseMode; set => Update(Current with { CollapseMode = value }); }
    public int CollapseDelayMilliseconds { get => Current.CollapseDelayMilliseconds; set => Update(Current with { CollapseDelayMilliseconds = value }); }
    public bool CanHoverCollapse => Current.CollapseMode != ExpansionMode.Click;
    public bool HideInFullscreen { get => Current.HideInFullscreen; set => Update(Current with { HideInFullscreen = value }); }
    public bool AlwaysOnTop { get => Current.AlwaysOnTop; set => Update(Current with { AlwaysOnTop = value }); }
    public double SurfaceOpacity { get => Current.SurfaceOpacity; set => Update(Current with { SurfaceOpacity = value }); }
    public double AnimationSpeed { get => Current.AnimationSpeed; set => Update(Current with { AnimationSpeed = value }); }
    public TransitionStyle AnimationStyle { get => Current.AnimationStyle; set => Update(Current with { AnimationStyle = value }); }
    public bool Use24Hour { get => Current.Use24Hour; set => Update(Current with { Use24Hour = value }); }
    public bool ShowSeconds { get => Current.ShowSeconds; set => Update(Current with { ShowSeconds = value }); }
    public IslandAlignment Alignment { get => Current.Alignment; set => Update(Current with { Alignment = value }); }
    public bool UseWorkingArea { get => Current.UseWorkingArea; set => Update(Current with { UseWorkingArea = value }); }
    public int XOffset { get => Current.XOffset; set => Update(Current with { XOffset = value }); }
    public int YOffset { get => Current.YOffset; set => Update(Current with { YOffset = value }); }
    public bool HomeClock { get => Current.HomeClock; set => Update(Current with { HomeClock = value }); }
    public bool HomeMedia { get => Current.HomeMedia; set => Update(Current with { HomeMedia = value }); }
    public bool HomeVolume { get => Current.HomeVolume; set => Update(Current with { HomeVolume = value }); }
    public bool QuickControlsTab { get => Current.QuickControlsTab; set => Update(Current with { QuickControlsTab = value }); }
    public void SetQuickControlOrder(IEnumerable<string> order)
    {
        var normalized = QuickControlLayout.Normalize(order);
        if (!Current.QuickControlOrder.SequenceEqual(normalized)) Update(Current with { QuickControlOrder = normalized });
    }
    public void ResetQuickControlLayout() => SetQuickControlOrder(QuickControlLayout.Normalize(null));
    public bool FileShelfTab { get => Current.FileShelfTab; set => Update(Current with { FileShelfTab = value }); }
    public bool Cpu { get => Current.Cpu; set => Update(Current with { Cpu = value }); }
    public bool Gpu { get => Current.Gpu; set => Update(Current with { Gpu = value }); }
    public bool Ram { get => Current.Ram; set => Update(Current with { Ram = value }); }
    public bool Disk { get => Current.Disk; set => Update(Current with { Disk = value }); }
    public bool Network { get => Current.Network; set => Update(Current with { Network = value }); }
    public bool Battery { get => Current.Battery; set => Update(Current with { Battery = value }); }
    public void Dispose() { _saveTimer.Stop(); _saveTimer.Tick -= SaveTimer_Tick; }
}
