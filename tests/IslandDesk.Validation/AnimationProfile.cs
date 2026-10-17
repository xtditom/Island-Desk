using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using IslandDesk;
using IslandDesk.Presentation;
using IslandDesk.Settings;

internal static class AnimationProfile
{
    private sealed class LayoutProbe : Decorator
    {
        public int Measures, Arranges;
        public long LayoutTicks;
        protected override Size MeasureOverride(Size available)
        {
            var start = Stopwatch.GetTimestamp(); Measures++;
            var result = base.MeasureOverride(available); LayoutTicks += Stopwatch.GetTimestamp() - start; return result;
        }
        protected override Size ArrangeOverride(Size available)
        {
            var start = Stopwatch.GetTimestamp(); Arranges++;
            var result = base.ArrangeOverride(available); LayoutTicks += Stopwatch.GetTimestamp() - start; return result;
        }
    }
    private sealed class Startup : IStartupRegistration
    { public bool IsEnabled() => false; public void SetEnabled(bool enabled) { } }

    public static async Task RunAsync(string artifacts)
    {
        using var settings = new SettingsController(Application.Current.Dispatcher,
            new PreferencesStore(Path.Combine(artifacts, "animation-profile-settings.json")), new Startup());
        var island = new MainWindow(enableFullscreenDetection: false, settings);
        var rows = new List<object>();
        try
        {
            island.Show();
            var surface = (UIElement)island.FindName("IslandSurface"); surface.IsHitTestVisible = false;
            var root = (UIElement)island.Content;
            island.Content = null;
            var layout = new LayoutProbe { Child = root }; island.Content = layout;
            var expanded = Field<FrameworkElement>(island, "ExpandedContent");
            var hwnd = new WindowInteropHelper(island).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            var morph = Field<IslandMorph>(island, "_morph");
            Console.WriteLine($"WPF tier={RenderCapability.Tier >> 16}; target={source.CompositionTarget.RenderMode}; display={settings.MonitorRefreshRate} Hz; transparency={island.AllowsTransparency}");
            await Task.Delay(1500);
            foreach (var cap in new int?[] { 60, 90, null })
            {
                settings.Update(settings.Current with { AnimationFrameRate = cap });
                // Warm both forms and their bindings before the measured run.
                Invoke(island, "SetExpanded", true); await CompleteAsync(morph);
                Invoke(island, "SetExpanded", false); await CompleteAsync(morph);
                var positions = 0; var sizes = 0; var contentLayouts = 0;
                var nativeSpans = new List<double>(); long nativeStart = 0;
                var intervals = new List<double>(); long? last = null; double previousProgress = double.NaN;
                void Tick(object? sender, EventArgs args)
                {
                    var progress = (double)morph.GetValue(IslandMorph.ProgressProperty);
                    if (progress == previousProgress) return;
                    previousProgress = progress;
                    var now = Stopwatch.GetTimestamp(); if (last is { } previous) intervals.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds); last = now;
                }
                void Layout(object? sender, EventArgs args) => contentLayouts++;
                nint Hook(nint h, int message, nint w, nint l, ref bool handled)
                {
                    if (message == 0x46) nativeStart = Stopwatch.GetTimestamp();
                    if (message == 0x47)
                    { positions++; if (nativeStart != 0) nativeSpans.Add(Stopwatch.GetElapsedTime(nativeStart).TotalMilliseconds); nativeStart = 0; }
                    if (message == 5) sizes++; return 0;
                }
                source.AddHook(Hook); morph.Changed += Tick; expanded.LayoutUpdated += Layout;
                layout.Measures = layout.Arranges = 0; layout.LayoutTicks = 0;
                using var process = Process.GetCurrentProcess(); process.Refresh(); var cpu = process.TotalProcessorTime;
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var elapsed = Stopwatch.StartNew();
                for (var i = 0; i < 8; i++)
                {
                    last = null; Invoke(island, "SetExpanded", true); await CompleteAsync(morph);
                    last = null; Invoke(island, "SetExpanded", false); await CompleteAsync(morph);
                }
                process.Refresh();
                source.RemoveHook(Hook); morph.Changed -= Tick; expanded.LayoutUpdated -= Layout;
                var sorted = intervals.Where(value => value > 0).Order().ToArray();
                var nativeSorted = nativeSpans.Order().ToArray();
                var row = new
                {
                    requested = cap?.ToString() ?? "Max", effective = settings.EffectiveFrameRate, transitions = 16,
                    positions, sizes, measures = layout.Measures, arranges = layout.Arranges, contentLayouts,
                    layoutMs = layout.LayoutTicks * 1000.0 / Stopwatch.Frequency,
                    processCpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                    uiAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated,
                    elapsedMs = elapsed.Elapsed.TotalMilliseconds,
                    tickMedianMs = sorted.Length == 0 ? 0 : sorted[sorted.Length / 2],
                    tickP95Ms = sorted.Length == 0 ? 0 : sorted[(int)((sorted.Length - 1) * .95)],
                    tickMaxMs = sorted.Length == 0 ? 0 : sorted[^1],
                    nativePositionP95Ms = nativeSorted.Length == 0 ? 0 : nativeSorted[(int)((nativeSorted.Length - 1) * .95)],
                    nativePositionMaxMs = nativeSorted.Length == 0 ? 0 : nativeSorted[^1],
                    activeClocksAfter = morph.HasAnimatedProperties
                };
                rows.Add(row); Console.WriteLine(JsonSerializer.Serialize(row));
            }
            File.WriteAllText(Path.Combine(artifacts, "animation-profile.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { island.Close(); await settings.FlushAsync(); File.Delete(Path.Combine(artifacts, "animation-profile-settings.json")); }
    }
    private static async Task CompleteAsync(IslandMorph morph)
    {
        var deadline = Stopwatch.StartNew();
        while (morph.HasAnimatedProperties && deadline.ElapsedMilliseconds < 3000) await Task.Delay(20);
        if (morph.HasAnimatedProperties) throw new TimeoutException("Profile transition did not complete.");
        await Task.Delay(80); // Validation only: separate completed transitions from one another.
    }
    private static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
    private static void Invoke(object source, string name, params object[] args) => source.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(source, args);
}
