using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Data;
using System.Runtime.InteropServices;
using IslandDesk;
using IslandDesk.Controls;
using IslandDesk.Media;
using IslandDesk.Presentation;
using IslandDesk.Settings;
using IslandDesk.Views;
using IslandDesk.Interaction;

internal static class AdvancedMediaValidation
{
    private sealed class Startup : IStartupRegistration { public bool IsEnabled() => false; public void SetEnabled(bool enabled) { } }
    public static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var reading = new MediaTimelineSnapshot(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(210), TimeSpan.FromSeconds(30), now,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(210), 1, true);
        var snapshot = new MediaSnapshot("Test track", "Artist", "Playing", true, true, true, true, SourceId: "test", ArtworkRevision: 1, Timeline: reading);
        var seeks = new List<MediaSeekRequest>();
        var timeline = new MediaTimelineViewModel(request => { seeks.Add(request); return Task.FromResult(true); });
        timeline.Update(snapshot, true);
        check(timeline.Available && timeline.CanSeek && timeline.Duration == 200 && Math.Abs(timeline.Position - 20) < 1, "Timeline respects nonzero start and actual Windows position");
        var position = timeline.Position; await Task.Delay(150);
        check(timeline.Position > position + .1, "Timeline projects active playback locally without sensor polling");
        timeline.Update(snapshot with { IsPlaying = false }, true); position = timeline.Position; await Task.Delay(100);
        check(timeline.Position == position && timeline.Rate == 0, "Pause freezes the projected position");
        timeline.Update(snapshot, true); await Task.Delay(100);
        check(timeline.Position > position, "Resume continues from the frozen anchor");
        check(await timeline.SeekAsync(.5) && seeks.Single().Position == TimeSpan.FromSeconds(110), "Seeking uses the session's absolute 100 ns tick position");
        timeline.Update(snapshot with { IsPlaying = false }, true); position = timeline.Position; await Task.Delay(100);
        check(Math.Abs(position - 100) < 1 && timeline.Position == position, "Stale timeline after seeking never snaps back or ignores pause");
        timeline.Update(snapshot with { Title = "Next", ArtworkRevision = 2, Timeline = reading with { Position = TimeSpan.FromSeconds(10), UpdatedAt = DateTimeOffset.UtcNow } }, true);
        check(timeline.Position < 1, "Track change resets seek state and position");
        timeline.Update(snapshot with { Timeline = reading with { CanSeek = false } }, true);
        check(timeline.Available && !timeline.CanSeek && !await timeline.SeekAsync(.8) && seeks.Count == 1, "Unsupported seeking stays read-only and issues no command");
        timeline.Update(snapshot with { Timeline = null }, true);
        check(!timeline.Available && timeline.Duration == 0 && !timeline.CanSeek, "Missing timeline is unavailable rather than fabricated");
        check(MediaTimelineViewModel.FormatTime(3661) == "1:01:01" && MediaTimelineViewModel.FormatTime(65) == "1:05", "Timeline labels format long and short tracks");
        var pending = new TaskCompletionSource<bool>(); var delayed = new MediaTimelineViewModel(_ => pending.Task);
        delayed.Update(snapshot, true); var seekTask = delayed.SeekAsync(.7);
        check(!delayed.CanSeek && !await delayed.SeekAsync(.9), "In-flight seek requests cannot overlap");
        delayed.Update(snapshot with { Title = "Replacement", ArtworkRevision = 2, Timeline = reading with { Position = TimeSpan.FromSeconds(10), UpdatedAt = DateTimeOffset.UtcNow } }, true);
        pending.SetResult(true); check(!await seekTask && delayed.Position < 1, "Old seek completion never changes a replacement track");

