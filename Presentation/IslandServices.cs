using System.Windows.Threading;
using IslandDesk.Audio;
using IslandDesk.Media;
using IslandDesk.Monitoring;
using IslandDesk.Settings;

namespace IslandDesk.Presentation;

/// <summary>UI-owned lifetimes; native work runs in the existing asynchronous services.</summary>
internal sealed class IslandServices(Dispatcher dispatcher, SystemUsageViewModel usage,
    MediaViewModel media, VolumeViewModel volume, BatteryViewModel battery) : IDisposable
{
    private readonly SystemUsageMonitor _usage = new();
    private CancellationTokenSource? _hardwareCancellation, _mediaCancellation, _volumeCancellation;
    private MediaSessionService? _media;
    private CoreAudioVolumeService? _volume;
    private MonitoringSelection? _selection;
    private nint _nativeWindow;
    private CancellationTokenSource? _batteryCancellation;
    private BatteryStatusMonitor? _battery;

    public void AttachWindow(nint window) => _nativeWindow = window;
    public void PowerChanged(nint eventId, nint data) => _battery?.PowerChanged(eventId, data);

    public Task<bool> ExecuteMediaAsync(MediaAction action) => _media?.ExecuteAsync(action) ?? Task.FromResult(false);
    public Task<bool> SeekMediaAsync(MediaSeekRequest request) => _media?.SeekAsync(request) ?? Task.FromResult(false);
    public void SetVolume(double percent) => _volume?.SetVolume(percent);
    public void SetMute(bool muted) => _volume?.SetMute(muted);

    public void SetState(bool visible, bool expanded, bool systemDashboard, AppSettings? settings = null)
    {
        dispatcher.VerifyAccess();
        settings ??= new();
        var needsBattery = visible && expanded && systemDashboard && settings.Battery && _nativeWindow != 0;
        if (!needsBattery) { Cancel(ref _batteryCancellation); _battery = null; }
        if (needsBattery && _batteryCancellation is null)
        {
            var cancellation = _batteryCancellation = new();
            var service = _battery = new();
            battery.Reset();
            _ = RunAsync(cancellation, () => _batteryCancellation,
                () => { _batteryCancellation = null; _battery = null; },
                (publish, token) => service.RunAsync(_nativeWindow, reading => publish(() => battery.Update(reading)), token),
                () => battery.Update(BatteryReading.Unavailable));
        }
        var selection = new MonitoringSelection(settings.Cpu, settings.Gpu, settings.Ram, settings.Disk, settings.Network);
        var needsHardware = visible && expanded && systemDashboard && selection.Any;
        if (_selection != selection) { Cancel(ref _hardwareCancellation); _selection = selection; }
        if (!needsHardware)
            Cancel(ref _hardwareCancellation);
        if (needsHardware && _hardwareCancellation is null)
        {
            var cancellation = _hardwareCancellation = new();
            usage.Reset();
            _ = RunAsync(cancellation, () => _hardwareCancellation,
                () => _hardwareCancellation = null,
                (publish, token) => _usage.RunAsync(reading => publish(() => usage.Update(reading)), token, systemDashboard, selection),
                () => usage.Update(new(null, false, null, new(null, null), new(null, null, NetworkReadingState.Unavailable))));
        }

        // Playback events are needed even when collapsed to select clock or music form.
        if (!visible && _mediaCancellation is not null)
        {
            Cancel(ref _mediaCancellation); _media = null; media.SetActive(false);
        }
        if (visible && _mediaCancellation is null)
        {
            var cancellation = _mediaCancellation = new();
            var service = _media = new();
            media.SetActive(true);
            _ = RunAsync(cancellation, () => _mediaCancellation,
                () => { _mediaCancellation = null; _media = null; },
                (publish, token) => service.RunAsync(snapshot => publish(() => media.Update(snapshot)), token),
                () => media.Update(MediaSnapshot.Unavailable));
        }

        // Both dashboards accept wheel volume, using this same endpoint subscription.
        var needsVolume = visible && expanded;
        if (!needsVolume && _volumeCancellation is not null)
        {
            Cancel(ref _volumeCancellation); _volume = null; volume.SetActive(false);
        }
        if (needsVolume && _volumeCancellation is null)
        {
            var cancellation = _volumeCancellation = new();
            var service = _volume = new();
            volume.SetActive(true);
            _ = RunAsync(cancellation, () => _volumeCancellation,
                () => { _volumeCancellation = null; _volume = null; },
                (publish, token) => service.RunAsync(snapshot => publish(() => volume.Update(snapshot)), token),
                () => volume.Update(VolumeSnapshot.Unavailable));
        }
    }

    private async Task RunAsync(CancellationTokenSource cancellation, Func<CancellationTokenSource?> current,
        Action clear, Func<Func<Action, Task>, CancellationToken, Task> run, Action failed)
    {
        // Capture before disposal; callbacks and continuations may race cancellation.
        var token = cancellation.Token;
        async Task PublishAsync(Action update) => await dispatcher.InvokeAsync(() =>
        {
            if (ReferenceEquals(current(), cancellation) && !token.IsCancellationRequested) update();
        }, DispatcherPriority.Background, token);
        try { await run(PublishAsync, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceWarning("Widget service failed: {0}", error);
            if (ReferenceEquals(current(), cancellation) && !token.IsCancellationRequested) failed();
        }
        finally
        {
            if (ReferenceEquals(current(), cancellation)) clear();
            cancellation.Dispose();
        }
    }

    private static void Cancel(ref CancellationTokenSource? current)
    { var previous = current; current = null; previous?.Cancel(); }

    public void Dispose()
    {
        Cancel(ref _hardwareCancellation); Cancel(ref _mediaCancellation); Cancel(ref _volumeCancellation); Cancel(ref _batteryCancellation);
        _battery = null;
        _media = null; _volume = null;
        media.SetActive(false); volume.SetActive(false);
    }
}
