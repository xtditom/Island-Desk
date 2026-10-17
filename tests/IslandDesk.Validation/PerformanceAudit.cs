using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IslandDesk;
using IslandDesk.Audio;
using IslandDesk.Controls;
using IslandDesk.Media;
using IslandDesk.Monitoring;
using IslandDesk.Presentation;
using IslandDesk.Settings;
using IslandDesk.Themes;
using IslandDesk.Views;
using ThemeMode = IslandDesk.Settings.ThemeMode;

// Diagnostics only: sampling, forced collections and reflection never run in IslandDesk.
internal static class PerformanceAudit
{
    private sealed class Startup : IStartupRegistration { public bool IsEnabled() => false; public void SetEnabled(bool enabled) { } }
    private sealed record Resources(double PrivateMiB, double WorkingSetMiB, double ManagedMiB,
        int Handles, int Threads, uint Gdi, uint User);
    private sealed record Sample(string State, double Seconds, double CpuOneCorePercent, double AllocatedMiB,
        Resources Start, Resources End, int Layouts, int MediaEvents, double? BusiestProcessGpuEnginePercent,
        bool Hardware, bool Media, bool Volume, bool Clock, bool FrequencyBars, bool Transitions);

    public static async Task RunAsync(string directory)
    {
        using var settings = Settings(directory);
        var island = new MainWindow(false, settings);
        var rows = new List<Sample>();
        try
        {
            island.Show(); ((UIElement)island.FindName("IslandSurface")).IsHitTestVisible = false;
            var vm = Field<IslandViewModel>(island, "_viewModel");
            var services = Field<IslandServices>(island, "_services");
            await Task.Delay(3000);
            rows.Add(await Measure(island, "collapsed-native", 15));
            Invoke(island, "SetExpanded", true); await Task.Delay(3000);
            rows.Add(await Measure(island, "expanded-home-native", 15));
            Invoke(island, "SelectDashboard", Dashboard.System); await Task.Delay(3000);
            rows.Add(await Measure(island, "expanded-system-native", 15));
            Invoke(island, "SetExpanded", false); services.Dispose(); await Task.Delay(1000);
            vm.Media.SetActive(true); vm.Media.Update(Playing()); await Task.Delay(1000);
            rows.Add(await Measure(island, "collapsed-playing-synthetic-services-off", 15));
            settings.EqualizerEnabled = false; services.Dispose(); vm.Media.SetActive(true); vm.Media.Update(Playing()); await Task.Delay(2000);
            rows.Add(await Measure(island, "collapsed-playing-bars-disabled", 10));
            settings.EqualizerEnabled = true;
            Invoke(island, "SetExpanded", true); services.Dispose(); await Task.Delay(1000);
            vm.Media.SetActive(true); vm.Media.Update(Playing()); await Task.Delay(1000);
            rows.Add(await Measure(island, "home-playing-synthetic-services-off", 15));
            settings.MediaTimeline = false; services.Dispose(); vm.Media.SetActive(true); vm.Media.Update(Playing()); await Task.Delay(2000);
            rows.Add(await Measure(island, "home-playing-timeline-disabled", 10));
            settings.MediaTimeline = true; services.Dispose(); vm.Media.SetActive(true); vm.Media.Update(Playing()); await Task.Delay(1000);
            var micro = await MeasureNotifications(vm, island);
            var clockProbe = await ProbeDetachedClocks();
            Invoke(island, "FullscreenChanged", true); await Task.Delay(1000);
            rows.Add(await Measure(island, "fullscreen-hidden-production-hide-path", 15));
            var result = new { DateUtc = DateTime.UtcNow, Environment.ProcessorCount, WpfTier = RenderCapability.Tier >> 16,
                Dpi = VisualTreeHelper.GetDpi(island).PixelsPerDip, RefreshHz = settings.MonitorRefreshRate,
                Notes = "Release validation process including its static host window; GPU collection runs in a separate process. CPU is percent of one core. GPU is the maximum valid per-process engine sample averaged over the interval, not delivered FPS, DWM GPU time or whole-system GPU. Synthetic playback does not control other apps. Fullscreen observer is disabled; its trigger is injected into the production hide path. No forced GC during CPU/memory samples.",
                Samples = rows, NotificationBurst = micro, DetachedClockProbe = clockProbe };
            var path = Path.Combine(directory, "performance-audit.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Saved " + path);
        }
        finally { island.Close(); await settings.FlushAsync(); }
    }

