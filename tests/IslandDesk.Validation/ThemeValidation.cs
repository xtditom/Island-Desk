using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
using IslandDesk.Views;
using Forms = System.Windows.Forms;
using ThemeMode = IslandDesk.Settings.ThemeMode;

internal static class ThemeValidation
{
    private sealed class Startup : IStartupRegistration { public bool IsEnabled() => false; public void SetEnabled(bool enabled) { } }
    private sealed class Source : IWindowsThemeSource
    {
        public bool IsDark { get; set; } = true;
        public bool Disposed;
        public event EventHandler? Changed;
        public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
        public void Dispose() { Disposed = true; Changed = null; }
    }
    public static async Task RunAsync(string directory, Action<bool, string> check)
    {
        check(new AppSettings().ThemeMode == ThemeMode.System && new AppSettings { ThemeMode = (ThemeMode)99 }.Normalize().ThemeMode == ThemeMode.System,
            "Theme defaults to System and invalid saved values normalize safely");
        using (var native = new WindowsThemeSource())
        {
            Console.WriteLine($"Windows UISettings: dark={native.IsDark}; color notifications={native.HasNotifications}");
            check(native.HasNotifications, "Native Windows app-color notification subscription is available");
        }
        var path = Path.Combine(directory, "theme-settings.json");
        using var settings = new SettingsController(Application.Current.Dispatcher, new PreferencesStore(path), new Startup());
        settings.AnimationStyle = TransitionStyle.Instant;
        var source = new Source(); var initialResources = Application.Current.Resources.MergedDictionaries.Count;
        var island = new MainWindow(false, settings, source);
        SettingsWindow? window = null;
        try
        {
            island.Show();
            var surface = (IslandSurface)island.FindName("IslandSurface"); surface.IsHitTestVisible = false;
            var services = Field<IslandServices>(island, "_services");
            var vm = Field<IslandViewModel>(island, "_viewModel");
            var hwnd = new WindowInteropHelper(island).Handle;
            var root = island.Content; var header = (DependencyObject)island.FindName("Header");
            void StopMedia(bool playing = false)
            {
                services.Dispose(); vm.Media.SetActive(true);
                vm.Media.Update(playing ? new("Theme validation", "Artist", "Playing", true, true, true, true,
                    Timeline: new(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(42), DateTimeOffset.UtcNow, TimeSpan.Zero, TimeSpan.FromMinutes(3), 1, true)) : MediaSnapshot.Empty);
                vm.Volume.SetActive(true); vm.Volume.Update(new(64, false, true));
            }
            StopMedia();
            check(island.Theme.IsDark && surface.Background is SolidColorBrush, "System mode starts with the source theme and a solid collapsed clock pill");
            var originalPill = ((SolidColorBrush)surface.Background).Color;
            settings.GradientIntensity = 0; StopMedia(); settings.GradientIntensity = 100; StopMedia();
            check(surface.Background is SolidColorBrush pill && pill.Color == originalPill, "Gradient intensity never changes the collapsed pill background");
            window = new SettingsWindow(settings); window.Show(); window.UpdateLayout();
            var categories = (TabControl)window.FindName("Categories");
            var quick = (ComboBox)window.FindName("QuickThemePicker");
            var appearancePicker = (ComboBox)window.FindName("ThemePicker");
            check(quick.Items.Count == 3 && (ThemeMode)quick.SelectedValue == ThemeMode.System && quick.ActualWidth == 32,
                "Title bar has one compact System/Light/Dark selector, defaulting to the saved mode");
            quick.SelectedValue = ThemeMode.Light; await Flush();
            check(settings.ThemeMode == ThemeMode.Light && (ThemeMode)appearancePicker.SelectedValue == ThemeMode.Light && !island.Theme.IsDark,
                "Title-bar selection updates Appearance, the controller and the island immediately");
            appearancePicker.SelectedValue = ThemeMode.Dark; await Flush();
            check(settings.ThemeMode == ThemeMode.Dark && (ThemeMode)quick.SelectedValue == ThemeMode.Dark,
                "Appearance selection synchronizes the title-bar icon selector in the other direction");
            quick.IsDropDownOpen = true; await Flush();
            check(quick.IsDropDownOpen && quick.ItemContainerGenerator.ContainerFromIndex(0) is ComboBoxItem,
                "Title-bar icon opens its three-option native WPF popup");
            quick.IsDropDownOpen = false; settings.ThemeMode = ThemeMode.System;
            var normalActive = GetActiveWindow(); var normalFocus = Keyboard.FocusedElement;
            var changes = 0; island.Theme.Changed += (_, _) => changes++;
            source.IsDark = false; await Task.Run(source.Notify); await Flush();
            check(!island.Theme.IsDark && surface.Background is SolidColorBrush lightPill && lightPill.Color.R > 220,
                "A background Windows color event switches System mode to Light on the UI dispatcher");
            check(GetActiveWindow() == normalActive && Keyboard.FocusedElement == normalFocus && ReferenceEquals(root, island.Content) && ReferenceEquals(header, island.FindName("Header")),
                "Live switching preserves focus, visual instances and window ownership");
            settings.ThemeMode = ThemeMode.Dark; source.Notify(); await Flush();
            check(island.Theme.IsDark && settings.Current.ThemeMode == ThemeMode.Dark, "Manual Dark overrides a Light Windows preference");
            settings.ThemeMode = ThemeMode.Light; source.IsDark = true; source.Notify(); await Flush();
            check(!island.Theme.IsDark, "Manual Light ignores a Dark Windows preference");
            settings.ThemeMode = ThemeMode.System;
            check(island.Theme.IsDark, "Returning to System resolves the current Windows preference immediately");
            source.IsDark = false; SendMessage(hwnd, 0x001A, 0, 0);
            check(!island.Theme.IsDark, "Existing WM_SETTINGCHANGE handling also refreshes System mode without polling");
            foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
            {
                settings.ThemeMode = mode; settings.GradientIntensity = 60;
                StopMedia(); Invoke(island, "SetExpanded", false); StopMedia(); await Flush();
                var expectedAlpha = SurfaceContrast.Alpha(80, mode == ThemeMode.Dark);
                check(surface.Background is SolidColorBrush solid && solid.Color.A == expectedAlpha && island.Opacity == 1,
                    $"{mode}: collapsed clock surface stays solid and only the brush is translucent");
                Save(island, Path.Combine(directory, $"theme-{mode}-clock.png"));
                StopMedia(true); await Flush();
                check(surface.Background is SolidColorBrush, $"{mode}: collapsed playing-music surface also stays solid");
                settings.EqualizerColor = EqualizerColorSource.Custom; settings.EqualizerCustomColor = "#FFCC77"; StopMedia(true);
                var palette = Field<IslandPalette>(island, "_palette"); var color = palette.EqualizerBrush.Color;
                var volumeAccent = ArtworkAccentExtractor.EnsureContrast(Colors.Orange, mode == ThemeMode.Dark);
                palette.Apply(settings.Current, Colors.Orange, false, 90, false, volumeVisible: true);
                check(palette.VolumeFillBrush.Color == volumeAccent && palette.VolumeThumbBrush.Color == volumeAccent &&
                    (mode == ThemeMode.Light ? Luminance(volumeAccent) <= .061 : Luminance(volumeAccent) >= .399),
                    $"{mode}: artwork volume fill/thumb use the existing foreground-contrast adjustment");
                check(!ReferenceEquals(window.FindResource("VolumeFill"), palette.VolumeFillBrush) &&
                    ((SolidColorBrush)window.FindResource("VolumeFill")).Color == ((SolidColorBrush)Application.Current.FindResource("VolumeFill")).Color,
                    $"{mode}: Settings sliders retain their normal theme colors outside the widget's resource scope");
                settings.DynamicVolume = false; StopMedia(true);
                check(palette.VolumeFillBrush.Color == ((SolidColorBrush)Application.Current.FindResource("VolumeFill")).Color &&
                    palette.VolumeThumbBrush.Color == ((SolidColorBrush)Application.Current.FindResource("ControlForeground")).Color &&
                    palette.VolumeTrackBrush.Color == ((SolidColorBrush)Application.Current.FindResource("UsageTrack")).Color,
                    $"{mode}: disabling volume colors restores the precise original fill, thumb and track colors");
                settings.DynamicVolume = true; StopMedia(true);
                check(mode == ThemeMode.Light ? Luminance(color) <= .061 : Luminance(color) >= .399,
                    $"{mode}: custom/artwork foregrounds adapt their contrast to the surface theme");
                Save(island, Path.Combine(directory, $"theme-{mode}-music.png"));
                Invoke(island, "SetExpanded", true); StopMedia(true); vm.Clock.Configure(new(false, false)); await Flush();
                check(surface.Background is LinearGradientBrush gradient && gradient.GradientStops.All(stop => stop.Color.A == expectedAlpha),
                    $"{mode}: expanded panel has an independent translucent gradient");
                var background = (LinearGradientBrush)surface.Background; var expandedColors = background.GradientStops.Select(stop => stop.Color).ToArray();
                settings.TintStrength = 10; settings.FallbackAccent = "#66CCFF"; StopMedia(true);
                check(expandedColors.SequenceEqual(((LinearGradientBrush)surface.Background).GradientStops.Select(stop => stop.Color)),
                    $"{mode}: music tint never colors the expanded panel gradient");
                var outline = surface.BorderBrush; var shadowColor = ((IslandShadow)island.FindName("IslandShadow")).Color;
                settings.DynamicBackground = true; settings.BackgroundTintStrength = 30; StopMedia(true); await Flush();
                check(ReferenceEquals(background, surface.Background) && !expandedColors.SequenceEqual(background.GradientStops.Select(stop => stop.Color)) &&
                    background.GradientStops.All(stop => stop.Color.A == expectedAlpha) && ReferenceEquals(outline, surface.BorderBrush) &&
                    ((IslandShadow)island.FindName("IslandShadow")).Color == shadowColor,
                    $"{mode}: background tint updates the existing gradient while preserving alpha, outline and shadow");
                var contrastPreserved = true;
                foreach (var opacity in new[] { 60d, 100d })
                foreach (var original in expandedColors.Concat(new[] { island.Theme.Palette.ExpandedHigh, island.Theme.Palette.ExpandedLow }))
                foreach (var accent in new[] { Colors.Red, Colors.Lime, Colors.Blue, Colors.White, Colors.Black })
                {
                    var alpha = SurfaceContrast.Alpha(opacity, mode == ThemeMode.Dark);
                    var tinted = IslandPalette.TintSurface(original, accent, .30, mode == ThemeMode.Dark, alpha);
                    foreach (var desktop in new[] { Colors.Black, Colors.White })
                    {
                        var before = Luminance(IslandPalette.Mix(desktop, original, alpha / 255d));
                        var after = Luminance(IslandPalette.Mix(desktop, tinted, alpha / 255d));
                        var labels = new[] { "Primary", "Secondary", "Muted" }.Select(key => Luminance(((SolidColorBrush)Application.Current.FindResource(key)).Color));
                        foreach (var label in labels)
                        {
                            double Ratio(double a, double b) => (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
                            contrastPreserved &= Ratio(label, after) + .001 >= Math.Min(4.5, Ratio(label, before));
                        }
                    }
                }
                check(contrastPreserved, $"{mode}: maximum tint retains readable label contrast over black/white desktops without forcing identical background luminance");
                Save(island, Path.Combine(directory, $"background-{mode}-white.png"), Colors.White);
                Save(island, Path.Combine(directory, $"background-{mode}-black.png"), Colors.Black);
                settings.DynamicBackground = false; StopMedia(true);
                check(expandedColors.SequenceEqual(background.GradientStops.Select(stop => stop.Color)) && background.GradientStops.All(stop => !stop.HasAnimatedProperties),
                    $"{mode}: disabling background tint restores the exact original theme gradient without active clocks");
                var home = (MainDashboard)island.FindName("MainDashboard");
                var clockCard = (Border)home.FindName("ClockSection"); var title = Descendants<TextBlock>(header).First(text => text.Text == vm.Title);
                var cardColor = ((SolidColorBrush)clockCard.Background).Color; var titleColor = ((SolidColorBrush)title.Foreground).Color;
                var primary = ((SolidColorBrush)Application.Current.FindResource("Primary")).Color;
                if (mode == ThemeMode.Light)
                {
                    foreach (var desktop in new[] { Colors.Black, Colors.White })
                    {
                        var low = island.Theme.Palette.ExpandedLow;
                        var effective = IslandPalette.Mix(desktop, low, SurfaceContrast.Alpha(60, false) / 255d);
                        var secondary = ((SolidColorBrush)Application.Current.FindResource("Secondary")).Color;
                        var muted = ((SolidColorBrush)Application.Current.FindResource("Muted")).Color;
                        double Ratio(Color a, Color b) => (Math.Max(Luminance(a), Luminance(b)) + .05) / (Math.Min(Luminance(a), Luminance(b)) + .05);
                        check(new[] { primary, secondary, muted }.All(text => Ratio(text, effective) >= 4.5),
                            $"Light: primary, secondary and small labels meet 4.5:1 on a {desktop} desktop at minimum opacity");
                        var input = ((SolidColorBrush)window.FindResource("InputSurface")).Color;
                        var stroke = ((SolidColorBrush)window.FindResource("InputBorder")).Color;
                        check(Ratio(input, stroke) >= 3, "Light: input boundaries meet 3:1 against their field surface");
                    }
                    var cardSurface = island.Theme.Palette.Card;
                    var track = ((SolidColorBrush)window.FindResource("UsageTrack")).Color;
                    check((Luminance(cardSurface) + .05) / (Luminance(track) + .05) >= 3,
                        "Light: volume/usage track remains distinguishable at 3:1 against its card");
                }
                var shadow = (IslandShadow)island.FindName("IslandShadow");
                check(!shadow.IsHitTestVisible && !shadow.HasAnimatedProperties && shadow.Effect is null && surface.Effect is null &&
                    surface.VisualBounds.X >= 8 && surface.VisualBounds.Y >= 8,
                    $"{mode}: rounded shadow has a reserved gutter, no input surface, no blur effect and no animation clock");
                check(mode == ThemeMode.Light ? cardColor.R > 230 && titleColor.R < 70 : cardColor.R < 60 && titleColor.R > 200,
                    $"{mode}: cards and already-created title text inherit the same semantic palette");
                Save(island, Path.Combine(directory, $"theme-{mode}-home.png"));
                Save(island, Path.Combine(directory, $"theme-{mode}-home-white.png"), Colors.White);
                Save(island, Path.Combine(directory, $"theme-{mode}-home-black.png"), Colors.Black);
                Invoke(island, "SelectDashboard", Dashboard.System); StopMedia(); await Flush();
                Save(island, Path.Combine(directory, $"theme-{mode}-system.png"));
                settings.BackgroundStyle = IslandBackgroundStyle.SolidDark;
                check(surface.Background is SolidColorBrush, $"{mode}: Solid background option remains functional in expanded state");
                settings.BackgroundStyle = IslandBackgroundStyle.CharcoalGradient;
                foreach (var alpha in new[] { 60d, 100d })
                {
                    settings.SurfaceOpacity = alpha;
                    check(((LinearGradientBrush)surface.Background).GradientStops.All(stop => stop.Color.A == SurfaceContrast.Alpha(alpha, mode == ThemeMode.Dark)) && island.Opacity == 1,
                        $"{mode}: {alpha}% surface opacity preserves fully opaque content");
                }
                settings.SurfaceOpacity = 80;
                for (var i = 0; i < categories.Items.Count; i++)
                {
                    categories.SelectedIndex = i; await Flush();
                    Save(window, Path.Combine(directory, $"theme-{mode}-settings-{i}.png"));
                    check(((SolidColorBrush)window.Background).Color.R == ((SolidColorBrush)window.FindResource("SettingsSurface")).Color.R,
                        $"{mode}: settings category {i + 1} shares the current window surface");
                }
                categories.SelectedIndex = 1; await Flush();
                var picker = (ComboBox)window.FindName("ThemePicker");
                check(picker.Items.Count == 3 && (ThemeMode)picker.SelectedValue == mode && picker.ItemTemplate is not null,
                    $"{mode}: Appearance selector has System, sun and moon choices and live selection");
                var field = (TextBox)window.FindName("TitleInput");
                check(((SolidColorBrush)field.Foreground).Color == primary, $"{mode}: text inputs use theme-aware foregrounds");
                var dialog = new ConfirmationWindow("Restore defaults", "Restore all IslandDesk preferences and turn off Windows startup?", "Restore");
                dialog.Show(); await Flush(); Save(dialog, Path.Combine(directory, $"theme-{mode}-dialog.png")); dialog.Close();
                var rename = new RenameWindow(vm.Title, _ => Task.FromResult(true)); rename.Show(); await Flush();
                Save(rename, Path.Combine(directory, $"theme-{mode}-rename.png")); rename.Close();
                var tooltip = new ToolTip { Content = "Playback progress · drag to seek" }; tooltip.ApplyTemplate(); tooltip.Measure(new(300, 80)); tooltip.Arrange(new Rect(0, 0, tooltip.DesiredSize.Width, tooltip.DesiredSize.Height));
                check(((SolidColorBrush)tooltip.Foreground).Color == primary, $"{mode}: tooltips use the semantic foreground");
                using var menu = new Forms.ContextMenuStrip(); menu.Items.Add("Settings…"); menu.Items.Add("Exit");
                var renderer = new TrayThemeRenderer(); menu.Renderer = renderer; renderer.Refresh(menu);
                var popup = ((SolidColorBrush)Application.Current.FindResource("PopupSurface")).Color;
                check(menu.BackColor.R == popup.R && menu.ForeColor.R == primary.R, $"{mode}: native tray menu uses the app's background and text colors");
            }
            settings.ThemeMode = ThemeMode.Light; await settings.FlushAsync();
            categories.SelectedIndex = 1; await Flush();
            var livePicker = (ComboBox)window.FindName("ThemePicker"); livePicker.IsDropDownOpen = true; await Flush();
            var liveItem = (ComboBoxItem)livePicker.ItemContainerGenerator.ContainerFromIndex(0);
            settings.ThemeMode = ThemeMode.Dark; await Flush();
            check(((SolidColorBrush)liveItem.Foreground).Color.R > 220, "An already-open dropdown follows a live switch to Dark");
            settings.ThemeMode = ThemeMode.Light; await Flush();
            check(((SolidColorBrush)liveItem.Foreground).Color.R < 70, "An already-open dropdown follows a live switch to Light");
            livePicker.IsDropDownOpen = false;
            await settings.FlushAsync();
            _ = window.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => Application.Current.Windows.OfType<ConfirmationWindow>().Single().Close()));
            check(!ConfirmationWindow.Confirm(window, "Cancel test", "This validation changes no preferences.", "Confirm"),
                "Closing a themed confirmation returns Cancel");
            _ = window.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<ConfirmationWindow>().Single();
                Invoke(dialog, "Confirm_Click", dialog, new RoutedEventArgs());
            }));
            check(ConfirmationWindow.Confirm(window, "Confirm test", "This validation changes no preferences.", "Confirm"),
                "The themed confirmation button returns an explicit approval result");
            check((await new PreferencesStore(path).LoadAsync()).ThemeMode == ThemeMode.Light, "Selected theme persists in the existing JSON file");
            await settings.RestoreDefaultsAsync();
            check(settings.ThemeMode == ThemeMode.System && (await new PreferencesStore(path).LoadAsync()).ThemeMode == ThemeMode.System,
                "Restore Defaults restores and persists System mode");
            settings.ThemeMode = ThemeMode.Dark;
            Field<IslandInteractionController>(island, "_interaction").SetActive(false);
            settings.AnimationStyle = TransitionStyle.Instant;
            Invoke(island, "SetExpanded", false); StopMedia(); settings.AnimationSpeed = .5; settings.AnimationStyle = TransitionStyle.Smooth;
            Invoke(island, "SetExpanded", true); StopMedia(); await Task.Delay(60);
            var morph = Field<IslandMorph>(island, "_morph");
            var activeClocks = (System.Collections.IDictionary)Field<object>(Field<IslandTransitions>(island, "_transitions"), "_clocks");
            var animationKey = ((DependencyObject)morph, IslandMorph.ProgressProperty); var activeBefore = activeClocks[animationKey];
            settings.ThemeMode = ThemeMode.Light;
            check(activeBefore is not null && (ReferenceEquals(activeBefore, activeClocks[animationKey]) ||
                (!morph.HasAnimatedProperties && (double)morph.GetValue(IslandMorph.ProgressProperty) == 1)),
                "Theme switching retains the existing size-animation clock while allowing natural progress/completion");
            await Task.Delay(700);
            check(surface.VisualBounds.Size == new Size(400, settings.Current.HomeHeight) && !morph.HasAnimatedProperties,
                "Expansion still reaches the exact endpoint after a theme switch");
            check((GetWindowLong(hwnd, -20) & 0x08000088) == 0x08000088 && (GetWindowLong(hwnd, -20) & 0x40000) == 0,
                "Theme changes preserve NOACTIVATE, TOOLWINDOW, TOPMOST and Alt+Tab exclusion");
            check(changes > 0 && Application.Current.Resources.MergedDictionaries.Count == initialResources,
                "Repeated switching reuses one theme dictionary slot");
        }
        finally { window?.CloseImmediately(); island.Close(); await settings.FlushAsync(); File.Delete(path); }
        check(source.Disposed && ThemeService.Active is null && Application.Current.Resources.MergedDictionaries.Count == initialResources,
            "Closing the widget unsubscribes notifications and releases its theme resource slot");
    }
    private static double Luminance(Color color)
    {
        static double Linear(byte c) { var s = c / 255d; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T t) yield return t; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private static async Task Flush() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    private static void Save(Window window, string path, Color? desktop = null)
    {
        window.UpdateLayout(); var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        if (desktop is { } color)
        {
            var backdrop = new DrawingVisual();
            using (var drawing = backdrop.RenderOpen()) drawing.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
            image.Render(backdrop);
        }
        image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file);
    }
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;
    private static void Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    [DllImport("user32.dll")] private static extern nint GetActiveWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
