using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace IslandDesk.Windowing;

/// <summary>WinEvents plus a two-second fallback for relevant foreground windows only.</summary>
internal sealed class FullscreenObserver : IDisposable
{
    private const uint Foreground = 3, MinimizeStart = 0x16, MinimizeEnd = 0x17, DesktopSwitch = 0x20, LocationChange = 0x800B;
    private readonly Dispatcher _dispatcher;
    private readonly nint _island;
    private readonly Action<bool> _changed;
    private readonly ForegroundWindowReader _reader = new();
    private readonly WinEventCallback _callback;
    private GCHandle _callbackRoot;
    private readonly DispatcherTimer _coalescingTimer, _fallbackTimer;
    private readonly CancellationTokenSource _cancellation = new();
    private nint _foregroundHook, _minimizeHook, _desktopHook, _locationHook, _trackedWindow;
    private uint _trackedProcess;
    private bool _pending, _reading, _disposed, _fullscreen;

    public FullscreenObserver(Dispatcher dispatcher, nint island, Action<bool> changed)
    {
        _dispatcher = dispatcher; _island = island; _changed = changed;
        _callback = WinEvent;
        _callbackRoot = GCHandle.Alloc(_callback);
        _coalescingTimer = new(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(60) };
        _fallbackTimer = new(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(2) };
        _coalescingTimer.Tick += Evaluate;
        _fallbackTimer.Tick += Fallback;
        // OUTOFCONTEXT (0): never inject a DLL or install an input hook.
        _foregroundHook = Hook(Foreground, Foreground);
        _minimizeHook = Hook(MinimizeStart, MinimizeEnd);
        _desktopHook = Hook(DesktopSwitch, DesktopSwitch);
        RequestCheck();
    }

    public void RequestCheck()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        _pending = true;
        if (!_reading && !_coalescingTimer.IsEnabled) _coalescingTimer.Start();
    }

    private nint Hook(uint first, uint last, uint process = 0) => SetWinEventHook(first, last, 0, _callback, process, 0, 2); // SKIPOWNPROCESS
    private void WinEvent(nint hook, uint kind, nint window, int objectId, int child, uint thread, uint time)
    {
        if (_disposed) return;
        if (kind == LocationChange && (window != _trackedWindow || objectId != 0 || child != 0)) return;
        RequestCheck();
    }
    private void Fallback(object? sender, EventArgs e) => RequestCheck();

    private async void Evaluate(object? sender, EventArgs e)
    {
        _coalescingTimer.Stop();
        if (_disposed || _reading || !_pending) return;
        _pending = false; _reading = true;
        var token = _cancellation.Token;
        try
        {
            var result = await Task.Run(() => _reader.Read(_island), token);
            if (_disposed) return;
            // A foreground switch during native queries must not hide for a stale window.
            if (result.Window != ForegroundWindowReader.GetForegroundWindow()) { _pending = true; return; }
            TrackLocation(result.TrackLocation ? result.Window : 0, result.ProcessId);
            var fallback = result.NeedsFallback || _foregroundHook == 0;
            if (fallback && !_fallbackTimer.IsEnabled) _fallbackTimer.Start();
            else if (!fallback) _fallbackTimer.Stop();
            if (_fullscreen != result.HideIsland)
            {
                _fullscreen = result.HideIsland;
                _changed(_fullscreen);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceWarning("Fullscreen detection unavailable: {0}", error.Message);
            if (!_disposed) _fallbackTimer.Start();
        }
        finally
        {
            _reading = false;
            if (!_disposed && _pending) _coalescingTimer.Start();
        }
    }

    private void TrackLocation(nint window, uint process)
    {
        if (_trackedWindow == window && _trackedProcess == process) return;
        if (_locationHook != 0) UnhookWinEvent(_locationHook);
        _locationHook = 0; _trackedWindow = window; _trackedProcess = process;
        if (window != 0 && process != 0) _locationHook = Hook(LocationChange, LocationChange, process);
    }

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        _coalescingTimer.Stop(); _fallbackTimer.Stop();
        _coalescingTimer.Tick -= Evaluate; _fallbackTimer.Tick -= Fallback;
        _cancellation.Cancel(); _cancellation.Dispose();
        foreach (var hook in new[] { _foregroundHook, _minimizeHook, _desktopHook, _locationHook })
            if (hook != 0) UnhookWinEvent(hook);
        if (_callbackRoot.IsAllocated) _callbackRoot.Free();
    }

    private delegate void WinEventCallback(nint hook, uint kind, nint window, int objectId, int child, uint thread, uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint first, uint last, nint module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(nint hook);
}
