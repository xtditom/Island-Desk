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
using IslandDesk.Monitoring;
using IslandDesk.Presentation;
using IslandDesk.Settings;
using IslandDesk.Themes;
using IslandDesk.Tray;
using IslandDesk.Views;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using ThemeMode = IslandDesk.Settings.ThemeMode;

internal static class Step13Validation
{
    private sealed class Startup : IStartupRegistration { public bool IsEnabled() => false; public void SetEnabled(bool enabled) { } }
    public static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var path = Path.Combine(directory, "step13.json");
        var store = new PreferencesStore(path);
        foreach (var mode in Enum.GetValues<ExpansionMode>())
        {
            await File.WriteAllTextAsync(path, $"{{\"ExpansionMode\":\"{mode}\"}}");
            var migrated = await store.LoadAsync();
            var expectedMode = mode == ExpansionMode.Both ? ExpansionMode.Click : mode;
            check(migrated.ExpansionMode == expectedMode && migrated.CollapseMode == expectedMode && !migrated.Battery,
                $"Legacy {mode} preference maps to a selectable mode and defaults Battery off");
        }
        check((new AppSettings { CollapseMode = (ExpansionMode)999, CollapseDelayMilliseconds = -1 }).Normalize()
            is { CollapseMode: ExpansionMode.Click, CollapseDelayMilliseconds: 150 } &&
            (new AppSettings { CollapseDelayMilliseconds = 99999 }).Normalize().CollapseDelayMilliseconds == 600,
            "Collapse preferences validate the enum and safe 150–600 ms delay");
        await VerifyInteractions(check);
        await VerifyBattery(check);
        using var settings = new SettingsController(Application.Current.Dispatcher, store, new Startup());
        check(settings.ExpansionModes.Cast<ExpansionMode>().SequenceEqual(new[] { ExpansionMode.Hover, ExpansionMode.Click }) &&
            settings.Current is { ExpansionMode: ExpansionMode.Click, CollapseMode: ExpansionMode.Click },
            "Both interaction selectors offer only Hover and Click with valid defaults");
        settings.Update(new AppSettings { AnimationStyle = TransitionStyle.Instant, Battery = true,
            Cpu = false, Gpu = false, Ram = false, Disk = false, Network = false, HomeClock = false, HomeMedia = false, HomeVolume = false });
        var island = new MainWindow(false, settings);
        SettingsWindow? window = null;
        try
        {
            island.Show();
            var pillInteraction = Field<IslandInteractionController>(island, "_interaction");
            pillInteraction.SetActive(true);
            pillInteraction.SetInteractionInProgress(true);
            Invoke(island, "PillButton_Click", island.FindName("PillButton"), new RoutedEventArgs());
            check(Field<bool>(island, "_isExpanded"), "Captured collapsed-pill click expands while generic captures stay guarded");
            pillInteraction.SetInteractionInProgress(false);
            Invoke(island, "Surface_MouseLeftButtonUp", island.FindName("IslandSurface"),
                new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            check(!Field<bool>(island, "_isExpanded"), "Clicking the expanded background still collapses the island");
            pillInteraction.SetActive(false);
            ((UIElement)island.FindName("IslandSurface")).IsHitTestVisible = false;
            var services = Field<IslandServices>(island, "_services"); services.Dispose();
            var vm = Field<IslandViewModel>(island, "_viewModel");
            Invoke(island, "SetExpanded", true); Invoke(island, "SelectDashboard", Dashboard.System);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(Field<object?>(services, "_batteryCancellation") is not null && Field<object?>(services, "_hardwareCancellation") is null,
                "Battery-only System starts event monitoring without the hardware sampling timer");
            await Until(() => vm.Battery.Status != "Reading battery…");
            var nativeStatus = vm.Battery.Status;
            vm.Battery.Update(new(BatteryPresence.Present, 77, BatteryFlow.Charging));
            SendMessage(new WindowInteropHelper(island).Handle, 0x0218, 0xA, 0);
            await Until(() => vm.Battery.Status == nativeStatus);
            check(vm.Battery.Status == nativeStatus, "Owned HWND power notification reaches the async battery service and UI");
            var system = (SystemDashboard)island.FindName("SystemDashboard");
            island.UpdateLayout();
            check(system.BatteryCard.Visibility == Visibility.Visible && system.BatteryCard.ActualWidth == 370 && system.Height == 140 &&
                Field<Grid>(system, "Metrics").Visibility == Visibility.Collapsed,
                "Battery-only card is full width with exactly 96 DIP of added space");
            Invoke(island, "SetExpanded", false);
            check(Field<object?>(services, "_batteryCancellation") is null, "Collapsing cancels battery work immediately");
            Invoke(island, "SetExpanded", true); Invoke(island, "SelectDashboard", Dashboard.System);
            island.Hide(); check(Field<object?>(services, "_batteryCancellation") is null, "Hiding cancels battery work immediately");
            island.Show(); Invoke(island, "SelectDashboard", Dashboard.Main);
            check(Field<object?>(services, "_batteryCancellation") is null, "Home never runs battery monitoring");
            settings.Cpu = settings.Gpu = settings.Ram = settings.Disk = settings.Network = true;
            Invoke(island, "SelectDashboard", Dashboard.System); services.Dispose();
            vm.Battery.Update(new(BatteryPresence.Present, 63, BatteryFlow.Discharging, 8280)); island.UpdateLayout();
            var metrics = Field<Grid>(system, "Metrics");
            check(system.BatteryCard.TranslatePoint(new Point(), system).Y + system.BatteryCard.ActualHeight + 8 == metrics.TranslatePoint(new Point(), system).Y &&
                system.Height == 412 && Field<IslandSurface>(island, "IslandSurface").VisualBounds.Height == 488,
                "Battery sits above the unchanged CPU/GPU/RAM/Disk grid and fits the expanded island");
            foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
            {
                settings.ThemeMode = mode; services.Dispose();
                vm.Battery.Update(new(BatteryPresence.Present, 63, BatteryFlow.Discharging, 8280));
                Save(island, Path.Combine(directory, $"battery-{mode}.png"));
                vm.Battery.Update(new(BatteryPresence.None)); Save(island, Path.Combine(directory, $"no-battery-{mode}.png"));
            }
            settings.Battery = false; services.Dispose(); island.UpdateLayout();
            check(system.BatteryCard.Visibility == Visibility.Collapsed && system.Height == 316 &&
                metrics.TranslatePoint(new Point(), system).Y == 52, "Disabling Battery restores the original dashboard without a gap");

            var settingsClicks = 0; var titleClicks = 0; var exitClicks = 0;
            using var tray = new SettingsTrayMenu(settings, island.Theme, () => settingsClicks++, () => titleClicks++, () => { exitClicks++; return Task.CompletedTask; });
            var menu = tray.Menu;
            check(menu.Items.Count == 9 && menu.Items.OfType<Forms.ToolStripMenuItem>().Select(item => item.Text).SequenceEqual(
                new[] { "Settings…", "Clock Format", "Island Position", "Theme", "Animation FPS", "Island Title", "Exit" }), "Tray has organized shortcut submenus and Settings/Exit");
            Item(menu, "Settings…").PerformClick(); Item(menu, "Exit").PerformClick(); Child(menu, "Island Title", "Edit title…").PerformClick();
            check(settingsClicks == 1 && titleClicks == 1 && exitClicks == 1, "Settings, title editor and Exit callbacks fire once");
            Child(menu, "Clock Format", "12-hour").PerformClick(); check(!settings.Use24Hour && Child(menu, "Clock Format", "12-hour").Checked, "Tray selects 12-hour through shared settings");
            Child(menu, "Clock Format", "24-hour").PerformClick(); Child(menu, "Clock Format", "Show Seconds").PerformClick();
            check(settings.Use24Hour && settings.ShowSeconds && !Child(menu, "Clock Format", "12-hour").Checked, "Clock format and seconds update exclusive checkmarks");
            foreach (var alignment in Enum.GetValues<IslandAlignment>())
            {
                Child(menu, "Island Position", alignment.ToString()).PerformClick();
                check(settings.Alignment == alignment && Item(menu, "Island Position").DropDownItems.OfType<Forms.ToolStripMenuItem>().Count(item => item.Checked) == 1,
                    $"Tray {alignment} alignment updates the existing position service");
            }
            settings.Use24Hour = false; settings.ShowSeconds = false; settings.Alignment = IslandAlignment.Left;
            check(Child(menu, "Clock Format", "12-hour").Checked && !Child(menu, "Clock Format", "Show Seconds").Checked &&
                Child(menu, "Island Position", "Left").Checked, "Settings changes immediately synchronize tray checkmarks");
            settings.UpdateDisplayRefreshRate(100); var fps = Item(menu, "Animation FPS");
            check(fps.DropDownItems.OfType<Forms.ToolStripMenuItem>().Where(item => item.Enabled).Select(item => item.Text)
                .SequenceEqual(new[] { "30 FPS", "60 FPS", "90 FPS", "Max" }), "100 Hz tray exposes exactly 30/60/90/Max");
            Child(menu, "Animation FPS", "90 FPS").PerformClick();
            fps.DropDownItems.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text?.StartsWith("120 FPS") == true).PerformClick();
            check(settings.Current.AnimationFrameRate == 90 && Child(menu, "Animation FPS", "90 FPS").Checked, "Unavailable FPS cannot overwrite the shared selection");
            settings.UpdateDisplayRefreshRate(60);
            check(settings.Current.AnimationFrameRate is null && Child(menu, "Animation FPS", "Max").Checked &&
                !fps.DropDownItems.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text?.StartsWith("90 FPS") == true).Enabled,
                "Monitor refresh changes update availability and fall back to checked Max");
            foreach (var mode in Enum.GetValues<ThemeMode>())
            {
                Child(menu, "Theme", mode.ToString()).PerformClick(); services.Dispose();
                check(settings.ThemeMode == mode && Item(menu, "Theme").DropDownItems.OfType<Forms.ToolStripMenuItem>().Count(item => item.Checked) == 1,
                    $"Tray {mode} theme is synchronized with Settings");
                if (mode != ThemeMode.System) VerifyTrayRendering(tray, mode, directory, check);
            }
            island.OpenSettings(editTitle: true); window = Field<SettingsWindow>(island, "_settingsWindow");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var input = (TextBox)window.FindName("TitleInput");
            check(((TabControl)window.FindName("Categories")).SelectedIndex == 0 && input.IsKeyboardFocused && input.SelectionLength == input.Text.Length &&
                window.ShowActivated && window.ShowInTaskbar && (GetWindowLong(new WindowInteropHelper(island).Handle, -20) & 0x08000088) == 0x08000088,
                "Tray title route focuses/selects Settings title while the island remains non-activating and topmost");
            input.Text = new string('x', 25); check(settings.Title.Length == 20 && input.Text.Length == 20, "Focused title editor retains its 20-character limit");
            Save(window, Path.Combine(directory, "settings-general-interactions.png"));
            settings.DynamicBackground = true; settings.BackgroundTintStrength = 21; settings.BackgroundStyle = IslandBackgroundStyle.SolidDark; services.Dispose();
            var categories = (TabControl)window.FindName("Categories"); categories.SelectedIndex = 6; window.UpdateLayout();
            var contents = (StackPanel)((ScrollViewer)((TabItem)categories.SelectedItem).Content).Content;
            var controls = (StackPanel)((Border)contents.Children[4]).Child;
            check(!controls.Children[0].IsEnabled && !controls.Children[1].IsEnabled && controls.Children[2].Visibility == Visibility.Visible &&
                settings.DynamicBackground && settings.BackgroundTintStrength == 21 && Field<IslandSurface>(island, "IslandSurface").Background is SolidColorBrush,
                "Solid disables both background controls with an explanation, retains preferences, and has no dynamic gradient");
            ((ScrollViewer)((TabItem)categories.SelectedItem).Content).ScrollToEnd();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Save(window, Path.Combine(directory, "settings-solid-availability.png"));
            settings.BackgroundStyle = IslandBackgroundStyle.CharcoalGradient; services.Dispose();
            check(controls.Children[0].IsEnabled && controls.Children[1].IsEnabled && controls.Children[2].Visibility == Visibility.Collapsed &&
                settings.DynamicBackground && settings.BackgroundTintStrength == 21 && Field<IslandSurface>(island, "IslandSurface").Background is LinearGradientBrush,
                "Returning to Gradient immediately restores enabled controls and the saved tint preference");
            settings.ExpansionMode = ExpansionMode.Click; settings.CollapseMode = ExpansionMode.Hover; settings.CollapseDelayMilliseconds = 425; settings.Battery = true;
            services.Dispose(); await settings.FlushAsync(); var saved = await store.LoadAsync();
            check(saved is { ExpansionMode: ExpansionMode.Click, CollapseMode: ExpansionMode.Hover, CollapseDelayMilliseconds: 425, Battery: true,
                DynamicBackground: true, BackgroundTintStrength: 21, Alignment: IslandAlignment.Left, ShowSeconds: false, Use24Hour: false, ThemeMode: ThemeMode.Dark },
                "Independent gestures, battery, tray shortcuts and retained tint preferences persist together");
        }
        finally { window?.CloseImmediately(); island.Close(); await settings.FlushAsync(); File.Delete(path); }
    }

    private static async Task VerifyInteractions(Action<bool, string> check)
    {
        foreach (var expand in Enum.GetValues<ExpansionMode>()) foreach (var collapse in Enum.GetValues<ExpansionMode>())
        {
            var expanded = false;
            using var interaction = new IslandInteractionController(Application.Current.Dispatcher, () => expanded, value => expanded = value,
                new() { ExpandOnHover = expand != ExpansionMode.Click, ExpandOnClick = expand != ExpansionMode.Hover,
                    CollapseOnHover = collapse != ExpansionMode.Click, CollapseOnClick = collapse != ExpansionMode.Hover, CollapseDelay = TimeSpan.FromMilliseconds(150) });
            void Reset(bool value) { interaction.SetActive(false); expanded = value; interaction.SetActive(true); }
            Reset(false); interaction.PointerEntered(); check(expanded == (expand != ExpansionMode.Click), $"{expand}/{collapse}: hover expansion is independent");
            Reset(false); interaction.Click(); check(expanded == (expand != ExpansionMode.Hover), $"{expand}/{collapse}: click expansion is independent");
            Reset(true); interaction.PointerEntered(false); interaction.Click(); check(expanded == (collapse == ExpansionMode.Hover), $"{expand}/{collapse}: click collapse is independent");
            Reset(true); interaction.PointerEntered(false); interaction.PointerLeft(); await Task.Delay(210);
            check(expanded == (collapse == ExpansionMode.Click), $"{expand}/{collapse}: delayed hover collapse is independent");
        }
        var open = true;
        using var policy = new IslandInteractionController(Application.Current.Dispatcher, () => open, value => open = value, new() { CollapseDelay = TimeSpan.FromMilliseconds(150) });
        policy.SetActive(true); policy.PointerEntered(); policy.SetInteractionInProgress(true); policy.PointerLeft(); policy.Click(); await Task.Delay(210);
        check(open, "Mouse capture/interactive gesture blocks collapse and click toggles");
        policy.SetInteractionInProgress(false); await Task.Delay(50); policy.PointerEntered(); await Task.Delay(210);
        check(open, "Re-entry cancels a pending collapse without flicker");
        policy.Click(); policy.PointerEntered(); policy.Configure(new()); policy.PointerEntered();
        check(!open, "Click-close stays closed for the same pointer visit even across policy updates");
        policy.PointerLeft(); policy.PointerEntered(); check(open, "Next pointer visit permits hover expansion again");
    }

    private static async Task VerifyBattery(Action<bool, string> check)
    {
        static BatteryReading Parse(byte ac, byte flag, byte percent, uint seconds = uint.MaxValue) =>
            BatteryStatusMonitor.FromNative(new() { AcLineStatus = ac, BatteryFlag = flag, BatteryLifePercent = percent, BatteryLifeTime = seconds });
        check(Parse(0, 1, 63, 8280) is { Presence: BatteryPresence.Present, Percent: 63, Flow: BatteryFlow.Discharging, RemainingSeconds: 8280 }, "Native battery reports valid discharge percentage and estimate");
        check(Parse(1, 8, 42, 1000) is { Flow: BatteryFlow.Charging, RemainingSeconds: null } && Parse(1, 1, 100).Flow == BatteryFlow.Full &&
            Parse(1, 1, 85).Flow == BatteryFlow.PluggedIn, "Charging, full and plugged-in states suppress invalid discharge estimates");
        check(Parse(1, 128, 100).Presence == BatteryPresence.None && Parse(255, 255, 255).Presence == BatteryPresence.Unavailable,
            "No battery and unknown 255 flags are distinguished without false charging/no-battery data");
        check(Parse(0, 0, 255) is { Percent: null, RemainingSeconds: null, Flow: BatteryFlow.Discharging }, "Unknown percentage and lifetime remain unavailable");
        var vm = new BatteryViewModel(); check(vm.Value == "—" && !vm.HasPercentage, "Battery initializes without a false zero");
        vm.Update(Parse(0, 1, 63, 8280)); check(vm.Value == "63%" && vm.Status == "Discharging" && vm.Remaining == "2 hr 18 min remaining", "Battery card formats native readings and remaining time");
        vm.Update(Parse(1, 128, 255)); check(vm.Status == "No battery detected" && vm.Value == "—" && !vm.HasPercentage && vm.Remaining == "", "Desktop no-battery card shows no invented percentage or estimate");
        var native = BatteryStatusMonitor.Read(); Console.WriteLine($"Native power reading: {native}");
        check(native.Percent is null or >= 0 and <= 100, "Actual Windows power API returns a bounded or unavailable percentage");
        var reads = 0; var publications = 0;
        var monitor = new BatteryStatusMonitor(() => { Interlocked.Increment(ref reads); return Parse(0, 1, 60); });
        for (var i = 0; i < 100; i++) monitor.RequestRefresh();
        using var cancellation = new CancellationTokenSource();
        var run = monitor.RunAsync(0, _ => { Interlocked.Increment(ref publications); return Task.CompletedTask; }, cancellation.Token);
        await Until(() => Volatile.Read(ref publications) >= 1);
        await Task.Delay(120); check(reads == 1, "Power event bursts coalesce and there is no background sampling loop");
        monitor.PowerChanged(0xA, 0); await Until(() => Volatile.Read(ref publications) == 2);
        check(reads == 2, "Power-status notification requests one asynchronous refresh");
        cancellation.Cancel(); try { await run; } catch (OperationCanceledException) { }
        monitor.RequestRefresh(); await Task.Delay(50); check(reads == 2, "Canceled battery sessions perform no later native reads");
    }

    private static void VerifyTrayRendering(SettingsTrayMenu tray, ThemeMode mode, string directory, Action<bool, string> check)
    {
        var submenu = Item(tray.Menu, "Clock Format"); var strip = submenu.DropDown;
        strip.CreateControl(); strip.PerformLayout();
        var item = Child(tray.Menu, "Clock Format", "12-hour"); item.Select();
        using var bitmap = new Drawing.Bitmap(item.Width, item.Height);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            tray.Menu.Renderer.DrawMenuItemBackground(new Forms.ToolStripItemRenderEventArgs(graphics, item));
            tray.Menu.Renderer.DrawItemText(new Forms.ToolStripItemTextRenderEventArgs(graphics, item, item.Text,
                new Drawing.Rectangle(28, 0, bitmap.Width - 38, bitmap.Height), item.ForeColor, item.Font, Forms.TextFormatFlags.Left | Forms.TextFormatFlags.VerticalCenter));
            tray.Menu.Renderer.DrawItemCheck(new Forms.ToolStripItemImageRenderEventArgs(graphics, item, new Drawing.Rectangle(6, (item.Height - 16) / 2, 16, 16)));
        }
        var expected = ((SolidColorBrush)Application.Current.FindResource("PopupHover")).Color;
        var selected = bitmap.GetPixel(bitmap.Width - 12, bitmap.Height / 2);
        check(ReferenceEquals(strip.Renderer, tray.Menu.Renderer) && selected.R == expected.R && selected.G == expected.G && selected.B == expected.B &&
            ((Forms.ToolStripDropDownMenu)strip).ShowCheckMargin && !((Forms.ToolStripDropDownMenu)strip).ShowImageMargin,
            $"{mode} nested tray selection uses themed hover and a clean checkmark margin");
        var text = ((SolidColorBrush)Application.Current.FindResource("Primary")).Color;
        check(Enumerable.Range(4, 21).SelectMany(x => Enumerable.Range(0, item.Height).Select(y => bitmap.GetPixel(x, y))).Any(pixel =>
            Math.Abs(pixel.R - text.R) + Math.Abs(pixel.G - text.G) + Math.Abs(pixel.B - text.B) < 20), $"{mode} submenu checkmark has readable themed foreground");
        bitmap.Save(Path.Combine(directory, $"tray-submenu-{mode}.png"));
    }
    public static void VerifyIconAssets(Action<bool, string> check)
    {
        check(ApplicationIconAssets.WindowIcon is { IsFrozen: true, PixelWidth: > 0 }, "Provided ICO is available as a cached frozen WPF resource");
        using var tray = ApplicationIconAssets.CreateTrayIcon(); check(tray is not null && tray.Handle != 0, "Provided ICO loads for NotifyIcon without a loose output file");
        using var executable = Drawing.Icon.ExtractAssociatedIcon(Path.Combine(AppContext.BaseDirectory, "IslandDesk.exe"));
        using var expected = tray!.ToBitmap(); using var actual = executable!.ToBitmap();
        check(expected.Size == actual.Size && Enumerable.Range(0, expected.Width).All(x => Enumerable.Range(0, expected.Height).All(y => expected.GetPixel(x, y) == actual.GetPixel(x, y))),
            "Published executable icon matches the embedded tray icon pixel for pixel");
        using var settings = new SettingsController(Application.Current.Dispatcher, startup: new Startup());
        var window = new SettingsWindow(settings);
        check(ReferenceEquals(window.Icon, ApplicationIconAssets.WindowIcon), "Settings/taskbar representation uses the shared custom icon"); window.CloseImmediately();
    }
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 100 && !ready(); i++) await Task.Delay(20); if (!ready()) throw new TimeoutException("Expected asynchronous test publication."); }
    private static Forms.ToolStripMenuItem Item(Forms.ContextMenuStrip menu, string text) => menu.Items.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text == text);
    private static Forms.ToolStripMenuItem Child(Forms.ContextMenuStrip menu, string parent, string text) => Item(menu, parent).DropDownItems.OfType<Forms.ToolStripMenuItem>().Single(item => item.Text == text);
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void Invoke(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);
    private static void Save(Window window, string path)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