    private static async Task<Sample> Measure(MainWindow island, string label, int seconds)
    {
        var services = Field<IslandServices>(island, "_services"); var media = Field<MediaViewModel>(island, "_mediaViewModel");
        using var process = Process.GetCurrentProcess();
        var gpuFile = Path.Combine(AppContext.BaseDirectory, "artifacts", "gpu-" + label + ".json");
        var probe = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { typeof(PerformanceAudit).Assembly.Location, "--gpu-probe", Environment.ProcessId.ToString(), seconds.ToString(), gpuFile }) probe.ArgumentList.Add(argument);
        using var gpuProcess = Process.Start(probe)!;
        var layouts = 0; var events = 0;
        void Layout(object? s, EventArgs e) => layouts++;
        void Changed(object? s, PropertyChangedEventArgs e) => events++;
        island.LayoutUpdated += Layout; media.PropertyChanged += Changed;
        var start = ReadResources(process); var cpu = process.TotalProcessorTime;
        var allocated = GC.GetTotalAllocatedBytes(); var watch = Stopwatch.StartNew();
        try
        {
            await Task.Delay(seconds * 1000);
            await gpuProcess.WaitForExitAsync();
            var gpuValues = File.Exists(gpuFile) ? JsonSerializer.Deserialize<double[]>(await File.ReadAllTextAsync(gpuFile)) ?? [] : [];
            process.Refresh();
            var row = new Sample(label, watch.Elapsed.TotalSeconds,
                (process.TotalProcessorTime - cpu).TotalSeconds / watch.Elapsed.TotalSeconds * 100,
                (GC.GetTotalAllocatedBytes() - allocated) / 1048576d, start, ReadResources(process), layouts, events,
                gpuValues.Length == 0 ? null : gpuValues.Average(),
                Field<object?>(services, "_hardwareCancellation") is not null,
                Field<object?>(services, "_mediaCancellation") is not null,
                Field<object?>(services, "_volumeCancellation") is not null,
                Field<ClockViewModel>(island, "_clock").IsRunning,
                ((Equalizer)island.FindName("MusicEqualizer")).IsAnimating,
                Field<IslandTransitions>(island, "_transitions").IsAnimating);
            Console.WriteLine($"PERF {label}: CPU {row.CpuOneCorePercent:F3}% one core; private {row.End.PrivateMiB:F2} MiB; WS {row.End.WorkingSetMiB:F2} MiB; alloc {row.AllocatedMiB:F3} MiB; handles {row.Start.Handles}->{row.End.Handles}; threads {row.End.Threads}; layouts {layouts}; GPU engine {(row.BusiestProcessGpuEnginePercent is { } activity ? activity.ToString("F3") + "%" : "unavailable")}");
            return row;
        }
        finally { island.LayoutUpdated -= Layout; media.PropertyChanged -= Changed; }
    }

    private static async Task<object> MeasureNotifications(IslandViewModel vm, MainWindow island)
    {
        var notifications = 0; var timelineNotifications = 0;
        void Changed(object? s, PropertyChangedEventArgs e) => notifications++;
        void TimelineChanged(object? s, PropertyChangedEventArgs e) => timelineNotifications++;
        var initial = Playing(); vm.Media.SetActive(true); vm.Media.Update(initial);
        vm.Media.PropertyChanged += Changed; vm.Media.Timeline.PropertyChanged += TimelineChanged;
        var allocated = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
        for (var i = 1; i <= 1000; i++)
            vm.Media.Update(initial with { Timeline = initial.Timeline! with { Position = TimeSpan.FromSeconds(i % 200), UpdatedAt = DateTimeOffset.UtcNow } });
        var mediaMs = watch.Elapsed.TotalMilliseconds; var mediaBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        vm.Media.PropertyChanged -= Changed; vm.Media.Timeline.PropertyChanged -= TimelineChanged;
        var volumeEvents = 0; void VolumeChanged(object? s, PropertyChangedEventArgs e) => volumeEvents++;
        vm.Volume.SetActive(true); var volume = new VolumeSnapshot(50, false, true); vm.Volume.Update(volume);
        vm.Volume.PropertyChanged += VolumeChanged;
        allocated = GC.GetAllocatedBytesForCurrentThread(); watch.Restart();
        for (var i = 0; i < 1000; i++) vm.Volume.Update(volume);
        var volumeMs = watch.Elapsed.TotalMilliseconds; var volumeBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        vm.Volume.PropertyChanged -= VolumeChanged;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Console.WriteLine($"EVENT BURST: 1000 timeline events => {notifications} media notifications, {timelineNotifications} timeline notifications; {mediaBytes} UI bytes, {mediaMs:F2} ms. 1000 duplicate volume events => {volumeEvents} notifications; {volumeBytes} bytes, {volumeMs:F2} ms.");
        return new { Count = 1000, MediaNotifications = notifications, TimelineNotifications = timelineNotifications, MediaUiBytes = mediaBytes, MediaUiMs = mediaMs,
            DuplicateVolumeNotifications = volumeEvents, VolumeUiBytes = volumeBytes, VolumeUiMs = volumeMs };
    }

    private sealed record ClockProbe(string State, bool TimeAdvanced, bool Attached);
    private sealed record ClockProbes(ClockProbe Playback, ClockProbe FrequencyBars);
    public static async Task RecordClockProbeAsync(string directory) =>
        await File.WriteAllTextAsync(Path.Combine(directory, "detached-clocks.json"), JsonSerializer.Serialize(await ProbeDetachedClocks()));
    private static async Task<ClockProbes> ProbeDetachedClocks()
    {
        var bar = new PlaybackBar(); bar.Measure(new Size(200, 12)); bar.Arrange(new Rect(0, 0, 200, 12));
        bar.SetProgress(0, 1, 60, 60);
        var fill = Field<RectangleGeometry>(bar, "_fill"); var clock = AppliedClock(fill, RectangleGeometry.RectProperty);
        await Task.Delay(100); bar.SetProgress(.5); var previous = clock.CurrentTime;
        await Task.Delay(250);
        var playback = new ClockProbe(clock.CurrentState.ToString(), clock.CurrentTime > previous, bar.IsAnimating);
        Console.WriteLine($"DETACHED playback clock: {playback}");
        var bars = new Equalizer { IsRunning = true };
        var host = new Window { Content = bars, Width = 100, Height = 60, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            host.Show(); await Task.Delay(100);
            var transform = Field<List<ScaleTransform>>(bars, "_bars")[0];
            var barClock = AppliedClock(transform, ScaleTransform.ScaleYProperty);
            bars.IsRunning = false; previous = barClock.CurrentTime; await Task.Delay(250);
            var frequency = new ClockProbe(barClock.CurrentState.ToString(), barClock.CurrentTime > previous, transform.HasAnimatedProperties);
            Console.WriteLine($"DETACHED frequency-bar clock: {frequency}");
            return new(playback, frequency);
        }
        finally { host.Close(); }
        // Holding the old clock makes this test independent of opportunistic GC.
    }

    public static async Task VerifyRegressionsAsync(Action<bool, string> check)
    {
        var probes = await ProbeDetachedClocks();
        check(probes.Playback is { State: "Stopped", TimeAdvanced: false, Attached: false },
            "Stopped/replaced playback clocks stop ticking even while retained by diagnostics");
        check(probes.FrequencyBars is { State: "Stopped", TimeAdvanced: false, Attached: false },
            "Stopped frequency-bar clocks stop ticking even while retained by diagnostics");
        var bar = new PlaybackBar(); bar.Measure(new Size(200, 12)); bar.Arrange(new Rect(0, 0, 200, 12));
        bar.SetProgress(.2, 1, .08, 60); await Task.Delay(250);
        check(!bar.IsAnimating && Field<RectangleGeometry>(bar, "_fill").Rect.Width == 200 && Field<object?>(bar, "_clock") is null,
            "Completed playback progress commits its exact endpoint and releases its owned clock");
        bar.SetProgress(0, 1, 60, 60); bar.SetProgress(.8, 1, .08, 60); await Task.Delay(250);
        check(!bar.IsAnimating && Field<RectangleGeometry>(bar, "_fill").Rect.Width == 200,
            "Rapid progress replacement cannot run a stale completion callback");
        using var media = new MediaViewModel(_ => Task.FromResult(true)); media.SetActive(true); var initial = Playing(); media.Update(initial);
        var changed = new List<string?>(); var timeline = 0;
        media.PropertyChanged += (_, e) => changed.Add(e.PropertyName); media.Timeline.PropertyChanged += (_, _) => timeline++;
        for (var i = 0; i < 1000; i++) media.Update(initial with { Timeline = initial.Timeline! with { Position = TimeSpan.FromSeconds(i % 200), UpdatedAt = DateTimeOffset.UtcNow } });
        check(changed.Count == 0 && timeline == 1000, "Timeline-only events update progress without invalidating metadata/artwork/transport bindings");
        media.Update(initial with { Title = "New song", Artist = "New artist", Status = "Paused", IsPlaying = false, CanToggle = true, CanNext = true, CanPrevious = true });
        check(media.Title == "New song" && media.Artist == "New artist" && !media.IsPlaying && media.Timeline.Rate == 0 &&
            media.ToggleCommand.CanExecute(null) && media.NextCommand.CanExecute(null) && media.PreviousCommand.CanExecute(null) &&
            new[] { nameof(media.Title), nameof(media.Artist), nameof(media.Status), nameof(media.IsPlaying), nameof(media.ToggleGlyph), nameof(media.ToggleLabel) }.All(changed.Contains),
            "Real metadata, playback, glyph and transport availability changes still publish immediately");
        changed.Clear(); media.SetActive(false);
        check(!media.ToggleCommand.CanExecute(null) && !media.Timeline.Available && !media.IsPlaying,
            "Deactivation disables transport and timeline immediately");
        var writes = 0; var volume = new VolumeViewModel(_ => writes++, _ => { }); volume.SetActive(true);
        var snapshot = new VolumeSnapshot(50, false, true); volume.Update(snapshot); var volumeEvents = 0;
        volume.PropertyChanged += (_, _) => volumeEvents++;
        for (var i = 0; i < 1000; i++) volume.Update(snapshot);
        check(volumeEvents == 0 && volume.Percent == 50, "Identical endpoint notifications do not invalidate volume bindings");
        volume.SetDragging(true); volume.Percent = 70; volume.Update(snapshot);
        check(volume.Percent == 70, "Duplicate endpoint reads never snap an active volume drag backward");
        volume.SetDragging(false); volume.Update(snapshot);
        check(volume.Percent == 50 && writes == 2, "The same endpoint value resynchronizes after a drag without duplicate native writes");
        volume.Update(new(50, true, true)); check(volume.IsMuted && volume.MuteLabel == "Unmute", "External mute changes remain immediate");
        volume.Update(VolumeSnapshot.Unavailable); check(!volume.IsAvailable && volume.VolumeText == "N/A", "Endpoint removal remains observable");
    }

    private static AnimationClock AppliedClock(Animatable target, DependencyProperty property)
    {
        var storageType = typeof(Animatable).Assembly.GetType("System.Windows.Media.Animation.AnimationStorage", true)!;
        var storage = storageType.GetMethod("GetStorage", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { target, property })!;
        var clocks = storageType.GetField("_animationClocks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(storage)!;
        return (AnimationClock)clocks.GetType().GetProperty("Item")!.GetValue(clocks, new object[] { 0 })!;
    }

    public static async Task VerifyStabilityAsync(string directory, Action<bool, string> check)
    {
        var errors = new ConcurrentQueue<string>();
        EventHandler<UnobservedTaskExceptionEventArgs> unobserved = (_, args) => errors.Enqueue(args.Exception.ToString());
        TaskScheduler.UnobservedTaskException += unobserved;
        try
        {
            await VerifyWindowStabilityAsync(directory, check); await SettleAndCollect();
            await File.WriteAllTextAsync(Path.Combine(directory, "unobserved-tasks.json"), JsonSerializer.Serialize(errors.ToArray()));
            check(errors.IsEmpty, "Stress and forced collections reveal no unobserved task exceptions");
        }
        finally { TaskScheduler.UnobservedTaskException -= unobserved; }
    }

    private static async Task VerifyWindowStabilityAsync(string directory, Action<bool, string> check)
    {
        using var settings = Settings(directory); var island = new MainWindow(false, settings);
        using var process = Process.GetCurrentProcess();
        var weakSettings = new List<WeakReference>(); var snapshots = new List<object>();
        try
        {
            island.Show(); ((UIElement)island.FindName("IslandSurface")).IsHitTestVisible = false;
            await Task.Delay(1500);
            // Warm JIT, WIC and both dashboard templates before retained-memory comparisons.
            Invoke(island, "SetExpanded", true); Invoke(island, "SelectDashboard", Dashboard.System); await Task.Delay(2500);
            Invoke(island, "SetExpanded", false); weakSettings.Add(await OpenCloseSettings(island)); await SettleAndCollect();
            var baseline = ReadResources(process); snapshots.Add(new { Stage = "warm", Resources = baseline });
            for (var batch = 0; batch < 4; batch++)
            {
                for (var i = 0; i < 15; i++)
                {
                    Invoke(island, "SetExpanded", true); await Task.Delay(17);
                    Invoke(island, "SelectDashboard", Dashboard.System); await Task.Delay(17);
                    Invoke(island, "SelectDashboard", Dashboard.Main); await Task.Delay(17);
                    Invoke(island, "SetExpanded", false); await Task.Delay(17);
                }
                for (var i = 0; i < 5; i++) weakSettings.Add(await OpenCloseSettings(island));
                settings.ThemeMode = batch % 2 == 0 ? ThemeMode.Light : ThemeMode.Dark;
                Invoke(island, "FullscreenChanged", true); Invoke(island, "FullscreenChanged", false);
                for (var i = 0; i < 30; i++) Invoke(island, "RequestPlacement");
                await SettleAndCollect();
                snapshots.Add(new { Stage = "batch-" + batch, Resources = ReadResources(process), RetainedSettings = weakSettings.Count(w => w.IsAlive) });
            }
            check(!Field<IslandTransitions>(island, "_transitions").IsAnimating && !Field<IslandMorph>(island, "_morph").HasAnimatedProperties,
                "Rapid expansion and tab reversals settle with no transition clocks");
            check(Field<object?>(Field<IslandServices>(island, "_services"), "_hardwareCancellation") is null,
                "Rapid tab switching leaves no hardware sampling session when collapsed");
            Console.WriteLine("Settings before explicit host focus: " + weakSettings.Count(w => w.IsAlive) + "; WPF focus=" + Keyboard.FocusedElement?.GetType().Name);
            var host = Application.Current.Windows.OfType<Window>().First(window => window != island && window is not SettingsWindow);
            host.Activate(); (host.Content as UIElement)?.Focus(); await SettleAndCollect();
            Console.WriteLine("Settings retained indexes: " + string.Join(",", weakSettings.Select((weak, index) => (weak, index)).Where(item => item.weak.IsAlive).Select(item => item.index)));
            await File.WriteAllTextAsync(Path.Combine(directory, "stability-checkpoints.json"), JsonSerializer.Serialize(snapshots, new JsonSerializerOptions { WriteIndented = true }));
            // WPF can retain its latest window/input graph. Check accumulation
            // separately from that single retained graph and record both counts.
            check(weakSettings.Take(weakSettings.Count - 1).All(w => !w.IsAlive),
                "Earlier Settings windows are collectible; repeated openings do not accumulate window graphs");
            Invoke(island, "SetExpanded", true); var vm = Field<IslandViewModel>(island, "_viewModel");
            Field<IslandServices>(island, "_services").Dispose(); await Task.Delay(300); vm.Media.SetActive(true); vm.Media.SetArtworkVisible(true);
            var references = new[] { await Artwork(directory, "red", Color.FromRgb(220, 50, 70)), await Artwork(directory, "blue", Color.FromRgb(50, 100, 220)) };
            for (var i = 0; i < 40; i++) { vm.Media.Update(Playing() with { Title = "track-" + i, Thumbnail = references[i % 2], ArtworkRevision = i }); await Task.Delay(15); }
            for (var i = 0; i < 100 && (vm.Media.Artwork is null || Field<object?>(vm.Media, "_artworkCancellation") is not null); i++) await Task.Delay(30);
            check(vm.Media.Title == "track-39" && vm.Media.Artwork is not null && vm.Media.ArtworkAccent == ArtworkAccentExtractor.EnsureContrast(Color.FromRgb(50, 100, 220)),
                "Rapid artwork changes cancel stale work and publish the final track/color");
            var cache = (System.Collections.IDictionary)typeof(ArtworkAccentExtractor).GetField("Cache", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            lock (cache) check(cache.Count <= 32, "Artwork accent cache stays bounded during repeated track changes");
            Invoke(island, "FullscreenChanged", true); await SettleAndCollect();
            var hiddenStart = ReadResources(process);
            for (var i = 0; i < 12; i++)
            {
                await Task.Delay(5000);
                snapshots.Add(new { Stage = "hidden-soak-" + (i + 1) * 5, Resources = ReadResources(process) });
            }
            await SettleAndCollect(); var end = ReadResources(process);
            check(!Field<ClockViewModel>(island, "_clock").IsRunning && !((Equalizer)island.FindName("MusicEqualizer")).IsAnimating,
                "Fullscreen-hidden soak leaves clock and frequency bars stopped");
            var services = Field<IslandServices>(island, "_services");
            check(new[] { "_hardwareCancellation", "_mediaCancellation", "_volumeCancellation", "_batteryCancellation" }.All(name => Field<object?>(services, name) is null),
                "Fullscreen-hidden soak leaves every native service canceled");
            check(end.Gdi <= hiddenStart.Gdi + 2 && end.User <= hiddenStart.User + 2 && end.Handles <= hiddenStart.Handles + 8,
                "Hidden soak does not accumulate native handles or GDI/USER objects");
            Console.WriteLine($"STABILITY retained managed {baseline.ManagedMiB:F2}->{end.ManagedMiB:F2} MiB; private {baseline.PrivateMiB:F2}->{end.PrivateMiB:F2}; handles {baseline.Handles}->{end.Handles}; Settings retained {weakSettings.Count(w => w.IsAlive)}/{weakSettings.Count}");
            await File.WriteAllTextAsync(Path.Combine(directory, "stability-audit.json"), JsonSerializer.Serialize(new { DateUtc = DateTime.UtcNow, Baseline = baseline, End = end, Snapshots = snapshots }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { island.Close(); await settings.FlushAsync(); }
        await SettleAndCollect();
        var windows = new List<WeakReference>();
        for (var i = 0; i < 3; i++) { windows.Add(await OpenCloseIsland(settings)); await SettleAndCollect(); }
        check(windows.Take(windows.Count - 1).All(w => !w.IsAlive) && ThemeService.Active is null,
            "Closed islands release theme subscriptions and earlier view graphs without accumulating windows");
        Console.WriteLine("Most recent island retained at checkpoint: " + windows[^1].IsAlive);
    }

    public static async Task ProfileSensorsAsync(string directory)
    {
        var result = await Task.Run(async () =>
        {
            var watch = Stopwatch.StartNew(); using var gpu = new GpuUsageSampler(); var gpuSetupMs = watch.Elapsed.TotalMilliseconds;
            var cpu = new SystemUsageSampler(); var network = new NetworkUsageSampler(); var disk = new SystemDiskSampler();
            var readings = new List<object>(); using var process = Process.GetCurrentProcess();
            void Read(string sensor, Func<object?> read)
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread(); var ticks = Stopwatch.GetTimestamp(); var cpuStart = process.TotalProcessorTime;
                var value = read(); var milliseconds = Stopwatch.GetElapsedTime(ticks).TotalMilliseconds;
                var cpuMs = (process.TotalProcessorTime - cpuStart).TotalMilliseconds;
                var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                var available = value is SystemUsageReading { Memory: not null } or GpuUsageReading { Percent: not null } or
                    NetworkUsageReading { State: not NetworkReadingState.Unavailable } or SystemDiskUsage;
                readings.Add(new { Sensor = sensor, WallMs = milliseconds, ProcessCpuMs = cpuMs, AllocatedBytes = bytes, Available = available });
            }
            for (var i = 0; i < 10; i++)
            {
                Read("CPU + RAM", () => cpu.Read()); Read("GPU PDH", () => gpu.Read(CancellationToken.None));
                Read("Network", () => network.Read()); Read("Disk", () => disk.Read());
                if (i < 9) await Task.Delay(2000);
            }
            return new { GpuSetupMs = gpuSetupMs, Samples = readings };
        });
        await File.WriteAllTextAsync(Path.Combine(directory, "sensor-profile.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Saved sensor-profile.json; GPU setup " + result.GpuSetupMs.ToString("F2") + " ms (native calls measured on a worker)");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> OpenCloseSettings(MainWindow island)
    { island.OpenSettings(); var window = Field<SettingsWindow>(island, "_settingsWindow"); await Task.Delay(100); var weak = new WeakReference(window); window.CloseImmediately(); return weak; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> OpenCloseIsland(SettingsController settings)
    { var window = new MainWindow(true, settings); window.Show(); await Task.Delay(100); var weak = new WeakReference(window); window.Close(); return weak; }
    private static async Task<Windows.Storage.Streams.IRandomAccessStreamReference> Artwork(string directory, string name, Color color)
    {
        var pixels = new byte[32 * 32 * 4]; for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255; }
        var image = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, pixels, 128); image.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); var path = Path.Combine(directory, name + ".png");
        using (var file = File.Create(path)) encoder.Save(file);
        return Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(await Windows.Storage.StorageFile.GetFileFromPathAsync(path));
    }
    private static async Task SettleAndCollect()
    { await Task.Delay(1000); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Task.Delay(100); }
    private static SettingsController Settings(string directory) => new(Application.Current.Dispatcher,
        new PreferencesStore(Path.Combine(directory, "performance-settings.json")), new Startup());
    private static MediaSnapshot Playing() => new("Synthetic benchmark track", "Validation only", "Playing", true,
        Timeline: new(TimeSpan.Zero, TimeSpan.FromMinutes(10), TimeSpan.Zero, DateTimeOffset.UtcNow, TimeSpan.Zero, TimeSpan.FromMinutes(10)));
    private static Resources ReadResources(Process process)
    { process.Refresh(); return new(process.PrivateMemorySize64 / 1048576d, process.WorkingSet64 / 1048576d, GC.GetTotalMemory(false) / 1048576d,
        process.HandleCount, process.Threads.Count, GetGuiResources(process.Handle, 0), GetGuiResources(process.Handle, 1)); }
    private static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
    private static void Invoke(object source, string name, params object[] args) => source.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(source, args);
    [DllImport("user32.dll")] private static extern uint GetGuiResources(nint process, uint flags);
}
