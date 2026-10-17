using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IslandDesk.Media;
using IslandDesk.Presentation;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using MediaPlayer = Windows.Media.Playback.MediaPlayer;

internal static class MediaValidation
{
    // Only this silent test provider receives commands; existing user sessions are never controlled.
    public static async Task VerifyAsync(string directory, Action<bool, string> check)
    {
        var wavePath = Path.Combine(directory, "silent-test.wav");
        using (var file = File.Create(wavePath))
        using (var writer = new BinaryWriter(file))
        {
            const int sampleBytes = 8000 * 2 * 2;
            writer.Write("RIFF"u8); writer.Write(36 + sampleBytes); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000);
            writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(sampleBytes); writer.Write(new byte[sampleBytes]);
        }
        var artPath = Path.Combine(directory, "artwork-test.png");
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(72, 92, 118)), null, new System.Windows.Rect(0, 0, 300, 300));
            context.DrawEllipse(new SolidColorBrush(Color.FromRgb(194, 206, 220)), null, new System.Windows.Point(150, 150), 78, 78);
        }
        var bitmap = new RenderTargetBitmap(300, 300, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(artPath)) png.Save(file);
        var reference = RandomAccessStreamReference.CreateFromFile(await StorageFile.GetFileFromPathAsync(artPath));
        var decoded = await MediaArtworkLoader.LoadAsync(reference, CancellationToken.None);
        check(decoded is { IsFrozen: true, PixelWidth: 144, PixelHeight: 144 }, "Artwork decodes off UI, is bounded and frozen");
        var vm = new MediaViewModel(_ => Task.FromResult(true));
        vm.SetActive(true); vm.SetArtworkVisible(true);
        var snapshot = new MediaSnapshot("Artwork", "Test", "Playing", true, Thumbnail: reference, SourceId: "test", ArtworkRevision: 1);
        vm.Update(snapshot);
        await WaitForAsync(() => vm.Artwork is not null);
        var cached = vm.Artwork;
        vm.Update(snapshot with { Status = "Paused", IsPlaying = false });
        check(ReferenceEquals(cached, vm.Artwork), "Playback-only events reuse artwork");
        vm.SetArtworkVisible(false);
        vm.Update(snapshot with { Title = "Hidden change", ArtworkRevision = 2 });
        check(ReferenceEquals(cached, vm.Artwork), "Hidden dashboard defers artwork decoding");
        vm.SetArtworkVisible(true);
        await WaitForAsync(() => vm.Artwork is not null && !ReferenceEquals(vm.Artwork, cached));
        check(true, "Visible dashboard loads latest artwork");
        vm.Dispose();

        using var source = MediaSource.CreateFromUri(new Uri(wavePath));
        using var player = new MediaPlayer { Volume = 0, IsLoopingEnabled = true, Source = source };
        player.CommandManager.IsEnabled = false;
        var transport = player.SystemMediaTransportControls;
        transport.IsEnabled = true;
        transport.IsPlayEnabled = true; transport.IsPauseEnabled = true;
        transport.IsPreviousEnabled = true; transport.IsNextEnabled = true;
        transport.DisplayUpdater.Type = MediaPlaybackType.Music;
        transport.DisplayUpdater.MusicProperties.Title = "IslandDesk silent validation";
        transport.DisplayUpdater.MusicProperties.Artist = "Test provider";
        transport.DisplayUpdater.Thumbnail = reference;
        transport.DisplayUpdater.Update();
        TaskCompletionSource<SystemMediaTransportControlsButton> pressed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            if (args.Button == SystemMediaTransportControlsButton.Play)
            { player.Play(); sender.PlaybackStatus = MediaPlaybackStatus.Playing; }
            if (args.Button == SystemMediaTransportControlsButton.Pause)
            { player.Pause(); sender.PlaybackStatus = MediaPlaybackStatus.Paused; }
            pressed.TrySetResult(args.Button);
        }
        transport.ButtonPressed += ButtonPressed;
        var sought = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        void PublishTimeline(TimeSpan position) => transport.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        { StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(2), MinSeekTime = TimeSpan.Zero, MaxSeekTime = TimeSpan.FromSeconds(2), Position = position });
        void PositionRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args)
        {
            player.PlaybackSession.Position = args.RequestedPlaybackPosition;
            PublishTimeline(args.RequestedPlaybackPosition); sought.TrySetResult(args.RequestedPlaybackPosition);
        }
        transport.PlaybackPositionChangeRequested += PositionRequested;
        using var cancellation = new CancellationTokenSource();
        var service = new MediaSessionService();
        MediaSnapshot? latest = null;
        var run = service.RunAsync(value => { Volatile.Write(ref latest, value); return Task.CompletedTask; }, cancellation.Token);
        try
        {
            player.Play(); transport.PlaybackStatus = MediaPlaybackStatus.Playing; PublishTimeline(TimeSpan.FromSeconds(.25));
            await WaitForAsync(() => Volatile.Read(ref latest)?.Title == "IslandDesk silent validation", 12);
            check(latest is { IsPlaying: true, Thumbnail: not null, CanToggle: true }, "Windows media session supplies playback and thumbnail");
            var ownSession = typeof(MediaSessionService).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service);
            check(ownSession is not null, "Own silent test session selected");
            // Pin only our own session, so a Windows current-session change can never target another app.
            var commands = new MediaSessionService();
            typeof(MediaSessionService).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(commands, ownSession);
            await WaitForAsync(() => Volatile.Read(ref latest)?.Timeline is { CanSeek: true });
            check(latest!.Timeline is { End.TotalSeconds: 2, Position.TotalSeconds: .25 }, "Native Windows timeline supplies duration, position and seek support");
            typeof(MediaSessionService).GetField("_metadataRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(commands, latest.ArtworkRevision);
            check(await commands.SeekAsync(new(latest.SourceId, latest.ArtworkRevision, TimeSpan.FromSeconds(1.25))), "Windows accepts seeking on the owned silent session");
            check(await sought.Task.WaitAsync(TimeSpan.FromSeconds(4)) == TimeSpan.FromSeconds(1.25), "Native provider receives the exact seek position");
            await WaitForAsync(() => Volatile.Read(ref latest)?.Timeline?.Position == TimeSpan.FromSeconds(1.25));
            check(true, "Native timeline change event confirms the seek without polling the media API");
            async Task Command(MediaAction action, SystemMediaTransportControlsButton expected)
            {
                pressed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                check(await commands.ExecuteAsync(action), $"Windows accepts {action}");
                check(await pressed.Task.WaitAsync(TimeSpan.FromSeconds(4)) == expected, $"Own provider receives {expected}");
            }
            await Command(MediaAction.Next, SystemMediaTransportControlsButton.Next);
            await Command(MediaAction.Previous, SystemMediaTransportControlsButton.Previous);
            await Command(MediaAction.Toggle, SystemMediaTransportControlsButton.Pause);
            await Command(MediaAction.Toggle, SystemMediaTransportControlsButton.Play);
            transport.DisplayUpdater.MusicProperties.Artist = "Updated through native event";
            transport.DisplayUpdater.Update();
            await WaitForAsync(() => Volatile.Read(ref latest)?.Artist == "Updated through native event");
            check(true, "Windows metadata events update without polling");
        }
        finally
        {
            cancellation.Cancel(); await run;
            transport.ButtonPressed -= ButtonPressed;
            transport.PlaybackPositionChangeRequested -= PositionRequested;
            transport.IsEnabled = false;
            player.Pause();
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, int seconds = 5)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Native media validation did not reach the expected state.");
            await Task.Delay(60); // Validation only; production services are event-driven.
        }
    }
}
