using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using IslandDesk;
using IslandDesk.Interaction;
using IslandDesk.Monitoring;
using IslandDesk.Presentation;
using IslandDesk.Settings;
using IslandDesk.Views;
using IslandDesk.Windowing;

internal static class SettingsValidation
{
    private sealed class Startup : IStartupRegistration
    {
        public bool Enabled, Fail;
        public bool IsEnabled() => Enabled;
        public void SetEnabled(bool value) { if (Fail) throw new UnauthorizedAccessException("Test failure"); Enabled = value; }
    }
    public static async Task VerifyAsync(string artifacts, Action<bool, string> check)
    {
        var path = Path.Combine(artifacts, "settings-test.json");
        File.Delete(path);
        var store = new PreferencesStore(path);
        var defaults = new AppSettings();
        check(await store.LoadAsync() == defaults, "Missing settings recover to defaults");
        await File.WriteAllTextAsync(path, "{bad JSON");
        check(await store.LoadAsync() == defaults, "Corrupt settings recover safely");
        await File.WriteAllTextAsync(path, "{\"Title\":\"My Island\"}");
        check((await store.LoadAsync(28)) is { Title: "My Island", ManualTopOffset: 28, HomeMedia: true }, "Title and legacy positioning migrate");
        var invalid = defaults with { SurfaceOpacity = 200, AnimationSpeed = -.5, Alignment = (IslandAlignment)99, XOffset = -20000, YOffset = 20000, AnimationFrameRate = 999, Title = new string('x', 30) };
        check(invalid.Normalize() is { SurfaceOpacity: 100, AnimationSpeed: .5, Alignment: IslandAlignment.Center, XOffset: -10000, YOffset: 10000, AnimationFrameRate: null, Title.Length: 20 }, "Settings validate limits and enum values");
        File.Delete(path);
        var startup = new Startup();
        using var settings = new SettingsController(Application.Current.Dispatcher, store, startup);
        await settings.InitializeAsync();
        check(settings.ExpansionModes.Cast<ExpansionMode>().SequenceEqual(new[] { ExpansionMode.Hover, ExpansionMode.Click }) &&
            settings.Current is { ExpansionMode: ExpansionMode.Click, CollapseMode: ExpansionMode.Click },
            "Both interaction selectors offer only Hover and Click with a valid default selection");
        for (var i = 60; i < 90; i++) settings.SurfaceOpacity = i;
        check(!File.Exists(path), "Rapid sliders are debounced before disk writes");
        await Task.Delay(650);
        check((await store.LoadAsync()).SurfaceOpacity == 89 && !File.Exists(path + ".tmp"), "Debounced preferences save atomically");
        settings.LaunchOnStartup = true; await settings.FlushAsync();
        check(startup.Enabled && settings.LaunchOnStartup && (await store.LoadAsync()).LaunchOnStartup, "Startup change is applied and saved");
        startup.Fail = true; settings.LaunchOnStartup = false; await settings.FlushAsync();
        check(settings.LaunchOnStartup && settings.Status.Contains("Couldn't change"), "Startup failure rolls back and reports error");
        await settings.RestoreDefaultsAsync();
        check(settings.LaunchOnStartup && settings.Status.Contains("couldn't be removed"), "Restore Defaults reports a failed startup removal honestly");
        startup.Fail = false;
        settings.Update(defaults with { Title = "Persistent", HomeMedia = false, Gpu = false, ShowSeconds = true, XOffset = -50, YOffset = 50, AnimationFrameRate = 90 });
        await settings.FlushAsync();
        using (var fresh = new SettingsController(Application.Current.Dispatcher, store, startup))
        {
            await fresh.InitializeAsync();
            check(fresh.Current is { Title: "Persistent", HomeMedia: false, Gpu: false, ShowSeconds: true, XOffset: -50, YOffset: 50, AnimationFrameRate: 90, LaunchOnStartup: true }, "New controller restores signed offsets, FPS, and existing preferences");
        }
        await settings.RestoreDefaultsAsync();
        check(settings.Current == defaults && !startup.Enabled && await store.LoadAsync() == defaults, "Restore Defaults reverses startup and persists defaults");
        settings.UpdateDisplayRefreshRate(100);
        check(settings.FrameRateOptions.Where(option => option.IsAvailable).Select(option => option.Rate).SequenceEqual(new int?[] { 30, 60, 90, null }), "100 Hz permits 30, 60, 90, Max and disables higher targets");
        settings.SelectedFrameRate = settings.FrameRateOptions.Single(option => option.Rate == 90);
        settings.SelectedFrameRate = settings.FrameRateOptions.Single(option => option.Rate == 120);
        check(settings.Current.AnimationFrameRate == 90 && settings.EffectiveFrameRate == 90, "Unavailable FPS cannot be selected");
        settings.UpdateDisplayRefreshRate(60); await settings.FlushAsync();
        check(settings.Current.AnimationFrameRate is null && settings.EffectiveFrameRate == 60 && (await store.LoadAsync()).AnimationFrameRate is null, "Refresh change falls back to Max and persists it");
        settings.UpdateDisplayRefreshRate(null);
        check(settings.FrameRateOptions.Count(option => option.IsAvailable) == 1 && settings.EffectiveFrameRate is null, "Unknown refresh safely uses Max with native WPF pacing");
        using (var transitions = new IslandTransitions())
        {
            var target = new TranslateTransform(); var completed = 0;
            transitions.ConfigureFrameRate(30);
            transitions.Animate(target, TranslateTransform.XProperty, 100, 600, () => completed++);
            await Task.Delay(150); var progress = target.X;
            transitions.ConfigureFrameRate(60);
            check(progress > 0 && progress < 100 && Math.Abs(target.X - progress) < 3, "Changing active animation FPS preserves elapsed progress");
            await Task.Delay(650);
            check(target.X == 100 && !target.HasAnimatedProperties && completed == 1, "Retimed animation completes once and removes its clock");
        }
        check(StartupRegistration.BuildCommand(@"C:\Program Files\IslandDesk\IslandDesk.exe", "unused") == "\"C:\\Program Files\\IslandDesk\\IslandDesk.exe\"", "Startup executable path is quoted");
        check(StartupRegistration.BuildCommand(@"C:\Program Files\dotnet\dotnet.exe", @"C:\Island Desk\IslandDesk.dll").EndsWith("\"C:\\Island Desk\\IslandDesk.dll\""), "Dotnet-hosted startup includes quoted DLL path");
        var registration = new StartupRegistration("IslandDesk.Validation." + Guid.NewGuid().ToString("N"), "\"" + Path.Combine(Path.GetDirectoryName(typeof(App).Assembly.Location)!, "IslandDesk.exe") + "\"");
        try
        {
            check(!registration.IsEnabled(), "Isolated current-user startup value starts absent");
            registration.SetEnabled(true); check(registration.IsEnabled(), "Native current-user startup registration works");
        }
        finally { registration.SetEnabled(false); }
        check(!registration.IsEnabled(), "Native startup registration is reversible and cleaned up");
        var blocker = Path.Combine(artifacts, "settings-blocker");
        await File.WriteAllTextAsync(blocker, "This file blocks creating a directory with the same name.");
        try
        {
            using var blocked = new SettingsController(Application.Current.Dispatcher, new PreferencesStore(Path.Combine(blocker, "preferences.json")), new Startup());
            blocked.SurfaceOpacity = 75;
            check(!await blocked.FlushAsync() && blocked.Status.Contains("Couldn't save"), "File-write failure is reported without crashing or claiming a save");
        }
        finally { File.Delete(blocker); }

        var island = new MainWindow(enableFullscreenDetection: false, settings);
        try
        {
            island.Show();
            var surface = (Border)island.FindName("IslandSurface"); surface.IsHitTestVisible = false;
            await Task.Delay(350);
            var hwnd = new WindowInteropHelper(island).Handle;
            Console.WriteLine($"Detected island display: {settings.MonitorRefreshRate?.ToString() ?? "unavailable"} Hz");
            check(settings.MonitorRefreshRate == MonitorRefreshReader.Read(MonitorFromWindow(hwnd, 2)), "Frame rate uses the widget's current monitor");
            var vm = Field<IslandViewModel>(island, "_viewModel");
            var services = Field<IslandServices>(island, "_services");
            settings.AnimationStyle = TransitionStyle.Instant;
            Invoke(island, "SetExpanded", true);
            check(Math.Abs(island.ActualHeight - 16 - new AppSettings().HomeHeight) < 1 && !Field<IslandMorph>(island, "_morph").HasAnimatedProperties, "Instant expansion immediately sets final native size including the shadow gutter");
            island.OpenSettings(); await Task.Delay(150);
            var window = Field<SettingsWindow>(island, "_settingsWindow");
            var text = (TextBox)window.FindName("TitleInput");
            window.Activate(); text.Focus();
            check(window.Owner is null && window.ShowInTaskbar && window.ShowActivated && Keyboard.FocusedElement == text, "Settings accepts keyboard input independently of widget");
            text.Text = "My "; text.AppendText("Island");
            check(settings.Title == "My Island", "Title editing preserves spaces between words");
            text.Text = new string('x', 25);
            check(text.Text.Length == 20 && settings.Title.Length == 20, "Settings title editor enforces the character limit");
            check((GetWindowLong(new WindowInteropHelper(window).Handle, -20) & 0x08000080) == 0, "Settings has neither NOACTIVATE nor TOOLWINDOW styles");
            var general = (TabItem)((TabControl)window.FindName("Categories")).Items[0];
            var gestureSelectors = Descendants<ComboBox>((DependencyObject)general.Content)
                .Where(item => System.Windows.Automation.AutomationProperties.GetName(item) is "Expansion mode" or "Collapse mode").ToArray();
            check(gestureSelectors.Length == 2 && gestureSelectors.All(item =>
                item.ItemsSource is IEnumerable<ExpansionMode> values && values.SequenceEqual(new[] { ExpansionMode.Hover, ExpansionMode.Click })) &&
                gestureSelectors.Any(item => System.Windows.Automation.AutomationProperties.GetName(item) == "Expansion mode") &&
                gestureSelectors.Any(item => System.Windows.Automation.AutomationProperties.GetName(item) == "Collapse mode"),
                "Expand with and Collapse with dropdowns each contain only Hover and Click");
            var toggle = Descendants<CheckBox>((DependencyObject)general.Content).Single(item => Equals(item.Content, "Hide during fullscreen apps"));
            toggle.Focus(); window.UpdateLayout();
            var row = (Border)toggle.Template.FindName("Focus", toggle);
            var focusPreview = new Control { Style = toggle.FocusVisualStyle, Width = 300, Height = 42 };
            focusPreview.ApplyTemplate();
            var ring = Descendants<Border>(focusPreview).Single();
            check(toggle.Focusable && toggle.IsTabStop && toggle.IsKeyboardFocused && ((SolidColorBrush)row.BorderBrush).Color.A == 0 && ring.Width == 38 && ring.CornerRadius.TopLeft == 12,
                "Toggle stays keyboard focusable with switch-sized focus visual and no row outline");
            var active = GetActiveWindow();
            settings.Title = "A custom island"; settings.SurfaceOpacity = 60; settings.AlwaysOnTop = false;
            check(vm.Title == "A custom island" && surface.Opacity == 1 && SurfaceAlpha(surface.Background, 153), "Title and brush opacity apply without dimming content");
            check((GetWindowLong(hwnd, -20) & 8) == 0 && !island.Topmost && GetActiveWindow() == active, "Turning topmost off releases native Z-order without activation");
            settings.AlwaysOnTop = true;
            check((GetWindowLong(hwnd, -20) & 0x08000088) == 0x08000088 && GetActiveWindow() == active, "Turning topmost on retains widget styles and Settings focus");
            settings.Use24Hour = false; settings.ShowSeconds = true;
            check(vm.Clock.Time.Contains(':') && vm.Clock.Time.Count(c => c == ':') == 2 && vm.Clock.IsRunning, "Clock format and seconds apply live");
            Invoke(island, "SetExpanded", false);
            island.UpdateLayout();
            var clockPill = (FrameworkElement)island.FindName("ClockPill");
            check(clockPill.DesiredSize.Width < island.ActualWidth - 8 && island.ActualWidth > 106, "Collapsed pill grows only as needed for 12-hour seconds");
            settings.ExpansionMode = ExpansionMode.Click; settings.CollapseMode = ExpansionMode.Click;
            var interaction = Field<IslandInteractionController>(island, "_interaction");
            interaction.PointerLeft(); interaction.PointerEntered(); check(!Field<bool>(island, "_isExpanded"), "Click-only mode disables hover expansion");
            interaction.Click(); interaction.PointerLeft(); await Task.Delay(300);
            check(Field<bool>(island, "_isExpanded"), "Click-only mode stays open after pointer leaves");
            interaction.Click(); settings.ExpansionMode = ExpansionMode.Hover; settings.CollapseMode = ExpansionMode.Hover; interaction.Click();
            check(!Field<bool>(island, "_isExpanded"), "Hover-only mode ignores expansion clicks");
            interaction.PointerEntered(); check(Field<bool>(island, "_isExpanded"), "Hover-only mode expands on entry");
            interaction.SetInteractionInProgress(true);
            settings.ExpansionMode = ExpansionMode.Click; settings.CollapseMode = ExpansionMode.Click;

            settings.HomeClock = false; settings.HomeMedia = false; settings.HomeVolume = false;
            check((island.ActualHeight - 16) == 76 && ((MainDashboard)island.FindName("MainDashboard")).Height == 0 && !Field<bool>(vm.Clock, "_timeVisible"), "All hidden Home widgets reclaim space; only header date updates remain");
            settings.HomeMedia = true;
            check((island.ActualHeight - 16) == 76 + settings.Current.MediaHeight && ((MainDashboard)island.FindName("MainDashboard")).MediaSection.Visibility == Visibility.Visible, "Single Home media card has no empty gaps");
            settings.Update(settings.Current with { Cpu = false, Gpu = false, Ram = false, Disk = false, Network = false });
            Invoke(island, "SelectDashboard", Dashboard.System);
            check((island.ActualHeight - 16) == 120 && Field<object?>(services, "_hardwareCancellation") is null, "All hidden sensors shrink System and stop monitoring");
            settings.Ram = true;
            var system = (SystemDashboard)island.FindName("SystemDashboard");
            check((island.ActualHeight - 16) == 216 && Grid.GetColumnSpan(system.RamCard) == 3 && Field<MonitoringSelection>(services, "_selection") == new MonitoringSelection(false, false, true, false, false), "One sensor spans the row and samples only RAM");
            settings.Network = true;
            check((island.ActualHeight - 16) == 296 && system.NetworkCard.Visibility == Visibility.Visible, "Network-only additions have correct compact height");
            settings.Cpu = true; settings.Disk = true;
            check((island.ActualHeight - 16) == 392 && system.GpuCard.Visibility == Visibility.Collapsed && Grid.GetColumnSpan(system.DiskCard) == 3, "Odd metric count reflows without a blank tile");
            settings.Alignment = IslandAlignment.Left;
            var left = island.Left; Invoke(island, "SetExpanded", false);
            check(Math.Abs(island.Left - left) < 1, "Left alignment preserves edge during expand/collapse");
            settings.Alignment = IslandAlignment.Right;
            var right = island.Left + island.ActualWidth; Invoke(island, "SetExpanded", true);
            check(Math.Abs(island.Left + island.ActualWidth - right) < 1, "Right alignment preserves edge during expand/collapse");
            settings.YOffset = 10000; settings.XOffset = -10000;
            check(island.Top >= 0 && island.Top + island.ActualHeight <= SystemParameters.PrimaryScreenHeight + 1 && island.Left >= 0,
                "Extreme signed offsets stay accessible inside monitor bounds");
            settings.Update(settings.Current with { Alignment = IslandAlignment.Center, XOffset = 0, YOffset = 50, UseWorkingArea = true });
            var baseLeft = island.Left; var baseTop = island.Top;
            var dpi = VisualTreeHelper.GetDpi(island);
            settings.XOffset = -50; settings.YOffset = 100;
            check(Math.Abs(island.Left - baseLeft + 50 / dpi.DpiScaleX) < 1 && Math.Abs(island.Top - baseTop - 50 / dpi.DpiScaleY) < 1 && settings.UseWorkingArea,
                "Independent signed X/Y apply in physical pixels with working area enabled");
            settings.YOffset = -50;
            check(settings.YOffset == -50 && island.Top >= 0, "Negative Y is accepted and clamped at the monitor edge");
            settings.UseWorkingArea = false;
            check(settings.XOffset == -50 && settings.YOffset == -50, "Working-area mode preserves manual offsets");

            var categories = (TabControl)window.FindName("Categories");
            categories.SelectedIndex = 3; window.UpdateLayout();
            ((TextBox)window.FindName("XOffsetInput")).Text = "+50";
            ((TextBox)window.FindName("YOffsetInput")).Text = "-50";
            check(settings.XOffset == 50 && settings.YOffset == -50, "Signed offset text boxes accept explicit plus and minus signs");
            categories.SelectedIndex = 1; window.UpdateLayout();
            var picker = (ComboBox)window.FindName("FrameRatePicker");
            picker.IsDropDownOpen = true; await Task.Delay(70);
            foreach (var option in settings.FrameRateOptions.Where(option => !option.IsAvailable))
            {
                var container = (ComboBoxItem)picker.ItemContainerGenerator.ContainerFromItem(option);
                check(!container.IsEnabled && container.Opacity < 1 && option.Label.Contains("Unavailable"), $"{option.Rate} FPS is visibly unavailable in the dropdown");
            }
            picker.IsDropDownOpen = false;
            var volumeSession = Field<object?>(services, "_volumeCancellation");
            var clockOptions = Field<ClockOptions>(vm.Clock, "_options");
            settings.SelectedFrameRate = settings.FrameRateOptions.First(option => option.IsAvailable);
            check(Field<IslandTransitions>(island, "_transitions").DesiredFrameRate == settings.EffectiveFrameRate &&
                ((IslandDesk.Controls.Equalizer)island.FindName("MusicEqualizer")).DesiredFrameRate == settings.EffectiveFrameRate &&
                ReferenceEquals(volumeSession, Field<object?>(services, "_volumeCancellation")) && ReferenceEquals(clockOptions, Field<ClockOptions>(vm.Clock, "_options")),
                "FPS applies to transition/equalizer timelines without restarting clock or audio services");

            settings.Update(defaults); await settings.FlushAsync();
            services.Dispose(); vm.Media.SetActive(true); vm.Media.Update(new("Midnight City", "M83", "Playing", true, true, true, true));
            vm.Volume.SetActive(true); vm.Volume.Update(new(64, false, true));
            foreach (var index in Enumerable.Range(0, 5))
            {
                ((TabControl)window.FindName("Categories")).SelectedIndex = index;
                window.UpdateLayout(); await Task.Delay(50);
                Render(window, Path.Combine(artifacts, $"settings-{index}.png"));
            }
            window.Height = 480; window.UpdateLayout();
            var widgetScroll = (ScrollViewer)((TabItem)((TabControl)window.FindName("Categories")).SelectedItem).Content;
            widgetScroll.ScrollToEnd(); window.UpdateLayout();
            check(widgetScroll.VerticalOffset > 0 && widgetScroll.ScrollableHeight > 0, "Settings categories remain scrollable at compact height");
            Render(window, Path.Combine(artifacts, "settings-compact.png"));
            window.Close(); await Task.Delay(100);
            check(Field<SettingsWindow?>(island, "_settingsWindow") is null && (GetWindowLong(hwnd, -20) & 0x08000088) == 0x08000088, "Closing Settings leaves widget styles unchanged");
            island.OpenSettings(); await Task.Delay(50);
            check(Field<SettingsWindow>(island, "_settingsWindow").IsVisible, "Settings remains reopenable after all widget configurations");
        }
        finally { island.Close(); await settings.FlushAsync(); File.Delete(path); }
    }
    private static void Render(Window window, string path)
    {
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject source) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++)
        {
            var child = VisualTreeHelper.GetChild(source, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static bool SurfaceAlpha(Brush brush, byte alpha) => brush is SolidColorBrush solid ? solid.Color.A == alpha : brush is LinearGradientBrush gradient && gradient.GradientStops.All(stop => stop.Color.A == alpha);
    private static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
    private static void Invoke(object source, string name, params object[] args) => source.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(source, args);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] private static extern nint GetActiveWindow();
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
}

