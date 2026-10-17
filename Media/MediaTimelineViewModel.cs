using System.Diagnostics;
using IslandDesk.Infrastructure;

namespace IslandDesk.Media;

internal sealed record MediaTimelineSnapshot(TimeSpan Start, TimeSpan End, TimeSpan Position,
    DateTimeOffset UpdatedAt, TimeSpan MinSeek, TimeSpan MaxSeek, double Rate = 1, bool CanSeek = false);
internal sealed record MediaSeekRequest(string SourceId, long Revision, TimeSpan Position);

/// <summary>Local monotonic projection of event-driven Windows timeline data. No sampling timer.</summary>
internal sealed class MediaTimelineViewModel(Func<MediaSeekRequest, Task<bool>> seek) : ObservableObject
{
    private MediaTimelineSnapshot? _reading;
    private (string Source, string Title, long Revision) _identity;
    private double _anchor;
    private long _anchorTime;
    private DateTimeOffset _ignoreBefore;
    private bool _playing, _busy;
    public long Generation { get; private set; }
    public bool Available => _reading is { } r && r.End > r.Start && r.Start >= TimeSpan.Zero;
    public bool CanSeek => Available && !_busy && _reading is { CanSeek: true } r && r.MaxSeek > r.MinSeek && r.MaxSeek > r.Start && r.MinSeek < r.End;
    public double Duration => Available ? (_reading!.End - _reading.Start).TotalSeconds : 0;
    public double Rate => _playing && Available ? _reading!.Rate : 0;
    public double Position => Available ? Math.Clamp(_anchor + Stopwatch.GetElapsedTime(_anchorTime).TotalSeconds * Rate, 0, Duration) : 0;
    public string Status { get; private set; } = "Timeline unavailable";

    public void Update(MediaSnapshot snapshot, bool active)
    {
        var identity = (snapshot.SourceId, snapshot.Title, snapshot.ArtworkRevision);
        var reading = active ? snapshot.Timeline : null;
        var playing = active && snapshot.IsPlaying;
        if (identity == _identity && reading == _reading && playing == _playing) return;
        var current = Position;
        var sameTrack = identity == _identity;
        if (!sameTrack || (reading is null && _reading is not null)) { _ignoreBefore = default; _busy = false; Generation++; }
        if (sameTrack && reading is not null && reading.UpdatedAt < _ignoreBefore)
        {
            // Ignore stale positions after a seek, but never ignore pause/rate/control changes.
            _anchor = current; _anchorTime = Stopwatch.GetTimestamp(); _playing = playing;
            if (_reading is not null) _reading = _reading with { Rate = reading.Rate, CanSeek = reading.CanSeek,
                MinSeek = reading.MinSeek, MaxSeek = reading.MaxSeek };
            Changed(null); return;
        }
        var unchangedAnchor = sameTrack && reading is not null && _reading is not null && reading.Start == _reading.Start &&
            reading.End == _reading.End && reading.Position == _reading.Position && reading.UpdatedAt == _reading.UpdatedAt;
        _identity = identity; _reading = reading;
        _anchor = unchangedAnchor ? current : reading is null ? 0 : (reading.Position - reading.Start).TotalSeconds;
        if (!unchangedAnchor && playing && reading is not null && reading.UpdatedAt != default)
            _anchor += Math.Max(0, (DateTimeOffset.UtcNow - reading.UpdatedAt).TotalSeconds) * reading.Rate;
        _anchorTime = Stopwatch.GetTimestamp(); _playing = playing;
        Status = !Available ? "Timeline unavailable" : CanSeek ? "Drag or click to seek" : "Playback progress · seeking unavailable";
        Changed(null);
    }

    public async Task<bool> SeekAsync(double fraction)
    {
        if (!CanSeek || !double.IsFinite(fraction)) return false;
        var reading = _reading!; var identity = _identity;
        var target = reading.Start + TimeSpan.FromSeconds(Math.Clamp(fraction, 0, 1) * Duration);
        target = TimeSpan.FromTicks(Math.Clamp(target.Ticks, Math.Max(reading.Start.Ticks, reading.MinSeek.Ticks), Math.Min(reading.End.Ticks, reading.MaxSeek.Ticks)));
        _busy = true; Status = "Seeking…"; Changed(null);
        var requestedAt = DateTimeOffset.UtcNow;
        bool accepted;
        try { accepted = await seek(new(identity.Source, identity.Revision, target)); }
        catch (Exception e) { System.Diagnostics.Trace.TraceWarning("Seek failed: {0}", e.Message); accepted = false; }
        if (identity != _identity) return false;
        _busy = false;
        if (accepted)
        {
            // Preserve the preview until a fresh timeline event confirms/corrects it.
            _anchor = (target - reading.Start).TotalSeconds; _anchorTime = Stopwatch.GetTimestamp();
            _ignoreBefore = requestedAt;
        }
        Status = accepted ? "Drag or click to seek" : "This application could not seek";
        Changed(null); return accepted;
    }

    public static string FormatTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "—";
        var time = TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds / 2));
        return time.TotalHours >= 1 ? $"{(long)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(long)time.TotalMinutes}:{time.Seconds:00}";
    }
}
