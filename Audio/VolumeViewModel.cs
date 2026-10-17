using System.Globalization;
using IslandDesk.Infrastructure;

namespace IslandDesk.Audio;

internal sealed class VolumeViewModel : ObservableObject
{
    private readonly Action<double> _setVolume;
    private bool _active, _available, _initializing, _dragging, _muted;
    private double _percent;
    public bool IsAvailable => _active && _available;
    public bool IsMuted => _muted;
    public string MuteGlyph => _muted ? "\uE74F" : "\uE767";
    public string MuteLabel => _muted ? "Unmute" : "Mute";
    public string VolumeText => _initializing ? "—" : !_available ? "N/A" :
        _percent.ToString("0", CultureInfo.CurrentCulture) + "%";
    public AsyncCommand MuteCommand { get; }
    public double Percent
    {
        get => _percent;
        set
        {
            if (!IsAvailable || !double.IsFinite(value)) return;
            var next = Math.Clamp(value, 0, 100);
            if (Math.Abs(next - _percent) < 0.001) return;
            _percent = next;
            Changed(); Changed(nameof(VolumeText));
            _setVolume(next); // Coalesced native write, never a synchronous COM call.
        }
    }

    public bool AdjustFromWheel(int delta)
    {
        if (!IsAvailable || delta == 0) return false;
        // Preserve fractional notches from precision wheels; never round each event.
        Percent += delta * (3.0 / 120.0);
        return true;
    }

    public VolumeViewModel(Action<double> setVolume, Action<bool> setMute)
    {
        _setVolume = setVolume;
        MuteCommand = new(() =>
        {
            _muted = !_muted;
            setMute(_muted);
            Changed(null);
            return Task.CompletedTask;
        }, () => IsAvailable);
    }

    public void SetActive(bool active)
    {
        _active = active; _dragging = false;
        if (active) { _available = false; _initializing = true; }
        Changed(null); MuteCommand.Refresh();
    }

    public void SetDragging(bool dragging)
    {
        _dragging = dragging;
        if (!dragging && IsAvailable) _setVolume(_percent);
    }

    public void Update(VolumeSnapshot snapshot)
    {
        var percent = !_dragging && snapshot.IsAvailable ? snapshot.Percent : _percent;
        if (!_initializing && _available == snapshot.IsAvailable && _muted == snapshot.IsMuted && _percent == percent)
            return;
        _initializing = false;
        _available = snapshot.IsAvailable;
        _muted = snapshot.IsMuted;
        _percent = percent;
        Changed(null); MuteCommand.Refresh();
    }
}
