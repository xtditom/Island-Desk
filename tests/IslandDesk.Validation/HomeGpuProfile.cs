using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IslandDesk;
using IslandDesk.Controls;
using IslandDesk.Media;
using IslandDesk.Presentation;
using IslandDesk.Settings;
using ThemeMode = IslandDesk.Settings.ThemeMode;

// Controlled Release diagnostics. No Rendering handler: subscribing would itself keep WPF rendering.
internal static class HomeGpuProfile
{
    private sealed class Startup : IStartupRegistration
    { public bool IsEnabled() => false; public void SetEnabled(bool enabled) { } }
    public static void ProbeRaster()
    {
        foreach (var thickness in new[] { 2d, 3d, 6d })
        {
            byte[]? previous = null; var changes = new List<double>();
            for (var i = 0; i <= 1000; i++)
            {
                var drawing = new DrawingVisual();
                using (var dc = drawing.RenderOpen()) dc.DrawRoundedRectangle(Brushes.White, null,
                    new Rect(0, (16 - thickness) / 2, 70 + i / 1000d, thickness), thickness / 2, thickness / 2);
                var bitmap = new RenderTargetBitmap(340, 16, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
                var pixels = new byte[340 * 16 * 4]; bitmap.CopyPixels(pixels, 340 * 4, 0);
                if (previous is not null && !pixels.AsSpan().SequenceEqual(previous)) changes.Add(i / 1000d);
                previous = pixels;
            }
            Console.WriteLine(JsonSerializer.Serialize(new { thickness, Changes = changes }));
        }
    }
    public static async Task RunAsync(string directory, bool homeOnly = false, int? fps = 60, int trackSeconds = 600)
    {
        using var settings = new SettingsController(Application.Current.Dispatcher,
            new PreferencesStore(Path.Combine(directory, "home-gpu-settings.json")), new Startup());
        // Independent of real user settings, media apps, system polling and wall-clock seconds.
        settings.Update(new AppSettings { AnimationFrameRate = fps, ThemeMode = ThemeMode.Dark });
        var island = new MainWindow(false, settings);
        var rows = new List<object>();
        try
        {
            island.Show(); ((UIElement)island.FindName("IslandSurface")).IsHitTestVisible = false;
            var vm = Field<IslandViewModel>(island, "_viewModel");
            var services = Field<IslandServices>(island, "_services");
            var shadow = (UIElement)island.FindName("IslandShadow");
            var source = (HwndSource)PresentationSource.FromVisual(island);
            foreach (var round in new[] { 1, 2 })
            foreach (var state in homeOnly ? new[] { "home-playing", "home-paused" } : new[] { "collapsed-paused", "collapsed-playing", "collapsed-equalizer-disabled",
                "home-playing", "home-equalizer-disabled", "home-shadow-hidden", "home-shadow-cached",
                "home-paused", "system-playing", "hidden-playing" })
            {
                settings.Update(new AppSettings { AnimationFrameRate = fps, ThemeMode = ThemeMode.Dark,
                    EqualizerEnabled = !state.Contains("equalizer-disabled") });
                island.Show();
                Invoke(island, "SetExpanded", !state.StartsWith("collapsed"));
                Invoke(island, "SelectDashboard", state.StartsWith("system") ? Dashboard.System : Dashboard.Main);
                services.Dispose();
                vm.Media.SetActive(true);
                vm.Media.Update(new("Controlled track", "IslandDesk diagnostics", state.Contains("paused") ? "Paused" : "Playing",
                    !state.Contains("paused"), Timeline: new(TimeSpan.Zero, TimeSpan.FromSeconds(trackSeconds), TimeSpan.FromSeconds(trackSeconds * .2),
                        DateTimeOffset.UtcNow, TimeSpan.Zero, TimeSpan.FromSeconds(trackSeconds), CanSeek: true)));
                shadow.Visibility = state == "home-shadow-hidden" ? Visibility.Hidden : Visibility.Visible;
                shadow.CacheMode = state == "home-shadow-cached" ? new BitmapCache(VisualTreeHelper.GetDpi(island).PixelsPerDip) : null;
                if (state.StartsWith("hidden")) island.Hide();
                await Task.Delay(2000);
                var layouts = 0;
                void Layout(object? sender, EventArgs args) => layouts++;
                island.LayoutUpdated += Layout;
                const int seconds = 10;
                var file = Path.Combine(directory, $"home-gpu-{round}-{state}.json");
                var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
                foreach (var argument in new[] { typeof(HomeGpuProfile).Assembly.Location, "--gpu-probe",
                    Environment.ProcessId.ToString(), seconds.ToString(), file }) info.ArgumentList.Add(argument);
                using var gpu = Process.Start(info)!;
                using var process = Process.GetCurrentProcess();
                var cpu = process.TotalProcessorTime; var elapsed = Stopwatch.StartNew();
                await Task.Delay(seconds * 1000); await gpu.WaitForExitAsync(); process.Refresh();
                island.LayoutUpdated -= Layout;
                var values = JsonSerializer.Deserialize<double[]>(await File.ReadAllTextAsync(file))!;
                var row = new { Round = round, State = state, Seconds = elapsed.Elapsed.TotalSeconds,
                    CpuMachinePercent = (process.TotalProcessorTime - cpu).TotalSeconds / elapsed.Elapsed.TotalSeconds / Environment.ProcessorCount * 100,
                    GpuMean = values.Length == 0 ? (double?)null : values.Average(), GpuSamples = values.Length,
                    WorkingSetMiB = process.WorkingSet64 / 1048576d, Layouts = layouts,
                    Equalizer = ((Equalizer)island.FindName("MusicEqualizer")).IsAnimating,
                    Transitions = Field<IslandTransitions>(island, "_transitions").IsAnimating };
                rows.Add(row); Console.WriteLine(JsonSerializer.Serialize(row));
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "home-gpu-profile.json"), JsonSerializer.Serialize(new {
                DateUtc = DateTime.UtcNow, Environment.ProcessorCount, WpfTier = RenderCapability.Tier >> 16,
                RenderMode = source.CompositionTarget.RenderMode.ToString(), Dpi = VisualTreeHelper.GetDpi(island).PixelsPerDip,
                RefreshHz = settings.MonitorRefreshRate, EffectiveFps = settings.EffectiveFrameRate,
                RequestedFps = fps, TrackSeconds = trackSeconds, Notes = "Two 10s samples per state, 2s settle. Synthetic seekable track at 20% initially, same window, services off, timestamps on, dark/default visuals. GPU: mean busiest valid per-process engine via external PDH sampler, excludes DWM. CPU normalized over logical processors. Shadow ablations are diagnostics only. No Rendering subscription or forced GC.", Samples = rows
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { island.Close(); await settings.FlushAsync(); }
    }
    private static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
    private static void Invoke(object source, string name, params object[] args) => source.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(source, args);
}
