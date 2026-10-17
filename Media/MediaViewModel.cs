using IslandDesk.Infrastructure;
using IslandDesk.Presentation;
using System.Windows.Media;

namespace IslandDesk.Media;

internal sealed class MediaViewModel : ObservableObject, IDisposable
{
    private MediaSnapshot _snapshot = MediaSnapshot.Empty;
    private bool _active;
    private bool _artworkVisible;
    private CancellationTokenSource? _artworkCancellation;
    private (string, string, string, long)? _artworkKey;
    private ImageSource? _artwork;
    private Color? _artworkAccent;
    public bool IsPlaying => _active && _snapshot.IsPlaying;
    public ImageSource? Artwork => _artwork;
    public Color? ArtworkAccent => _artworkAccent;
    public string Title => _snapshot.Title;
    public string Artist => _snapshot.Artist;
    public string Status => _snapshot.Status;
    public string ToggleGlyph => _snapshot.IsPlaying ? "\uE769" : "\uE768";
    public string ToggleLabel => _snapshot.IsPlaying ? "Pause" : "Play";
    public AsyncCommand ToggleCommand { get; }
    public AsyncCommand PreviousCommand { get; }
    public AsyncCommand NextCommand { get; }
    public MediaTimelineViewModel Timeline { get; }

    public MediaViewModel(Func<MediaAction, Task<bool>> execute, Func<MediaSeekRequest, Task<bool>>? seek = null)
    {
        Timeline = new(seek ?? (_ => Task.FromResult(false)));
        async Task Run(MediaAction action)
        {
            if (!await execute(action) && _active)
                Update(_snapshot with { Status = "Command unavailable" });
        }
        ToggleCommand = new(() => Run(MediaAction.Toggle), () => _active && _snapshot.CanToggle);
        PreviousCommand = new(() => Run(MediaAction.Previous), () => _active && _snapshot.CanPrevious);
        NextCommand = new(() => Run(MediaAction.Next), () => _active && _snapshot.CanNext);
    }

    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        if (active) _snapshot = new("Connecting media…", "", "Connecting");
        else { CancelArtwork(); _artworkKey = null; _artwork = null; _artworkAccent = null; }
        Refresh();
    }

    public void Update(MediaSnapshot snapshot)
    {
        if (_snapshot == snapshot) return;
        var previous = _snapshot;
        _snapshot = snapshot;
        // Timeline events are frequent. Notify the timeline itself without
        // invalidating every metadata, artwork and transport binding as well.
        Timeline.Update(snapshot, _active);
        if (previous.Title != snapshot.Title) Changed(nameof(Title));
        if (previous.Artist != snapshot.Artist) Changed(nameof(Artist));
        if (previous.Status != snapshot.Status) Changed(nameof(Status));
        if (previous.IsPlaying != snapshot.IsPlaying)
        {
            Changed(nameof(IsPlaying)); Changed(nameof(ToggleGlyph)); Changed(nameof(ToggleLabel));
        }
        if (previous.CanToggle != snapshot.CanToggle) ToggleCommand.Refresh();
        if (previous.CanPrevious != snapshot.CanPrevious) PreviousCommand.Refresh();
        if (previous.CanNext != snapshot.CanNext) NextCommand.Refresh();
        UpdateArtwork();
    }
    public void SetArtworkVisible(bool visible)
    {
        if (_artworkVisible == visible) return;
        _artworkVisible = visible;
        if (visible) UpdateArtwork();
        else if (_artworkCancellation is not null) { CancelArtwork(); _artworkKey = null; }
    }

    private void UpdateArtwork()
    {
        if (!_active || !_artworkVisible) return;
        var key = (_snapshot.SourceId, _snapshot.Title, _snapshot.Artist, _snapshot.ArtworkRevision);
        if (_artworkKey == key) return;
        CancelArtwork();
        _artworkKey = key;
        _artwork = null; Changed(nameof(Artwork));
        if (_snapshot.Thumbnail is null) { _artworkAccent = null; Changed(nameof(ArtworkAccent)); return; }
        var cancellation = _artworkCancellation = new CancellationTokenSource();
        _ = LoadArtworkAsync(_snapshot.Thumbnail, cancellation);
    }

    private async Task LoadArtworkAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference,
        CancellationTokenSource cancellation)
    {
        try
        {
            var image = await MediaArtworkLoader.LoadAsync(reference, cancellation.Token);
            var accent = image is null ? null : await Task.Run(() => ArtworkAccentExtractor.Extract(image, cancellation.Token), cancellation.Token);
            if (_artworkCancellation == cancellation && !cancellation.IsCancellationRequested)
            { _artwork = image; _artworkAccent = accent; Changed(nameof(Artwork)); Changed(nameof(ArtworkAccent)); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (_artworkCancellation == cancellation) _artworkCancellation = null;
            cancellation.Dispose();
        }
    }
    private void CancelArtwork()
    {
        var cancellation = _artworkCancellation;
        _artworkCancellation = null;
        cancellation?.Cancel();
    }
    public void Dispose() { _active = false; CancelArtwork(); }
    private void Refresh()
    {
        Timeline.Update(_snapshot, _active);
        Changed(null);
        ToggleCommand.Refresh(); PreviousCommand.Refresh(); NextCommand.Refresh();
    }
}
