using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace IslandDesk.Audio;

internal sealed record VolumeSnapshot(double Percent, bool IsMuted, bool IsAvailable)
{
    public static VolumeSnapshot Unavailable { get; } = new(0, false, false);
}

/// <summary>Event-driven default render endpoint access, serialized on a thread-pool worker.</summary>
internal sealed class CoreAudioVolumeService
{
    private readonly Channel<bool> _changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true
    });
    private double _pendingVolume = double.NaN;
    private int _pendingMute = -1;
    private int _rebind = 1;
    private CancellationToken _cancellation;

    public void SetVolume(double percent)
    {
        if (_cancellation.IsCancellationRequested || !double.IsFinite(percent)) return;
        Interlocked.Exchange(ref _pendingVolume, Math.Clamp(percent, 0, 100));
        _changes.Writer.TryWrite(true);
    }

    public void SetMute(bool muted)
    {
        if (_cancellation.IsCancellationRequested) return;
        Interlocked.Exchange(ref _pendingMute, muted ? 1 : 0);
        _changes.Writer.TryWrite(true);
    }

    public Task RunAsync(Func<VolumeSnapshot, Task> publish, CancellationToken cancellationToken)
    {
        _cancellation = cancellationToken;
        return Task.Run(async () =>
        {
            IDeviceEnumerator? enumerator = null;
            IAudioDevice? device = null;
            IEndpointVolume? endpoint = null;
            var enumeratorRegistered = false;
            var endpointRegistered = false;
            var context = Guid.NewGuid();
            var volumeObserver = new VolumeObserver(() => _changes.Writer.TryWrite(true));
            var deviceObserver = new DeviceObserver(() =>
            {
                Interlocked.Exchange(ref _rebind, 1);
                _changes.Writer.TryWrite(true);
            });
            void ReleaseEndpoint()
            {
                if (endpoint is not null)
                {
                    if (endpointRegistered) endpoint.UnregisterControlChangeNotify(volumeObserver);
                    endpointRegistered = false;
                    Marshal.ReleaseComObject(endpoint);
                    endpoint = null;
                }
                if (device is not null) { Marshal.ReleaseComObject(device); device = null; }
            }
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                enumerator = (IDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(
                    new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!)!;
                Marshal.ThrowExceptionForHR(enumerator.RegisterEndpointNotificationCallback(deviceObserver));
                enumeratorRegistered = true;
                _changes.Writer.TryWrite(true);
                while (await _changes.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (_changes.Reader.TryRead(out _)) { }
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (Interlocked.Exchange(ref _rebind, 0) != 0 || endpoint is null)
                        {
                            ReleaseEndpoint();
                            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out device));
                            var iid = typeof(IEndpointVolume).GUID;
                            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, 0, out var instance)); // CLSCTX_ALL
                            endpoint = (IEndpointVolume)instance;
                            Marshal.ThrowExceptionForHR(endpoint.RegisterControlChangeNotify(volumeObserver));
                            endpointRegistered = true;
                        }
                        var volume = Interlocked.Exchange(ref _pendingVolume, double.NaN);
                        var mute = Interlocked.Exchange(ref _pendingMute, -1);
                        if (!double.IsNaN(volume))
                            Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar((float)(volume / 100), ref context));
                        if (mute >= 0)
                            Marshal.ThrowExceptionForHR(endpoint.SetMute(mute == 1, ref context));
                        Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var scalar));
                        Marshal.ThrowExceptionForHR(endpoint.GetMute(out var muted));
                        if (!float.IsFinite(scalar) || scalar < 0 || scalar > 1)
                            throw new COMException("Invalid endpoint volume reading.");
                        cancellationToken.ThrowIfCancellationRequested();
                        await publish(new(Math.Clamp(scalar * 100.0, 0, 100), muted, true)).ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is COMException or InvalidCastException)
                    {
                        ReleaseEndpoint();
                        Interlocked.Exchange(ref _pendingVolume, double.NaN);
                        Interlocked.Exchange(ref _pendingMute, -1);
                        if (!cancellationToken.IsCancellationRequested)
                            await publish(VolumeSnapshot.Unavailable).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception error)
            {
                System.Diagnostics.Trace.TraceWarning("Audio endpoint unavailable: {0}", error.Message);
                if (!cancellationToken.IsCancellationRequested)
                    await publish(VolumeSnapshot.Unavailable).ConfigureAwait(false);
            }
            finally
            {
                ReleaseEndpoint();
                if (enumerator is not null)
                {
                    if (enumeratorRegistered) enumerator.UnregisterEndpointNotificationCallback(deviceObserver);
                    Marshal.ReleaseComObject(enumerator);
                }
                _changes.Writer.TryComplete();
            }
        }, cancellationToken);
    }
}
