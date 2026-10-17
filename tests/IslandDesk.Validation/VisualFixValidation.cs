using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IslandDesk;
using IslandDesk.Controls;
using IslandDesk.Interaction;
using IslandDesk.Media;
using IslandDesk.Presentation;
using IslandDesk.Settings;
using IslandDesk.Themes;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using ThemeMode = IslandDesk.Settings.ThemeMode;

internal static class VisualFixValidation
{
    private sealed class Startup : IStartupRegistration { public bool IsEnabled() => false; public void SetEnabled(bool enabled) { } }
    public static async Task RunAsync(string directory, Action<bool, string> check, bool probe)
    {
        using var settings = new SettingsController(Application.Current.Dispatcher, new PreferencesStore(Path.Combine(directory, "visual-fixes.json")), new Startup());
        settings.Update(settings.Current with { AnimationStyle = TransitionStyle.Instant, DynamicBackground = true, BackgroundTintStrength = 30, SurfaceOpacity = 60 });
        var island = new MainWindow(false, settings);
        try
        {
            island.Show(); ((UIElement)island.FindName("IslandSurface")).IsHitTestVisible = false;
            Field<IslandInteractionController>(island, "_interaction").SetActive(false);
            var services = Field<IslandServices>(island, "_services"); services.Dispose();
            var vm = Field<IslandViewModel>(island, "_viewModel");
            Invoke(island, "SetExpanded", true);
            using var menu = new Forms.ContextMenuStrip();
            var settingsClicks = 0; var exitClicks = 0;
            var settingsItem = menu.Items.Add("Settings…", null, (_, _) => settingsClicks++);
            var exitItem = menu.Items.Add("Exit", null, (_, _) => exitClicks++); var disabled = menu.Items.Add("Unavailable"); disabled.Enabled = false;
            var renderer = new TrayThemeRenderer(); menu.Renderer = renderer;
            menu.CreateControl(); menu.PerformLayout();
            renderer.Refresh(menu);
            island.Theme.Changed += (_, _) => renderer.Refresh(menu); // Same event-driven wiring as App.
            var menuHandle = menu.Handle; var menuBounds = menu.Bounds;
            foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
            {
                settings.ThemeMode = mode; services.Dispose();
                settingsItem.Select();
                using var bitmap = new Drawing.Bitmap(settingsItem.Width, settingsItem.Height);
                using (var graphics = Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.Clear(menu.BackColor);
                    renderer.DrawMenuItemBackground(new Forms.ToolStripItemRenderEventArgs(graphics, settingsItem));
                    renderer.DrawItemText(new Forms.ToolStripItemTextRenderEventArgs(graphics, settingsItem, settingsItem.Text,
                        new Drawing.Rectangle(8, 0, bitmap.Width - 16, bitmap.Height), settingsItem.ForeColor, settingsItem.Font, Forms.TextFormatFlags.Left | Forms.TextFormatFlags.VerticalCenter));
                }
                var actual = bitmap.GetPixel(bitmap.Width - 12, bitmap.Height / 2);
                var expected = ((SolidColorBrush)Application.Current.FindResource("PopupHover")).Color;
                Console.WriteLine($"TRAY {mode}: selected={settingsItem.Selected}; visualStyles={Forms.Application.RenderWithVisualStyles}; pixel={actual}; expected={expected}");
                bitmap.Save(Path.Combine(directory, $"tray-{mode}-selected.png"));
                if (!probe) check(settingsItem.Selected && actual.R == expected.R && actual.G == expected.G && actual.B == expected.B,
                    $"{mode}: native WinForms selection uses the semantic hover color, without system blue");
                if (!probe)
                {
                    check(menu.Handle == menuHandle && menu.Bounds == menuBounds, $"{mode}: live menu theme refresh preserves the native handle and bounds");
                    foreach (var item in new[] { exitItem, disabled })
                    {
                        using var stateBitmap = new Drawing.Bitmap(item.Width, item.Height);
                        using (var graphics = Drawing.Graphics.FromImage(stateBitmap))
                        {
                            renderer.DrawMenuItemBackground(new Forms.ToolStripItemRenderEventArgs(graphics, item));
                            renderer.DrawItemText(new Forms.ToolStripItemTextRenderEventArgs(graphics, item, item.Text,
                                new Drawing.Rectangle(8, 0, item.Width - 16, item.Height), item.ForeColor, item.Font, Forms.TextFormatFlags.Left | Forms.TextFormatFlags.VerticalCenter));
                        }
                        var pixel = stateBitmap.GetPixel(item.Width - 12, item.Height / 2);
                        var surfaceColor = ((SolidColorBrush)Application.Current.FindResource("PopupSurface")).Color;
                        var foreground = ((SolidColorBrush)Application.Current.FindResource(item.Enabled ? "Primary" : "Muted")).Color;
                        var closest = 765;
                        for (var y = 0; y < item.Height; y++) for (var x = 3; x < item.Width - 12; x++)
                        { var p = stateBitmap.GetPixel(x, y); closest = Math.Min(closest, Math.Abs(p.R - foreground.R) + Math.Abs(p.G - foreground.G) + Math.Abs(p.B - foreground.B)); }
                        check(pixel.R == surfaceColor.R && pixel.G == surfaceColor.G && pixel.B == surfaceColor.B && closest <= 12,
                            $"{mode}: {(item.Enabled ? "normal" : "disabled")} item paints its semantic surface and readable text");
                        stateBitmap.Save(Path.Combine(directory, $"tray-{mode}-{(item.Enabled ? "normal" : "disabled")}.png"));
                    }
                }
                vm.Media.SetActive(true); vm.Media.SetArtworkVisible(true); vm.Volume.SetActive(true); vm.Volume.Update(new(64, false, true));
                var first = await SetSong(vm.Media, directory, "Red", Colors.Red);
                var a = Save(island, Path.Combine(directory, $"background-{mode}-red.png"));
                var cached = vm.Media.Artwork;
                settings.BackgroundTintStrength = 8;
                var subtle = Save(island, Path.Combine(directory, $"background-{mode}-8percent.png"));
                settings.BackgroundTintStrength = 0;
                var neutral = Save(island, Path.Combine(directory, $"background-{mode}-neutral.png"));
                settings.BackgroundTintStrength = 30;
                if (!probe) check(ReferenceEquals(cached, vm.Media.Artwork), $"{mode}: settings reuse the loaded artwork without extraction");
                services.Dispose();
                vm.Media.SetActive(true); vm.Media.SetArtworkVisible(true); vm.Volume.SetActive(true); vm.Volume.Update(new(64, false, true));
                var second = await SetSong(vm.Media, directory, "Blue", Colors.Blue);
                var b = Save(island, Path.Combine(directory, $"background-{mode}-blue.png"));
                var bounds = ((IslandSurface)island.FindName("IslandSurface")).VisualBounds;
                var gap = new Point(bounds.X + bounds.Width / 2, bounds.Y + 58);
                var card = new Point(bounds.X + 30, bounds.Y + 90);
                var gapDelta = Delta(a, b, gap); var cardDelta = Delta(a, b, card);
                Console.WriteLine($"BACKGROUND {mode}: enabled={settings.DynamicBackground}, strength={settings.BackgroundTintStrength}, brush={((IslandSurface)island.FindName("IslandSurface")).Background.GetType().Name}, accents={first}/{second}, rendered gap delta={gapDelta}, card delta={cardDelta}");
                if (!probe)
                {
                    check(first != second && gapDelta >= 15, $"{mode}: two real thumbnail/accent events visibly change the rendered expanded background at 30 percent");
                    check(cardDelta >= 6, $"{mode}: card surfaces no longer mask the dynamic background");
                    var small = Delta(subtle, neutral, card); var large = Delta(a, neutral, card);
                    Console.WriteLine($"STRENGTH {mode}: card pixel change at 8%={small}; 30%={large}");
                    check(small > 0 && large > small * 1.5, $"{mode}: default 8 percent is subtler than the visibly stronger 30 percent tint");
                    settings.AnimationStyle = TransitionStyle.Smooth; services.Dispose();
                    vm.Media.SetActive(true); vm.Media.SetArtworkVisible(true);
                    await SetSong(vm.Media, directory, "Red", Colors.Red);
                    var palette = Field<IslandPalette>(island, "_palette");
                    check(palette.CardBrush.HasAnimatedProperties && ((LinearGradientBrush)palette.Surface).GradientStops.Any(stop => stop.HasAnimatedProperties),
                        $"{mode}: actual artwork events start coordinated card/gradient fades");
                    await Task.Delay(40);
                    var layouts = 0; void Layout(object? sender, EventArgs e) => layouts++;
                    island.LayoutUpdated += Layout; await Task.Delay(90); island.LayoutUpdated -= Layout;
                    check(layouts == 0, $"{mode}: live card/gradient color fades add no per-frame layout");
                    await Task.Delay(350);
                    check(!palette.CardBrush.HasAnimatedProperties && ((LinearGradientBrush)palette.Surface).GradientStops.All(stop => !stop.HasAnimatedProperties),
                        $"{mode}: completed artwork fades leave no card/gradient clocks running");
                    settings.DynamicBackground = false; services.Dispose();
                    var normalCard = island.Theme.Palette.Card; normalCard.A = SurfaceContrast.Alpha(settings.SurfaceOpacity, mode == ThemeMode.Dark, true);
                    check(palette.CardBrush.Color == normalCard, $"{mode}: disabling tint immediately restores the exact theme card surface");
                    settings.DynamicBackground = true; settings.AnimationStyle = TransitionStyle.Instant; services.Dispose();
                }
            }
            settingsItem.PerformClick(); exitItem.PerformClick(); disabled.PerformClick();
            if (!probe) check(settingsClicks == 1 && exitClicks == 1, "Tray Settings and Exit still execute exactly once; disabled item stays inert");
        }
        finally { island.Close(); await settings.FlushAsync(); }
    }
    private static async Task<Color?> SetSong(MediaViewModel media, string directory, string title, Color color)
    {
        var pixels = Enumerable.Repeat(new byte[] { color.B, color.G, color.R, 255 }, 32 * 32).SelectMany(p => p).ToArray();
        var image = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, pixels, 128); image.Freeze();
        var path = Path.Combine(directory, $"visual-art-{title}.png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(path)) encoder.Save(file);
        var reference = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(await Windows.Storage.StorageFile.GetFileFromPathAsync(path));
        media.Update(new(title, "Artwork validation", "Playing", true, true, true, true, SourceId: "visual-test", ArtworkRevision: title == "Red" ? 1 : 2, Thumbnail: reference));
        var watch = Stopwatch.StartNew();
        while (media.Artwork is null && watch.ElapsedMilliseconds < 3000) await Task.Delay(25);
        if (media.Artwork is null) throw new InvalidOperationException("Test artwork did not load.");
        await Dispatcher.Yield(DispatcherPriority.Render);
        return media.ArtworkAccent;
    }
    private static RenderTargetBitmap Save(Window window, string path)
    {
        window.UpdateLayout(); var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var desktop = new DrawingVisual(); using (var drawing = desktop.RenderOpen()) drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
        image.Render(desktop); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file); return image;
    }
    private static int Delta(BitmapSource a, BitmapSource b, Point point)
    {
        var x = new byte[4]; var y = new byte[4]; var rect = new Int32Rect((int)point.X, (int)point.Y, 1, 1);
        a.CopyPixels(rect, x, 4, 0); b.CopyPixels(rect, y, 4, 0);
        return Math.Abs(x[0] - y[0]) + Math.Abs(x[1] - y[1]) + Math.Abs(x[2] - y[2]);
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
}
