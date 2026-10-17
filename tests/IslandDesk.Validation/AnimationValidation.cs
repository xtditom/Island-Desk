using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IslandDesk;
using IslandDesk.Controls;
using IslandDesk.Media;
using IslandDesk.Presentation;
using IslandDesk.Settings;

internal static class AnimationValidation
{
    private sealed class Startup : IStartupRegistration
    { public bool IsEnabled() => false; public void SetEnabled(bool enabled) { } }
    public static async Task RunAsync(string artifacts, Action<bool, string> check)
    {
        var path = Path.Combine(artifacts, "animation-check-settings.json");
        using var settings = new SettingsController(Application.Current.Dispatcher, new PreferencesStore(path), new Startup());
        settings.Update(settings.Current with { AnimationSpeed = .5 });
        var island = new MainWindow(enableFullscreenDetection: false, settings);
        try
        {
            island.Show();
            var surface = Field<IslandSurface>(island, "IslandSurface"); surface.IsHitTestVisible = false;
            var morph = Field<IslandMorph>(island, "_morph");
            var services = Field<IslandServices>(island, "_services");
            var media = Field<IslandViewModel>(island, "_viewModel").Media;
            var hwnd = new WindowInteropHelper(island).Handle;
            var playing = false;
            void ResetMedia() { services.Dispose(); media.SetActive(true); media.Update(playing ? new("Transition sample", "Test artist", "Playing", true) : MediaSnapshot.Empty); }
            void Expand(bool value) { Invoke(island, "SetExpanded", value); ResetMedia(); }
            ResetMedia(); await Task.Delay(650);
            foreach (var cap in new int?[] { 60, 90, null })
            {
                settings.Update(settings.Current with { AnimationFrameRate = cap });
                Expand(true); await Task.Delay(110);
                GetWindowRect(hwnd, out var envelope);
                var first = surface.VisualBounds;
                check(first.Width > 106 && first.Width < 400 && first.Height > 36 && first.Height < new AppSettings().HomeHeight,
                    $"{cap?.ToString() ?? "Max"}: both visible axes morph without scaling content");
                var outside = new Point(2, island.ActualHeight - 2);
                check(VisualTreeHelper.HitTest(surface, outside) is null, "Transparent host margin is excluded by the rounded visual clip");
                var screen = island.PointToScreen(outside);
                check(WindowFromPoint(new((int)screen.X, (int)screen.Y)) != hwnd, "Transparent host margin passes native hit-testing through");
                Save(island, Path.Combine(artifacts, $"animation-expand-{cap?.ToString() ?? "Max"}.png"));
                await Task.Delay(30);
                GetWindowRect(hwnd, out var nextEnvelope);
                check(envelope == nextEnvelope, "Native bounds stay fixed between visual animation ticks");
                var current = surface.VisualBounds;
                Expand(false);
                check(Math.Abs(surface.VisualBounds.Width - current.Width) < .1 && Math.Abs(surface.VisualBounds.Height - current.Height) < .1,
                    "Reversing expansion starts at the currently displayed geometry");
                await Task.Delay(55); current = surface.VisualBounds;
                Expand(true);
                check(Math.Abs(surface.VisualBounds.Width - current.Width) < .1, "Rapid re-entry reverses collapse without snapping");
                await Task.Delay(650);
                check(Math.Abs(island.ActualWidth - 416) < .1 && Math.Abs(island.ActualHeight - 16 - new AppSettings().HomeHeight) < .1 &&
                    surface.VisualBounds.Size == new Size(400, new AppSettings().HomeHeight) && !morph.HasAnimatedProperties,
                    "Expansion commits tight final native bounds and releases its morph clock");
                check(Field<TranslateTransform>(island, "PillTranslation") is not null &&
                    Field<FrameworkElement>(island, "ExpandedContent").ActualWidth == 370, "Content keeps its fixed layout and translation-only motion");
                Expand(false); await Task.Delay(600);
                check(island.ActualWidth == 122 && island.ActualHeight == 52 && surface.VisualBounds == new Rect(8, 8, 106, 36),
                    "Collapse preserves original visible pill size inside the shadow gutter");
            }
            settings.AnimationStyle = TransitionStyle.Instant;
            foreach (var alignment in new[] { IslandAlignment.Left, IslandAlignment.Right })
            {
                settings.Alignment = alignment; settings.XOffset = alignment == IslandAlignment.Left ? 50 : -50;
                Expand(false); var edge = alignment == IslandAlignment.Left ? island.Left + 8 : island.Left + island.ActualWidth - 8;
                settings.AnimationStyle = TransitionStyle.Smooth;
                Expand(true); await Task.Delay(100);
                var visibleEdge = island.Left + surface.VisualBounds.X + (alignment == IslandAlignment.Right ? surface.VisualBounds.Width : 0);
                check(Math.Abs(edge - visibleEdge) < 1, "Left/right alignment and X offset stay anchored during the visual morph");
                await Task.Delay(650); Expand(false); await Task.Delay(600);
                settings.AnimationStyle = TransitionStyle.Instant;
            }
            settings.Alignment = IslandAlignment.Center; settings.XOffset = 0;
            settings.Update(settings.Current with { AnimationSpeed = 1 });
            var pill = Field<FrameworkElement>(island, "PillButton");
            var panel = Field<FrameworkElement>(island, "ExpandedContent");
            var clock = Field<FrameworkElement>(island, "ClockPill");
            foreach (var music in new[] { false, true })
            {
                settings.AnimationStyle = TransitionStyle.Instant;
                Expand(false); playing = music; ResetMedia(); Invoke(island, "UpdatePresentation", false);
                settings.AnimationStyle = TransitionStyle.Smooth;
                var clockVisibility = clock.Visibility;
                Expand(true);
                check(clock.Visibility == clockVisibility, "Expansion retains the outgoing pill form instead of revealing the other form");
                var overlap = false; var clipped = true;
                for (var i = 0; i < 18; i++)
                {
                    await Task.Delay(20);
                    overlap |= pill.IsVisible && panel.IsVisible && pill.Opacity > .001 && panel.Opacity > .001;
                    if (i == 2)
                    {
                        Save(island, Path.Combine(artifacts, $"layers-expand-{music}.png"));
                        clipped &= IsClipped(island, surface.VisualBounds);
                    }
                }
                check(!overlap, "Pill and dashboard never draw over one another during expansion");
                check(clipped, "Intermediate expansion raster stays inside the animated rounded boundary");
                Invoke(island, "SelectDashboard", Dashboard.System); await Task.Delay(300); ResetMedia();
                Expand(false);
                check(Field<FrameworkElement>(island, "SystemDashboard").Visibility == Visibility.Visible,
                    "Collapse retains the outgoing System dashboard while it fades");
                overlap = false; clipped = true;
                Rect? endpoint = null;
                void EndFrame(object? sender, EventArgs e)
                {
                    if ((double)morph.GetValue(IslandMorph.ProgressProperty) == 1)
                        endpoint = new Rect(island.Left + surface.VisualBounds.X, island.Top + surface.VisualBounds.Y,
                            surface.VisualBounds.Width, surface.VisualBounds.Height);
                }
                morph.Changed += EndFrame;
                for (var i = 0; i < 18; i++)
                {
                    await Task.Delay(20);
                    overlap |= pill.IsVisible && panel.IsVisible && pill.Opacity > .001 && panel.Opacity > .001;
                    if (i == 2)
                    {
                        Save(island, Path.Combine(artifacts, $"layers-collapse-{music}.png"));
                        clipped &= IsClipped(island, surface.VisualBounds);
                    }
                }
                morph.Changed -= EndFrame;
                check(!overlap, "Pill and dashboard never draw over one another during collapse");
                check(clipped, "Intermediate collapse raster stays inside the animated rounded boundary");
                check(!panel.IsVisible && island.ActualWidth == (music ? 276 : 122) && island.ActualHeight == 52,
                    "Native trim occurs with no visible outgoing dashboard and exact final pill dimensions");
                check(endpoint == new Rect(island.Left + 8, island.Top + 8, island.ActualWidth - 16, island.ActualHeight - 16),
                    "Last animated screen rectangle exactly matches the visible boundary inside the trimmed HWND without an endpoint correction");
                var continuous = true;
                for (var i = 0; i < 8; i++)
                {
                    var bounds = new Rect(island.Left + surface.VisualBounds.X, island.Top + surface.VisualBounds.Y,
                        surface.VisualBounds.Width, surface.VisualBounds.Height);
                    var pillOpacity = pill.Opacity; var panelOpacity = panel.Opacity;
                    Expand(i % 2 == 0);
                    continuous &= Math.Abs(bounds.X - (island.Left + surface.VisualBounds.X)) < .001 &&
                        Math.Abs(bounds.Width - surface.VisualBounds.Width) < .001 && Math.Abs(bounds.Height - surface.VisualBounds.Height) < .001 &&
                        Math.Abs(pillOpacity - pill.Opacity) < .001 && Math.Abs(panelOpacity - panel.Opacity) < .001;
                    await Task.Delay(25);
                    continuous &= pill.Opacity < .001 || panel.Opacity < .001;
                }
                await Task.Delay(350);
                check(continuous && !panel.IsVisible && !morph.HasAnimatedProperties,
                    "Rapid music/clock reversals retain screen geometry and layer opacity without overlap or stale completion");
            }
            playing = false; ResetMedia(); await Task.Delay(350);
            settings.Alignment = IslandAlignment.Left;
            Expand(true); await Task.Delay(60);
            var oldEdge = island.Left + surface.VisualBounds.X;
            settings.XOffset = 50;
            check(Math.Abs(island.Left + surface.VisualBounds.X - oldEdge - 50) < 1,
                "An offset change during a morph immediately rebases its visible screen anchor");
            await Task.Delay(350);
            check(surface.VisualBounds == new Rect(8, 8, island.ActualWidth - 16, island.ActualHeight - 16) && !morph.HasAnimatedProperties,
                "A placement-retargeted transition still finishes exactly inside its native host");
            Expand(false); await Task.Delay(350);
            var changes = 0; void Changed(object? s, EventArgs e) => changes++;
            morph.Changed += Changed;
            await Task.Delay(350); morph.Changed -= Changed;
            check(changes == 0 && !morph.HasAnimatedProperties && !Field<FrameworkElement>(island, "ExpandedContent").HasAnimatedProperties,
                "Static pill has no morph ticks or surviving transition clocks while idle");
            check(!Field<IslandTransitions>(island, "_transitions").IsAnimating, "All opacity/translation/morph clocks are released at idle");
        }
        finally { island.Close(); await settings.FlushAsync(); File.Delete(path); }
    }
    private static bool IsClipped(Window window, Rect bounds)
    {
        // Two raster densities exercise rounding/antialiasing; this does not emulate a native DPI change.
        foreach (var scale in new[] { 1d, 1.5, 2d })
        {
            var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * scale),
                (int)Math.Ceiling(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            image.Render((Visual)window.FindName("IslandSurface")); // Exclude the intentional outer shadow from the content leak check.
            var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            var padded = bounds; padded.Inflate(1 / scale, 1 / scale);
            var allowed = new RectangleGeometry(padded, 18, 18);
            for (var y = 0; y < image.PixelHeight; y++)
                for (var x = 0; x < image.PixelWidth; x++)
                    if (pixels[(y * image.PixelWidth + x) * 4 + 3] > 0 &&
                        !allowed.FillContains(new Point((x + .5) / scale, (y + .5) / scale))) return false;
        }
        return true;
    }
    private static void Save(Window window, string path)
    {
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render((Visual)window.Content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(path); encoder.Save(output);
    }
    private static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
    private static void Invoke(object source, string name, params object[] args) => source.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(source, args);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativeRect(int Left, int Top, int Right, int Bottom);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
}