        var pixels = Enumerable.Repeat(new byte[] { 40, 90, 180, 255 }, 64 * 64).SelectMany(pixel => pixel).ToArray();
        var bitmap = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 256); bitmap.Freeze();
        var accent = await Task.Run(() => ArtworkAccentExtractor.Extract(bitmap, CancellationToken.None));
        check(accent is { A: 255 } && accent.Value.R > accent.Value.B, "Worker extraction produces a legible artwork hue");
        var cached = await Task.Run(() => ArtworkAccentExtractor.Extract(bitmap, CancellationToken.None));
        check(cached == accent, "Repeated artwork reuses the bounded color cache");
        using (var palette = new IslandPalette())
        {
            var colorOptions = new AppSettings { AnimationStyle = TransitionStyle.Instant };
            palette.Apply(colorOptions, accent, true, 90, false, true, false);
            check(palette.TimelineBrush.Color == accent && palette.EqualizerBrush.Color == accent, "Artwork accent reaches both reusable music brushes");
            var tinted = ((SolidColorBrush)palette.Surface).Color;
            palette.Apply(colorOptions with { TintStrength = 0 }, accent, true, 90, false, true, false);
            check(((SolidColorBrush)palette.Surface).Color != tinted, "Tint strength controls a subtle solid-pill color independently of alpha");
            palette.Apply(colorOptions with { TimelineColor = TimelineColorSource.Custom, TimelineCustomColor = "#FFAA77", EqualizerColor = EqualizerColorSource.Custom, EqualizerCustomColor = "#AABBFF" }, null, false, 90, false, true);
            check(palette.TimelineBrush.Color.R > palette.TimelineBrush.Color.B && palette.EqualizerBrush.Color.B > palette.EqualizerBrush.Color.R, "Independent custom timeline and equalizer colors work without artwork");
            palette.Apply(colorOptions with { DynamicTimeline = false, DynamicEqualizer = false }, accent, true, 90, false, true);
            check(palette.TimelineBrush.Color == Color.FromRgb(213, 224, 216) && palette.EqualizerBrush.Color == palette.TimelineBrush.Color, "Dynamic-color toggles select the neutral fallback immediately");
            check(new AppSettings().DynamicVolume && System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!.DynamicVolume,
                "Dynamic volume defaults enabled, including existing JSON without the preference");
            palette.Apply(colorOptions, accent, false, 90, false, volumeVisible: true);
            check(palette.VolumeFillBrush.Color == accent && palette.VolumeThumbBrush.Color == accent,
                "Dynamic volume filled track and thumb share the cached artwork accent");
            var remaining = palette.VolumeTrackBrush.Color;
            check(remaining.A == 89 && remaining.R == accent!.Value.R && remaining.G == accent.Value.G && remaining.B == accent.Value.B,
                "Unfilled volume track uses the same hue at a subtle 35 percent alpha");
            palette.Apply(colorOptions with { DynamicVolume = false }, accent, false, 90, false);
            check(palette.VolumeFillBrush.Color == ((SolidColorBrush)Application.Current.FindResource("VolumeFill")).Color &&
                palette.VolumeTrackBrush.Color == ((SolidColorBrush)Application.Current.FindResource("VolumeTrack")).Color &&
                palette.VolumeThumbBrush.Color == ((SolidColorBrush)Application.Current.FindResource("VolumeThumb")).Color,
                "Disabling dynamic volume restores all three normal theme colors");
            palette.Apply(colorOptions with { DynamicAccent = false }, accent, false, 90, false);
            check(palette.VolumeFillBrush.Color == accent, "Volume color is controlled by its own toggle independently of other artwork accents");
            palette.Apply(colorOptions, null, false, 90, false);
            check(palette.VolumeFillBrush.Color == ArtworkAccentExtractor.EnsureContrast((Color)ColorConverter.ConvertFromString(colorOptions.FallbackAccent)),
                "Missing artwork uses the existing fallback accent");
            var smooth = colorOptions with { AnimationStyle = TransitionStyle.Smooth };
            palette.Apply(smooth, Colors.CornflowerBlue, false, 90, true, volumeVisible: true);
            check(palette.VolumeFillBrush.HasAnimatedProperties && palette.VolumeThumbBrush.HasAnimatedProperties && palette.VolumeTrackBrush.HasAnimatedProperties,
                "Song-accent changes use the existing brief color-animation system");
            await Task.Delay(350);
            check(!palette.VolumeFillBrush.HasAnimatedProperties && !palette.VolumeThumbBrush.HasAnimatedProperties && !palette.VolumeTrackBrush.HasAnimatedProperties &&
                palette.VolumeFillBrush.Color == ArtworkAccentExtractor.EnsureContrast(Colors.CornflowerBlue),
                "Volume color fades finish at the exact contrast-adjusted accent and release their clocks");
            palette.Apply(smooth, Colors.Orange, false, 90, true, volumeVisible: true);
            palette.Apply(smooth, Colors.Purple, false, 90, true, volumeVisible: false);
            check(!palette.VolumeFillBrush.HasAnimatedProperties && !palette.VolumeThumbBrush.HasAnimatedProperties && !palette.VolumeTrackBrush.HasAnimatedProperties,
                "Hidden volume changes cancel color animations immediately without idle work");
        }
        var normalized = new AppSettings { EqualizerBarCount = 999, EqualizerBarWidth = double.NaN, EqualizerSpeed = 99, TimelineThickness = 99, FallbackAccent = "invalid" }.Normalize();
        var backgroundDefaults = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!;
        check(!backgroundDefaults.DynamicBackground && backgroundDefaults.BackgroundTintStrength == 8 &&
            (backgroundDefaults with { BackgroundTintStrength = double.NaN }).Normalize().BackgroundTintStrength == 8 &&
            (backgroundDefaults with { BackgroundTintStrength = -1 }).Normalize().BackgroundTintStrength == 0 &&
            (backgroundDefaults with { BackgroundTintStrength = 100 }).Normalize().BackgroundTintStrength == 30,
            "Background tint defaults off at 8 percent and validates legacy, invalid and out-of-range preferences");
        using (var backgroundPalette = new IslandPalette())
        {
            var originalOptions = new AppSettings { AnimationStyle = TransitionStyle.Instant };
            backgroundPalette.Apply(originalOptions, Colors.Red, false, 60, false);
            var gradient = (LinearGradientBrush)backgroundPalette.Surface;
            var original = gradient.GradientStops.Select(stop => stop.Color).ToArray();
            var tintedOptions = originalOptions with { DynamicBackground = true, BackgroundTintStrength = 30 };
            backgroundPalette.Apply(tintedOptions, Colors.Red, false, 60, false);
            check(ReferenceEquals(gradient, backgroundPalette.Surface) && !original.SequenceEqual(gradient.GradientStops.Select(stop => stop.Color)) &&
                gradient.GradientStops.All(stop => stop.Color.A == 204), "Expanded tint reuses its gradient and preserves surface transparency");
            var tint = gradient.GradientStops.Select(stop => stop.Color).ToArray();
            backgroundPalette.Apply(tintedOptions with { BackgroundTintStrength = 0 }, Colors.Red, false, 60, false);
            check(original.SequenceEqual(gradient.GradientStops.Select(stop => stop.Color)), "Zero background tint restores the exact original gradient");
            backgroundPalette.Apply(tintedOptions with { FallbackAccent = "#FF0000" }, null, false, 60, false);
            check(tint.SequenceEqual(gradient.GradientStops.Select(stop => stop.Color)), "Missing artwork uses the existing fallback accent for the background");
            var smooth = tintedOptions with { AnimationStyle = TransitionStyle.Smooth };
            backgroundPalette.Apply(smooth, Colors.Blue, false, 60, true);
            check(gradient.GradientStops.Any(stop => stop.HasAnimatedProperties), "Song changes start the existing gradient-stop color clocks");
            var clocks = Field<System.Collections.IDictionary>(backgroundPalette, "_clocks");
            check(clocks.Values.Cast<object>().Select(value => (System.Windows.Media.Animation.AnimationClock)value.GetType().GetProperty("Clock")!.GetValue(value)!)
                .Where(clock => clock.Timeline.Duration.TimeSpan.TotalMilliseconds == 300).Count() == 4,
                "Gradient stops and the shared card surface use coordinated 300 ms color transitions");
            await Task.Delay(60); var before = gradient.GradientStops.Select(stop => stop.Color).ToArray();
            backgroundPalette.Apply(smooth, Colors.Lime, false, 60, true);
            check(before.SequenceEqual(gradient.GradientStops.Select(stop => stop.Color)), "Rapid song-accent changes retarget from the current presented colors without snapping");
            await Task.Delay(420);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); // Let the render tick dispatch completion before inspecting clocks.
            check(gradient.GradientStops.All(stop => !stop.HasAnimatedProperties) && !backgroundPalette.CardBrush.HasAnimatedProperties, "Completed background transitions release gradient and card color clocks");
            backgroundPalette.Apply(smooth, Colors.Blue, false, 60, true);
            backgroundPalette.Apply(smooth with { DynamicBackground = false }, Colors.Blue, false, 60, true);
            check(original.SequenceEqual(gradient.GradientStops.Select(stop => stop.Color)) && gradient.GradientStops.All(stop => !stop.HasAnimatedProperties),
                "Disabling background tint interrupts a running fade and restores the original immediately");
            backgroundPalette.Apply(originalOptions, Colors.Blue, true, 60, false, expanded: false);
            var pill = ((SolidColorBrush)backgroundPalette.Surface).Color;
            backgroundPalette.Apply(tintedOptions, Colors.Blue, true, 60, true, expanded: false);
            check(backgroundPalette.Surface is SolidColorBrush solid && solid.Color == pill && gradient.GradientStops.All(stop => !stop.HasAnimatedProperties),
                "Collapsed pill stays solid, with its independent music tint, and stops unused gradient transitions");
            backgroundPalette.Apply(tintedOptions with { BackgroundStyle = IslandBackgroundStyle.SolidDark }, Colors.Blue, false, 60, true);
            check(backgroundPalette.Surface is SolidColorBrush && gradient.GradientStops.All(stop => !stop.HasAnimatedProperties),
                "Expanded Solid appearance remains intact and does not animate an unused gradient");
        }
        check(normalized.EqualizerBarCount == 12 && normalized.EqualizerBarWidth == 2 && normalized.EqualizerSpeed == 2 && normalized.TimelineThickness == 6 && normalized.FallbackAccent == "#D5E0D8", "New numeric and color preferences validate safely");
        var path = Path.Combine(directory, "advanced-settings.json");
        using var settings = new SettingsController(Application.Current.Dispatcher, new PreferencesStore(path), new Startup());
        settings.AnimationStyle = TransitionStyle.Instant;
        var island = new MainWindow(false, settings);
        try
        {
            island.Show(); ((UIElement)island.FindName("IslandSurface")).IsHitTestVisible = false;
            Field<IslandInteractionController>(island, "_interaction").PointerEntered();
            Invoke(island, "SetExpanded", true); Field<IslandServices>(island, "_services").Dispose();
            var vm = Field<IslandViewModel>(island, "_viewModel"); vm.Media.SetActive(true); vm.Media.Update(snapshot with { Timeline = reading with { UpdatedAt = DateTimeOffset.UtcNow } });
            island.UpdateLayout(); await Task.Delay(80);
            var card = (MediaPlayerCard)((MainDashboard)island.FindName("MainDashboard")).MediaSection;
            var control = (PlaybackTimeline)card.FindName("Timeline"); var bar = Field<PlaybackBar>(control, "Bar");
            var info = (Border)card.FindName("InformationCard"); var transport = (Border)card.FindName("TransportCard");
            check(info.ActualHeight == transport.ActualHeight && transport.ActualWidth == 108 && control.ActualWidth == card.ActualWidth - 2 && island.ActualHeight - 16 == settings.Current.HomeHeight && info.BorderThickness.Left == 0 && transport.BorderThickness.Left == 0, "One outer media card contains aligned information, controls and an integrated full-width timeline");
            check(bar.IsAnimating && Field<DispatcherTimer>(control, "_labels").IsEnabled, "Visible playing timeline starts retained motion and one-second labels");
            var layoutPasses = 0; void Layout(object? sender, EventArgs args) => layoutPasses++;
            await Task.Delay(30); island.LayoutUpdated += Layout; await Task.Delay(180); island.LayoutUpdated -= Layout;
            check(layoutPasses == 0, "Playing progress geometry advances without per-frame WPF layout");
            var controlSeeks = 0;
            var seekModel = new MediaTimelineViewModel(_ => { controlSeeks++; return Task.FromResult(true); });
            seekModel.Update(snapshot, true); control.DataContext = seekModel;
            var activeWindow = GetActiveWindow();
            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent };
            Invoke(control, "Seek_Down", control, down);
            for (var i = 0; i < 20; i++) Invoke(control, "Seek_Move", control, new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseMoveEvent });
            check(Field<bool>(control, "_dragging") && controlSeeks == 0 && down.Handled, "Timeline capture previews many drag events without issuing seek calls");
            var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent };
            Invoke(control, "Seek_Up", control, up);
            check(controlSeeks == 1 && up.Handled && GetActiveWindow() == activeWindow && Field<bool>(island, "_isExpanded"), "Timeline release sends one seek without activation or island collapse");
            Invoke(control, "Seek_Down", control, new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
            seekModel.Update(snapshot with { Title = "Changed during drag", ArtworkRevision = 22 }, true);
            check(!Field<bool>(control, "_dragging") && controlSeeks == 1, "Track changes cancel an old timeline drag without seeking the replacement track");
            seekModel.Update(snapshot with { Timeline = reading with { CanSeek = false } }, true);
            Invoke(control, "Seek_Down", control, new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
            check(!Field<bool>(control, "_dragging") && controlSeeks == 1, "Read-only timeline does not capture or issue media commands");
            control.SetBinding(FrameworkElement.DataContextProperty, new Binding("Timeline"));
            vm.Volume.SetActive(true); vm.Volume.Update(new(50, false, true));
            var timelineScreen = control.PointToScreen(new Point(control.ActualWidth / 2, control.ActualHeight / 2));
            var packedPoint = (nint)(((int)timelineScreen.X & 0xFFFF) | (((int)timelineScreen.Y & 0xFFFF) << 16));
            SendMessage(new WindowInteropHelper(island).Handle, 0x020A, (nint)(120 << 16), packedPoint);
            check(vm.Volume.Percent == 53 && controlSeeks == 1 && GetActiveWindow() == activeWindow, "Wheel over the timeline changes only system volume by the existing step");
            var artPath = Path.Combine(directory, "advanced-artwork.png");
            var artEncoder = new PngBitmapEncoder(); artEncoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(artPath)) artEncoder.Save(file);
            var reference = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(await Windows.Storage.StorageFile.GetFileFromPathAsync(artPath));
            vm.Media.Update(snapshot with { Thumbnail = reference, ArtworkRevision = 2 });
            var deadline = Stopwatch.StartNew();
            while (vm.Media.Artwork is null && deadline.ElapsedMilliseconds < 3000) await Task.Delay(25);
            check(vm.Media.Artwork is not null && vm.Media.ArtworkAccent == accent, "Artwork loading publishes its cached accent to the visible media UI");
            var artwork = vm.Media.Artwork;
            var mainPalette = Field<IslandPalette>(island, "_palette");
            var volumeCard = ((MainDashboard)island.FindName("MainDashboard")).VolumeSection;
            var slider = (Slider)volumeCard.FindName("MasterVolumeSlider"); slider.ApplyTemplate();
            var sliderTrack = (System.Windows.Controls.Primitives.Track)slider.Template.FindName("PART_Track", slider);
            sliderTrack.Thumb.ApplyTemplate();
            var thumbSurface = (Border)VisualTreeHelper.GetChild(sliderTrack.Thumb, 0);
            check(ReferenceEquals(sliderTrack.DecreaseRepeatButton.Background, mainPalette.VolumeFillBrush) &&
                ReferenceEquals(sliderTrack.IncreaseRepeatButton.Background, mainPalette.VolumeTrackBrush) &&
                ReferenceEquals(thumbSurface.Background, mainPalette.VolumeThumbBrush) && mainPalette.VolumeFillBrush.Color == accent,
                "Actual master-volume template uses all three window-scoped accent brushes");
            await Dispatcher.Yield(DispatcherPriority.Render); island.UpdateLayout();
            Save(island, Path.Combine(directory, "volume-artwork.png"));
            settings.DynamicVolume = false; settings.DynamicVolume = true;
            check(ReferenceEquals(artwork, vm.Media.Artwork), "Toggling volume color reuses the decoded artwork without another image-processing pass");
            settings.HomeMedia = false; settings.MediaArtwork = false; settings.DynamicAccent = false;
            check(Field<bool>(vm.Media, "_artworkVisible") && mainPalette.VolumeFillBrush.Color == accent,
                "A visible volume-only card can request the existing accent pipeline when media/artwork cards are hidden");
            settings.HomeVolume = false;
            check(!Field<bool>(vm.Media, "_artworkVisible"), "Hiding volume stops its artwork demand when no other visible feature needs artwork");
            settings.HomeVolume = true; settings.HomeMedia = true; settings.MediaArtwork = true; settings.DynamicAccent = true;
            settings.Update(settings.Current with { HomeMedia = false, HomeVolume = false, MediaArtwork = false, DynamicAccent = false,
                DynamicVolume = false, DynamicBackground = true, BackgroundTintStrength = 8 });
            Invoke(island, "SelectDashboard", Dashboard.System);
            check(Field<bool>(vm.Media, "_artworkVisible"), "Expanded System can request cached artwork accents solely for its dynamic background");
            settings.BackgroundTintStrength = 0;
            check(!Field<bool>(vm.Media, "_artworkVisible"), "Zero background strength stops otherwise-unused artwork demand");
            settings.BackgroundTintStrength = 8; settings.BackgroundStyle = IslandBackgroundStyle.SolidDark;
            check(!Field<bool>(vm.Media, "_artworkVisible"), "Solid appearance does not request unused dynamic-gradient artwork");
            settings.BackgroundStyle = IslandBackgroundStyle.CharcoalGradient; Invoke(island, "SetExpanded", false);
            check(!Field<bool>(vm.Media, "_artworkVisible"), "Collapsed state suspends background-only artwork demand");
            settings.Update(settings.Current with { HomeMedia = true, HomeVolume = true, MediaArtwork = true, DynamicAccent = true,
                DynamicVolume = true, DynamicBackground = false });
            Invoke(island, "SetExpanded", true);
            await Dispatcher.Yield(DispatcherPriority.Render); island.UpdateLayout();
            Save(island, Path.Combine(directory, "advanced-home.png"));
            settings.MediaTimeline = false; island.UpdateLayout();
            check(!bar.IsAnimating && !Field<DispatcherTimer>(control, "_labels").IsEnabled && island.ActualHeight - 16 == settings.Current.HomeHeight, "Hiding the timeline releases all its clocks and reclaims its card gap");
            settings.MediaTimeline = true; settings.TimelineTimestamps = false; island.UpdateLayout();
            check(control.ActualHeight == 24 && ((FrameworkElement)control.FindName("Timestamps")).Visibility == Visibility.Collapsed, "Hiding timestamps gives a compact bar-only card");
            settings.MediaControls = false; settings.MediaArtwork = false; settings.MediaArtist = false; settings.MediaStatus = false; island.UpdateLayout();
            check(transport.Visibility == Visibility.Collapsed && info.ActualWidth == card.ActualWidth - 2 && ((FrameworkElement)card.FindName("AlbumArtwork")).Visibility == Visibility.Collapsed, "Media visibility controls reclaim horizontal space");
            settings.MediaControls = true; settings.MediaArtwork = true; settings.MediaArtist = true; settings.MediaStatus = true; settings.TimelineTimestamps = true;
            var surface = (IslandSurface)island.FindName("IslandSurface");
            check(surface.Background is LinearGradientBrush gradient && gradient.GradientStops.All(stop => stop.Color.A == 204) && island.Opacity == 1, "Charcoal gradient uses surface alpha while text remains opaque");
            settings.BackgroundStyle = IslandBackgroundStyle.SolidDark;
            check(surface.Background is SolidColorBrush solid && solid.Color.A == 204, "Solid Dark remains a functional live alternative");
            settings.BackgroundStyle = IslandBackgroundStyle.CharcoalGradient;
            settings.SurfaceCornerRadius = 16; settings.CardCornerRadius = 10;
            check(surface.CornerRadius.TopLeft == 16 && ((Border)card.FindName("PlayerCard")).CornerRadius.TopLeft == 10, "Both corner settings apply immediately to the unified outer card");
            Invoke(island, "SetExpanded", false); Field<IslandServices>(island, "_services").Dispose(); vm.Media.SetActive(true); vm.Media.Update(snapshot);
            var equalizer = (Equalizer)island.FindName("MusicEqualizer"); island.UpdateLayout();
            foreach (var style in Enum.GetValues<EqualizerStyle>())
            {
                settings.EqualizerStyle = style; Field<IslandServices>(island, "_services").Dispose();
                vm.Media.SetActive(true); vm.Media.Update(snapshot); island.UpdateLayout();
                var bars = Field<StackPanel>(equalizer, "Bars"); var origin = style == EqualizerStyle.Top ? 0 : style == EqualizerStyle.Middle ? .5 : 1;
                check(equalizer.IsAnimating && bars.Children.Cast<Border>().All(item => item.RenderTransformOrigin.Y == origin), $"{style} equalizer uses transform-only bars at the correct origin");
            }
            settings.EqualizerBarCount = 12; settings.EqualizerBarWidth = 4; settings.EqualizerBarSpacing = 4;
            check(equalizer.Width == 92 && ((ColumnDefinition)island.FindName("EqualizerColumn")).Width.Value == 92, "Largest supported equalizer fits without stretching the collapsed pill");
            settings.EqualizerEnabled = false;
            check(!equalizer.IsAnimating && equalizer.Visibility == Visibility.Collapsed, "Disabled equalizer removes all repeating clocks");
            settings.EqualizerEnabled = true; island.Hide();
            check(!equalizer.IsAnimating && !bar.IsAnimating && !Field<DispatcherTimer>(control, "_labels").IsEnabled, "Hidden widget stops equalizer and timeline work");
            var window = new SettingsWindow(settings); window.Show(); window.UpdateLayout();
            var categories = (TabControl)window.FindName("Categories");
            for (var index = 0; index < categories.Items.Count; index++)
            {
                categories.SelectedIndex = index; window.UpdateLayout(); await Task.Delay(30);
                Save(window, Path.Combine(directory, $"advanced-settings-{index}.png"));
            }
            check(categories.Items.Count == 8, "Three new categorized pages extend the existing Settings window");
            var musicColors = (StackPanel)((ScrollViewer)((TabItem)categories.Items[6]).Content).Content;
            var colorToggles = (StackPanel)((Border)musicColors.Children[3]).Child;
            var volumeToggle = (CheckBox)colorToggles.Children[2];
            check((string)((CheckBox)colorToggles.Children[1]).Content == "Dynamic frequency bar color" &&
                (string)volumeToggle.Content == "Dynamic volume slider color" && volumeToggle.IsChecked == true,
                "Enabled volume-color toggle appears immediately below Dynamic frequency bar color");
            volumeToggle.IsChecked = false;
            check(!settings.DynamicVolume, "The new Settings toggle applies directly through the existing controller");
            var backgroundControls = (StackPanel)((Border)musicColors.Children[4]).Child;
            var backgroundToggle = (CheckBox)backgroundControls.Children[0];
            var backgroundSlider = (Slider)((StackPanel)backgroundControls.Children[1]).Children[1];
            check((string)backgroundToggle.Content == "Dynamic background color" && backgroundToggle.IsChecked == false &&
                backgroundSlider.Minimum == 0 && backgroundSlider.Maximum == 30 && backgroundSlider.Value == 8,
                "Music Colors exposes the disabled background toggle and 0–30 percent slider at 8 percent");
            backgroundToggle.IsChecked = true; backgroundSlider.Value = 17;
            check(settings.DynamicBackground && settings.BackgroundTintStrength == 17, "Background controls apply immediately through the existing settings controller");
            window.CloseImmediately();
            settings.FallbackAccent = "#BADA55"; settings.EqualizerColor = EqualizerColorSource.Custom; settings.TimelineCustomColor = "#FFAA77";
            await settings.FlushAsync(); var loaded = await new PreferencesStore(path).LoadAsync();
            check(loaded.FallbackAccent == "#BADA55" && loaded.EqualizerColor == EqualizerColorSource.Custom && loaded.TimelineCustomColor == "#FFAA77" && loaded.EqualizerBarCount == 12 && !loaded.DynamicVolume &&
                loaded.DynamicBackground && loaded.BackgroundTintStrength == 17, "New volume/background preferences and tint strength survive a JSON reload");
            await settings.RestoreDefaultsAsync(); loaded = await new PreferencesStore(path).LoadAsync();
            check(loaded == new AppSettings(), "Restore Defaults resets and persists every new preference");
        }
        finally { island.Close(); await settings.FlushAsync(); File.Delete(path); }
    }
    private static void Save(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void Invoke(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);
    [DllImport("user32.dll")] private static extern nint GetActiveWindow();
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
}

