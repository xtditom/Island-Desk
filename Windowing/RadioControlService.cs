using System.Runtime.InteropServices;
using System.Windows.Threading;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace IslandDesk.Windowing;

internal sealed record RadioStatus(string Text, bool IsOn, bool CanToggle, string Detail);

// UI-owned snapshots, native StateChanged/device notifications, no polling.
internal sealed class RadioControlService(Dispatcher dispatcher) : IDisposable
{
    private readonly List<Radio> _radios = [];
    private DeviceWatcher? _watcher;
    private bool _active, _refreshing, _refreshAgain, _disposed;
    private int _generation;
    private RadioAccessStatus? _access;
    public event EventHandler? Changed;
    public RadioStatus Wifi => Snapshot(RadioKind.WiFi);
    public RadioStatus Bluetooth => Snapshot(RadioKind.Bluetooth);
    public void SetActive(bool active)
    {
        if (_disposed || _active == active) return;
        _active = active; _generation++;
        if (!active) { Stop(); return; }
        try
        {
            _watcher = DeviceInformation.CreateWatcher(Radio.GetDeviceSelector());
            _watcher.Added += DeviceAdded; _watcher.Removed += DeviceRemoved; _watcher.Updated += DeviceUpdated;
            _watcher.Start();
        }
        catch (Exception error) when (IsRadioError(error)) { StopWatcher(); }
        _ = RefreshAsync();
    }
    private RadioStatus Snapshot(RadioKind kind)
    {
        try
        {
            var radios = _radios.Where(radio => radio.Kind == kind).ToArray();
            if (radios.Length == 0) return new("Unavailable", false, false, "No accessible radio. Open Windows Settings to check hardware and permissions.");
            var states = radios.Select(radio => radio.State).ToArray();
            var on = states.All(state => state == RadioState.On);
            var mutable = states.Any(state => state is RadioState.On or RadioState.Off);
            var text = states.Distinct().Count() > 1 ? "Mixed" : on ? "On" : states[0] == RadioState.Off ? "Off" : "Unavailable";
            var allowed = _access is null or RadioAccessStatus.Allowed;
            return new(text, on, mutable && allowed, !allowed ? "Windows denied radio control. Use Windows Settings." :
                !mutable ? "The radio is disabled by hardware, policy, or its driver. Use Windows Settings." : "Toggle this radio. Right-click for Windows Settings.");
        }
        catch (Exception error) when (IsRadioError(error))
        { return new("Unavailable", false, false, "The radio is unavailable or was removed. Use Windows Settings."); }
    }
    public async Task<UtilityResult> ToggleAsync(RadioKind kind)
    {
        try
        {
            _access ??= await Radio.RequestAccessAsync(); // User click, UI thread, at most once per session.
            if (_access != RadioAccessStatus.Allowed)
                return new(false, "Windows denied direct radio control. Use the Windows Settings link below or the card's menu.");
            var radios = _radios.Where(radio => radio.Kind == kind).ToArray();
            if (radios.Length == 0) return new(false, "No radio is available. Use Windows Settings.");
            var state = radios.All(radio => radio.State == RadioState.On) ? RadioState.Off : RadioState.On;
            foreach (var radio in radios)
            {
                if (radio.State == state) continue;
                if (radio.State is not (RadioState.On or RadioState.Off) || await radio.SetStateAsync(state) != RadioAccessStatus.Allowed)
                    return new(false, "Windows or the radio hardware blocked the change. Use Windows Settings.");
            }
            return new(true, "Radio change requested; the card reflects the current Windows state.");
        }
        catch (Exception error) when (IsRadioError(error))
        { return new(false, "Radio control is unavailable. Use Windows Settings. " + error.Message); }
        finally { Changed?.Invoke(this, EventArgs.Empty); }
    }
    private async Task RefreshAsync()
    {
        if (_refreshing) { _refreshAgain = true; return; }
        if (!_active || _disposed) return;
        _refreshing = true;
        var generation = _generation;
        try
        {
            var radios = await Radio.GetRadiosAsync();
            if (!_active || _disposed || generation != _generation) return;
            foreach (var radio in _radios) radio.StateChanged -= StateChanged;
            _radios.Clear();
            foreach (var radio in radios.Where(radio => radio.Kind is RadioKind.WiFi or RadioKind.Bluetooth))
            { _radios.Add(radio); radio.StateChanged += StateChanged; }
        }
        catch (Exception error) when (IsRadioError(error))
        {
            foreach (var radio in _radios) radio.StateChanged -= StateChanged;
            _radios.Clear();
        }
        finally
        {
            _refreshing = false;
            if (_active && !_disposed) Changed?.Invoke(this, EventArgs.Empty);
            if (_refreshAgain) { _refreshAgain = false; await RefreshAsync(); }
        }
    }
    private void StateChanged(Radio sender, object args) => Post(() => Changed?.Invoke(this, EventArgs.Empty));
    private void DeviceAdded(DeviceWatcher sender, DeviceInformation args) => Post(() => _ = RefreshAsync());
    private void DeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate args) => Post(() => _ = RefreshAsync());
    private void DeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate args) => Post(() => _ = RefreshAsync());
    private void Post(Action action)
    {
        if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(new Action(() => { if (_active && !_disposed) action(); }));
    }
    private void StopWatcher()
    {
        if (_watcher is null) return;
        _watcher.Added -= DeviceAdded; _watcher.Removed -= DeviceRemoved; _watcher.Updated -= DeviceUpdated;
        if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) _watcher.Stop();
        _watcher = null;
    }
    private void Stop()
    {
        StopWatcher();
        foreach (var radio in _radios) radio.StateChanged -= StateChanged;
        _radios.Clear();
    }
    private static bool IsRadioError(Exception error) => error is COMException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException;
    public void Dispose() { _disposed = true; _active = false; _generation++; Stop(); Changed = null; }
}
