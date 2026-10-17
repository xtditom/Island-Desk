using System.Threading.Channels;
using System.Runtime.InteropServices;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Manager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace IslandDesk.Media;

internal enum MediaAction { Toggle, Previous, Next }
internal sealed record MediaSnapshot(string Title, string Artist, string Status, bool IsPlaying = false,
    bool CanToggle = false, bool CanPrevious = false, bool CanNext = false,
    IRandomAccessStreamReference? Thumbnail = null, string SourceId = "", long ArtworkRevision = 0,
    MediaTimelineSnapshot? Timeline = null)
{
    public static MediaSnapshot Empty { get; } = new("No active media", "", "No session");
    public static MediaSnapshot Unavailable { get; } = new("Media unavailable", "", "Unavailable");
}

/// <summary>One visible-session subscription. Events are coalesced; there is no polling timer.</summary>
internal sealed class MediaSessionService
{
    private Session? _session;
    private CancellationToken _cancellation;
    private long _metadataRevision;
    private readonly Channel<bool> _changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true
    });

    public Task RunAsync(Func<MediaSnapshot, Task> publish, CancellationToken cancellationToken)
    {
        _cancellation = cancellationToken;
        return Task.Run(async () =>
        {
            Manager? manager = null;
            Session? subscribed = null;
            MediaSnapshot? previous = null;
            void CurrentChanged(Manager sender, CurrentSessionChangedEventArgs args) => _changes.Writer.TryWrite(true);
            void SessionsChanged(Manager sender, SessionsChangedEventArgs args) => _changes.Writer.TryWrite(true);
            void PropertiesChanged(Session sender, MediaPropertiesChangedEventArgs args)
            { Interlocked.Increment(ref _metadataRevision); _changes.Writer.TryWrite(true); }
            void PlaybackChanged(Session sender, PlaybackInfoChangedEventArgs args) => _changes.Writer.TryWrite(true);
            void TimelineChanged(Session sender, TimelinePropertiesChangedEventArgs args) => _changes.Writer.TryWrite(true);
            void Unsubscribe()
            {
                if (subscribed is null) return;
                TryRemove(() => subscribed.MediaPropertiesChanged -= PropertiesChanged);
                TryRemove(() => subscribed.PlaybackInfoChanged -= PlaybackChanged);
                TryRemove(() => subscribed.TimelinePropertiesChanged -= TimelineChanged);
                subscribed = null;
            }
            void TryRemove(Action remove)
            {
                try { remove(); }
                catch (Exception error) when (error is ExternalException or InvalidOperationException)
                {
                    System.Diagnostics.Trace.TraceWarning("Media source closed during unsubscribe: {0}", error.Message);
                }
            }
            try
            {
                manager = await Manager.RequestAsync().AsTask(cancellationToken).ConfigureAwait(false);
                manager.CurrentSessionChanged += CurrentChanged;
                manager.SessionsChanged += SessionsChanged;
                _changes.Writer.TryWrite(true);
                while (await _changes.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (_changes.Reader.TryRead(out _)) { }
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var selected = SelectSession(manager);
                        if (!ReferenceEquals(selected, subscribed))
                        {
                            Interlocked.Increment(ref _metadataRevision);
                            Unsubscribe();
                            subscribed = selected;
                            _session = selected;
                            if (subscribed is not null)
                            {
                                subscribed.MediaPropertiesChanged += PropertiesChanged;
                                subscribed.PlaybackInfoChanged += PlaybackChanged;
                                subscribed.TimelinePropertiesChanged += TimelineChanged;
                            }
                        }
                        var snapshot = selected is null ? MediaSnapshot.Empty :
                            await ReadAsync(selected, Interlocked.Read(ref _metadataRevision), cancellationToken, previous).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        // A session switch while awaiting metadata must not publish stale song details.
                        if (!ReferenceEquals(selected, SelectSession(manager)) || (selected is not null && snapshot.ArtworkRevision != Interlocked.Read(ref _metadataRevision)))
                        {
                            _changes.Writer.TryWrite(true);
                            continue;
                        }
                        await publish(snapshot).ConfigureAwait(false);
                        previous = snapshot;
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        // App/session races disable controls, but keep the manager subscribed for recovery.
                        _session = null;
                        Unsubscribe();
                        if (!cancellationToken.IsCancellationRequested)
                            await publish(MediaSnapshot.Unavailable).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception error)
            {
                System.Diagnostics.Trace.TraceWarning("Media sessions unavailable: {0}", error.Message);
                if (!cancellationToken.IsCancellationRequested)
                    await publish(MediaSnapshot.Unavailable).ConfigureAwait(false);
            }
            finally
            {
                _session = null;
                Unsubscribe();
                if (manager is not null)
                {
                    TryRemove(() => manager.CurrentSessionChanged -= CurrentChanged);
                    TryRemove(() => manager.SessionsChanged -= SessionsChanged);
                }
                _changes.Writer.TryComplete();
            }
        }, cancellationToken);
    }

    private static Session? SelectSession(Manager manager)
    {
        var current = manager.GetCurrentSession();
        if (current is not null) return current;
        var sessions = manager.GetSessions();
        foreach (var session in sessions)
        {
            try
            {
                if (session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    return session;
            }
            catch (ExternalException) { }
        }
        return sessions.FirstOrDefault();
    }

    private static async Task<MediaSnapshot> ReadAsync(Session session, long revision, CancellationToken token, MediaSnapshot? previous)
    {
        var info = session.GetPlaybackInfo();
        var playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var controls = info.Controls;
        string title = "Media info unavailable", artist = "";
        IRandomAccessStreamReference? thumbnail = null;
        if (previous?.SourceId == session.SourceAppUserModelId && previous.ArtworkRevision == revision && previous.Title != "Media info unavailable")
        { title = previous.Title; artist = previous.Artist; thumbnail = previous.Thumbnail; }
        else try
        {
            var properties = await session.TryGetMediaPropertiesAsync().AsTask(token).ConfigureAwait(false);
            title = string.IsNullOrWhiteSpace(properties.Title) ? "Untitled media" : properties.Title;
            artist = properties.Artist ?? "";
            thumbnail = properties.Thumbnail;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            System.Diagnostics.Trace.TraceWarning("Media metadata unavailable: {0}", error.Message);
        }
        MediaTimelineSnapshot? timeline = null;
        try
        {
            var data = session.GetTimelineProperties();
            var rate = info.PlaybackRate ?? 1;
            if (!double.IsFinite(rate) || rate < 0 || rate > 16) rate = 1;
            if (data.EndTime > data.StartTime)
                timeline = new(data.StartTime, data.EndTime, data.Position, data.LastUpdatedTime,
                    data.MinSeekTime, data.MaxSeekTime, rate, controls.IsPlaybackPositionEnabled);
        }
        catch (Exception e) { System.Diagnostics.Trace.TraceWarning("Media timeline unavailable: {0}", e.Message); }
        return new(title, artist, info.PlaybackStatus.ToString(), playing,
            controls.IsPlayPauseToggleEnabled || (playing ? controls.IsPauseEnabled : controls.IsPlayEnabled),
            controls.IsPreviousEnabled, controls.IsNextEnabled, thumbnail, session.SourceAppUserModelId, revision, timeline);
    }

    public Task<bool> ExecuteAsync(MediaAction action) => Task.Run(async () =>
    {
        var session = _session;
        if (session is null || _cancellation.IsCancellationRequested) return false;
        try
        {
            var info = session.GetPlaybackInfo();
            var controls = info.Controls;
            var operation = action switch
            {
                MediaAction.Previous when controls.IsPreviousEnabled => session.TrySkipPreviousAsync(),
                MediaAction.Next when controls.IsNextEnabled => session.TrySkipNextAsync(),
                MediaAction.Toggle when controls.IsPlayPauseToggleEnabled => session.TryTogglePlayPauseAsync(),
                MediaAction.Toggle when info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && controls.IsPauseEnabled => session.TryPauseAsync(),
                MediaAction.Toggle when controls.IsPlayEnabled => session.TryPlayAsync(),
                _ => null
            };
            if (operation is null) return false;
            var accepted = await operation.AsTask(_cancellation).ConfigureAwait(false);
            _changes.Writer.TryWrite(true);
            return accepted;
        }
        catch (Exception error)
        {
            if (error is not OperationCanceledException)
                System.Diagnostics.Trace.TraceWarning("Media command failed: {0}", error.Message);
            return false;
        }
    });

    public Task<bool> SeekAsync(MediaSeekRequest request) => Task.Run(async () =>
    {
        var session = _session;
        if (session is null || _cancellation.IsCancellationRequested || request.Revision != Interlocked.Read(ref _metadataRevision)) return false;
        try
        {
            if (session.SourceAppUserModelId != request.SourceId || !session.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled) return false;
            var timeline = session.GetTimelineProperties();
            if (timeline.MaxSeekTime <= timeline.MinSeekTime) return false;
            var ticks = Math.Clamp(request.Position.Ticks, timeline.MinSeekTime.Ticks, timeline.MaxSeekTime.Ticks);
            if (!ReferenceEquals(session, _session) || request.Revision != Interlocked.Read(ref _metadataRevision)) return false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(4)); // Only a pending command has a deadline; no background timer.
            var accepted = await session.TryChangePlaybackPositionAsync(ticks).AsTask(timeout.Token).ConfigureAwait(false);
            if (ReferenceEquals(session, _session)) _changes.Writer.TryWrite(true);
            return accepted;
        }
        catch (Exception e) { System.Diagnostics.Trace.TraceWarning("Media seek unavailable: {0}", e.Message); return false; }
    });
}
