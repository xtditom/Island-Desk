using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IslandDesk;
using IslandDesk.Audio;
using IslandDesk.Controls;
using IslandDesk.Interaction;
using IslandDesk.Media;
using IslandDesk.Monitoring;
using IslandDesk.Presentation;
using IslandDesk.Views;
using IslandDesk.Windowing;
using IslandDesk.Settings;

internal static class Program
{
    private static readonly string Artifacts = Path.Combine(AppContext.BaseDirectory, "artifacts");
    private static int _checks;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--deployment")
            return DeploymentValidation.Run(args[1]);
        if (args.Length == 2 && args[0] == "--deployment-child")
        {
            using var instance = IslandDesk.Infrastructure.SingleInstanceGuard.TryAcquire(args[1]);
            return instance is null ? 17 : 0;
        }
        if (args.Length == 4 && args[0] == "--gpu-probe")
            return DiagnosticGpuSampler.Probe(int.Parse(args[1]), int.Parse(args[2]), args[3]);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources = new ResourceDictionary { Source = new Uri("/IslandDesk;component/Themes/ApplicationTheme.xaml", UriKind.Relative) };
        app.DispatcherUnhandledException += (_, e) => { Console.Error.WriteLine(e.Exception); Environment.ExitCode = 1; app.Shutdown(); };
        if (args.Length >= 2 && args[0] == "--fullscreen-provider")
        { FullscreenValidation.RunProvider(app, new nint(long.Parse(args[1]))); return Environment.ExitCode; }
        if (args.Contains("--perf"))
        {
            var widget = new MainWindow(enableFullscreenDetection: false);
            var started = false;
            widget.ContentRendered += async (_, _) =>
            {
                if (started) return; started = true;
                try
                {
                    ((Border)widget.FindName("IslandSurface")).IsHitTestVisible = false;
                    Console.WriteLine($"Rendering tier {RenderCapability.Tier >> 16}; logical processors {Environment.ProcessorCount}");
                    await Task.Delay(5000);
                    var media = Field<IslandViewModel>(widget, "_viewModel").Media;
                    await MeasureAsync(media.IsPlaying ? "native collapsed music" : "native collapsed clock", 8, widget);
                    Field<IslandServices>(widget, "_services").Dispose();
                    media.SetActive(true); media.Update(MediaSnapshot.Empty);
                    await Task.Delay(1500);
                    await MeasureAsync("isolated clock (media subscriptions off)", 8, widget);
                    widget.Hide(); await Task.Delay(300);
                    await MeasureAsync("isolated hidden", 5, widget);
                    widget.Show(); await Task.Delay(500);
                    Field<IslandServices>(widget, "_services").Dispose();
                    media.SetActive(true); media.Update(new("Performance sample", "", "Playing", true));
                    await Task.Delay(1500);
                    await MeasureAsync("isolated equalizer", 8, widget);
                    ((Equalizer)widget.FindName("MusicEqualizer")).IsRunning = false;
                    await Task.Delay(500);
                    await MeasureAsync("isolated static music", 5, widget);
                }
                catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
                finally { widget.Close(); app.Shutdown(); }
            };
            widget.Show(); app.Run(); return Environment.ExitCode;
        }
        var normal = new Window { Width = 280, Height = 100, Left = 40, Top = 200, Title = "IslandDesk validation", Content = new TextBox { Text = "Focus stays here", IsReadOnly = true } };
        normal.ContentRendered += async (_, _) =>
        {
            try
            {
                if (args.Contains("--performance-audit"))
                { Directory.CreateDirectory(Artifacts); await PerformanceAudit.RunAsync(Artifacts); }
                else if (args.Contains("--clock-lifetime"))
                { Directory.CreateDirectory(Artifacts); await PerformanceAudit.RecordClockProbeAsync(Artifacts); }
                else if (args.Contains("--performance-regression"))
                { Directory.CreateDirectory(Artifacts); await PerformanceAudit.VerifyRegressionsAsync(Check); }
                else if (args.Contains("--sensor-profile"))
                { Directory.CreateDirectory(Artifacts); await PerformanceAudit.ProfileSensorsAsync(Artifacts); }
                else if (args.Contains("--stability-audit"))
                { Directory.CreateDirectory(Artifacts); await PerformanceAudit.VerifyStabilityAsync(Artifacts, Check); }
                else if (args.Contains("--step13"))
                { Directory.CreateDirectory(Artifacts); await Step13Validation.RunAsync(Artifacts, Check); }
                else if (args.Contains("--icon-assets")) Step13Validation.VerifyIconAssets(Check);
                else if (args.Contains("--visual-fixes") || args.Contains("--visual-fix-probe"))
                { Directory.CreateDirectory(Artifacts); await VisualFixValidation.RunAsync(Artifacts, Check, args.Contains("--visual-fix-probe")); }
                else if (args.Contains("--themes"))
                { Directory.CreateDirectory(Artifacts); await ThemeValidation.RunAsync(Artifacts, Check); }
                else if (args.Contains("--advanced-media"))
                { Directory.CreateDirectory(Artifacts); await AdvancedMediaValidation.RunAsync(Artifacts, Check); }
                else if (args.Contains("--media"))
                { Directory.CreateDirectory(Artifacts); await MediaValidation.VerifyAsync(Artifacts, Check); }
                else if (args.Contains("--settings"))
                { Directory.CreateDirectory(Artifacts); await SettingsValidation.VerifyAsync(Artifacts, Check); }
                else if (args.Contains("--animation-profile"))
                { Directory.CreateDirectory(Artifacts); await AnimationProfile.RunAsync(Artifacts); }
                else if (args.Contains("--animation-check"))
                { Directory.CreateDirectory(Artifacts); await AnimationValidation.RunAsync(Artifacts, Check); }
                else if (args.Contains("--fullscreen")) await FullscreenValidation.VerifyNativeAsync(Check);
                else await VerifyAsync(normal);
                Console.WriteLine($"PASS: {_checks} checks. Renders: {Artifacts}");
            }
            catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
            finally { app.Shutdown(); }
        };
        normal.Show(); app.Run();
        return Environment.ExitCode;
    }

    private static async Task VerifyAsync(Window normal)
    {
        Directory.CreateDirectory(Artifacts);
        // Fullscreen integration runs in its own live, separate-process suite.
        var island = new MainWindow(enableFullscreenDetection: false);
        try
        {
            normal.Activate(); ((TextBox)normal.Content).Focus();
            var focused = Keyboard.FocusedElement;
            var active = GetActiveWindow();
            island.Show();
            var surface = (Border)island.FindName("IslandSurface");
            surface.IsHitTestVisible = false; // Real pointer location must not influence deterministic checks.
            await Task.Delay(500);
            var hwnd = new WindowInteropHelper(island).Handle;
            Check((GetWindowLong(hwnd, -20) & 0x08000088) == 0x08000088, "NOACTIVATE, TOOLWINDOW, TOPMOST styles");
            Check((GetWindowLong(hwnd, -20) & 0x40000) == 0, "APPWINDOW absent");
            Check(SendMessage(hwnd, 0x21, 0, 0) == 3, "WM_MOUSEACTIVATE preserves clicks without activation");
            Check(GetActiveWindow() == active && Keyboard.FocusedElement == focused, "Opening preserves active window and keyboard focus");
            var vm = Field<IslandViewModel>(island, "_viewModel");
            var services = Field<IslandServices>(island, "_services");
            Check(Field<object?>(services, "_hardwareCancellation") is null && Field<object?>(services, "_volumeCancellation") is null, "No collapsed hardware/audio work");
            // Existing desktop playback must not make deterministic visual checks depend on the user's song.
            services.Dispose(); vm.Media.SetActive(true); vm.Media.Update(MediaSnapshot.Empty);
            await WaitForAsync(() => !Field<IslandMorph>(island, "_morph").HasAnimatedProperties);
            Check(((IslandSurface)surface).VisualBounds.Size == new Size(106, 36), "Clock pill visible dimensions are unchanged inside the shadow gutter");
            Check(vm.Clock.IsRunning, "Visible clock uses its timer");
            Render(island, "clock", 1);

            var sizeFrames = new List<Size>();
            var morph = Field<IslandMorph>(island, "_morph");
            void CaptureSize(object? sender, EventArgs e) => sizeFrames.Add(((IslandSurface)surface).VisualBounds.Size);
            morph.Changed += CaptureSize;
            Invoke(island, "SetExpanded", true);
            await Task.Delay(75);
            Check(sizeFrames.Any(size => size.Width > 106 && size.Width < 400 && size.Height > 36 && size.Height < new AppSettings().HomeHeight), "Visible width and height transition simultaneously inside a stable native host");
            morph.Changed -= CaptureSize;
            Invoke(island, "SetExpanded", false);
            await Task.Delay(40);
            Invoke(island, "SetExpanded", true);
            await WaitForAsync(() => !Field<IslandMorph>(island, "_morph").HasAnimatedProperties);
            Console.WriteLine($"Transition result: Width={island.Width:0.000}, Height={island.Height:0.000}, Actual={island.ActualWidth:0.000}x{island.ActualHeight:0.000}, animated={island.HasAnimatedProperties}");
            Check(Math.Abs(island.Width - 416) < .1 && Math.Abs(island.Height - 16 - new AppSettings().HomeHeight) < .1, "Interrupted transition reaches correct dimensions");
            Check(!Field<IslandMorph>(island, "_morph").HasAnimatedProperties && !((Grid)island.FindName("ExpandedContent")).HasAnimatedProperties, "Completed transition clocks removed");
            Check(GetActiveWindow() == active && Keyboard.FocusedElement == focused, "Transitions preserve keyboard focus");
            Check(Field<object?>(services, "_hardwareCancellation") is null, "Home performs no hardware sampling");
            Check(Field<object?>(services, "_volumeCancellation") is not null, "Home observes volume");
            GetWindowRect(hwnd, out var rectangle);
            var scale = HwndSource.FromHwnd(hwnd)!.CompositionTarget.TransformToDevice;
            var expected = scale.Transform(new Point(Field<double>(island, "_horizontalCenter"), Field<double>(island, "_topPosition")));
            Check(Math.Abs((rectangle.Left + rectangle.Right) / 2.0 - expected.X) <= 1 && Math.Abs(rectangle.Top + 8 * scale.M22 - expected.Y) <= 1,
                "Native morph preserves centered position and physical top margin");
            Console.WriteLine($"Native DPI scale: {scale.M11:0.00} x {scale.M22:0.00}");
            Invoke(island, "SelectDashboard", Dashboard.System);
            await Task.Delay(5000);
            Console.WriteLine($"Native samples: CPU {vm.Usage.CpuText}; GPU {vm.Usage.GpuText} {vm.Usage.GpuName}; RAM {vm.Usage.MemoryDetail}; disk {vm.Usage.DiskText} {vm.Usage.DiskDetail}; network {vm.Usage.DownloadText}/{vm.Usage.UploadText}; media {vm.Media.Status}");
            Check(Field<object?>(services, "_hardwareCancellation") is not null && Field<object?>(services, "_volumeCancellation") is not null, "System samples hardware and shares wheel-volume subscription");
            Check(((MainDashboard)island.FindName("MainDashboard")).DataContext is null, "Hidden Home bindings detached");
            var header = (IslandHeader)island.FindName("Header");
            var mainTab = (Button)header.FindName("MainTab");
            var homeTab = (Button)header.FindName("HomeTab");
            var systemTab = (Button)header.FindName("SystemTab");
            Hover(mainTab, true); Hover(mainTab, false);
            Check(vm.IsSystem, "Title hover leaves navigation unchanged");
            Hover(homeTab, true);
            Check(!vm.IsSystem && Field<bool>(island, "_isExpanded"), "Home hover switches immediately without collapsing");
            Hover(homeTab, false); Hover(systemTab, true);
            Check(vm.IsSystem && Field<bool>(island, "_isExpanded"), "System hover switches immediately without collapsing");
            Check(Field<bool>(vm.Clock, "_timeVisible") && !Field<bool>(vm.Clock, "_dateVisible"), "System header uses existing minute clock scheduling");
            var headerClock = (TextBlock)header.FindName("SystemClock");
            Check(headerClock.Text == vm.Clock.Time && headerClock.Text.Contains(':') && headerClock.IsVisible && !((StackPanel)header.FindName("HomeDate")).IsVisible, "System immediately shows complete time instead of date");
            foreach (var format in new[] { "23:59", "11:59 PM", "11:59:59 PM" })
            {
                headerClock.SetCurrentValue(TextBlock.TextProperty, format); island.UpdateLayout();
                var right = headerClock.TranslatePoint(new Point(headerClock.ActualWidth, 0), header).X;
                var titleCenter = mainTab.TranslatePoint(new Point(mainTab.ActualWidth / 2, 0), header).X;
                Check(headerClock.ActualWidth >= headerClock.DesiredSize.Width - .5 && right <= header.ActualWidth - 1 && Math.Abs(titleCenter - header.ActualWidth / 2) < .5, $"Header fits {format} with edge padding and centered title");
            }
            headerClock.GetBindingExpression(TextBlock.TextProperty)!.UpdateTarget(); island.UpdateLayout();
            var systemTitleCenter = mainTab.TranslatePoint(new Point(mainTab.ActualWidth / 2, 0), header).X;
            var systemClockWidth = headerClock.ActualWidth;
            Invoke(island, "SelectDashboard", Dashboard.Main); island.UpdateLayout();
            Check(Math.Abs(mainTab.TranslatePoint(new Point(mainTab.ActualWidth / 2, 0), header).X - systemTitleCenter) < .5 && headerClock.ActualWidth == systemClockWidth, "Home/System switching retains header geometry");
            Invoke(island, "SelectDashboard", Dashboard.System);
            var hardwareSession = Field<object?>(services, "_hardwareCancellation");
            Hover(systemTab, true); Hover(systemTab, false);
            Check(ReferenceEquals(hardwareSession, Field<object?>(services, "_hardwareCancellation")), "Repeated tab hover retains the active sampling session");

            // Stop services before assigning stable presentation data for renders.
            Invoke(island, "SetExpanded", false);
            Check(!vm.IsSystem && Field<object?>(services, "_hardwareCancellation") is null, "Collapse resets Home and stops hardware");
            Invoke(island, "SetExpanded", true);
            Check(!vm.IsSystem && ((StackPanel)header.FindName("HomeDate")).IsVisible && !((TextBlock)header.FindName("SystemClock")).IsVisible, "Reopening immediately shows Home and date");
            Invoke(island, "SelectDashboard", Dashboard.System);
            services.Dispose();
            vm.Media.SetActive(true);
            vm.Usage.Update(new(24, false, new(8_589_934_592, 17_179_869_184),
                new(18, "AMD Radeon(TM) Graphics"), new(2_400_000, 84_000, NetworkReadingState.Available, "Wi-Fi"), "AMD Ryzen 7 Processor",
                new(214_748_364_800, 536_870_912_000, "Z:\\")));
            await Task.Delay(100);
            Render(island, "system", 1); Render(island, "system-150", 1.5); Render(island, "system-200", 2);
            Invoke(island, "SetDashboardScale", 0.8); Invoke(island, "UpdatePresentation", false);
            await Task.Delay(80); island.UpdateLayout();
            var systemView = (SystemDashboard)island.FindName("SystemDashboard");
            var bottom = systemView.TranslatePoint(new Point(systemView.ActualWidth, systemView.ActualHeight), surface);
            var visualBounds = ((IslandSurface)surface).VisualBounds;
            Check(bottom.Y < visualBounds.Bottom && bottom.X < visualBounds.Right &&
                Math.Abs(((FrameworkElement)island.FindName("ExpandedContent")).Margin.Top - (8 + 14 * .8)) < .01,
                "Scaled System content keeps its original padding inside the rounded boundary and shadow gutter");
            Render(island, "system-constrained", 1);
            Invoke(island, "SetDashboardScale", 1.0); Invoke(island, "UpdatePresentation", false);
            vm.Media.Update(new("Midnight City — a long title with an intentional ellipsis", "M83", "Playing", true, true, true, true));
            Invoke(island, "SelectDashboard", Dashboard.Main);
            services.Dispose(); vm.Media.SetActive(true);
            vm.Media.Update(new("Midnight City — a long title with an intentional ellipsis", "M83", "Playing", true, true, true, true));
            vm.Usage.Update(new(24, false, new(8_589_934_592, 17_179_869_184)));
            vm.Volume.SetActive(true); vm.Volume.Update(new(64, false, true));
            await Task.Delay(450);
            Check(SurfaceAlpha(surface.Background, 204) && island.Opacity == 1, "Only surface background uses 80 percent alpha");
            Render(island, "home", 1); Render(island, "home-150", 1.5); Render(island, "home-200", 2);
            Render(island, "home-bright", 1, Colors.White); Render(island, "home-dark", 1, Color.FromRgb(18, 20, 24));
            Check(((FrameworkElement)island.FindName("ExpandedContent")).ActualWidth == 370, "Expanded layout width is stable");

            var volumeWrites = 0;
            var volume = new VolumeViewModel(_ => volumeWrites++, _ => { });
            var card = new VolumeCard { DataContext = volume };
            volume.SetActive(true); volume.Update(new(42, false, true));
            Check(volumeWrites == 0, "Incoming volume events never write back");
            volume.Percent = 43; Check(volumeWrites == 1, "Slider changes route to native write delegate");
            volume.SetDragging(true); volume.Update(new(20, false, true));
            Check(volume.Percent == 43, "External callbacks do not fight a slider drag");
            volume.SetDragging(false);
            volume.AdjustFromWheel(120); Check(volume.Percent == 46, "One wheel notch changes volume by three points");
            volume.AdjustFromWheel(30); Check(volume.Percent == 46.75, "High-resolution wheel delta preserves fractional points");
            volume.Percent = 99; volume.AdjustFromWheel(120); Check(volume.Percent == 100, "Wheel volume clamps at 100");
            volume.Percent = 1; volume.AdjustFromWheel(-120); Check(volume.Percent == 0, "Wheel volume clamps at zero");
            volume.Update(new(25, false, true)); volume.AdjustFromWheel(120);
            Check(volume.Percent == 28, "Wheel uses the latest external volume state");
            volume.SetActive(false); Check(!volume.AdjustFromWheel(120), "Unavailable volume ignores wheel changes");

            // Native HWND routing: services are stopped, so these VM writes cannot change actual audio.
            GetWindowRect(hwnd, out var wheelRect);
            nint WheelPosition(int x, int y) => (nint)((uint)(ushort)x | ((uint)(ushort)y << 16));
            var wheelPoint = WheelPosition((wheelRect.Left + wheelRect.Right) / 2, wheelRect.Top + 80);
            var beforeWheelFocus = GetActiveWindow();
            SendMessage(hwnd, 0x020A, (nint)(120 << 16), wheelPoint);
            Check(vm.Volume.Percent == 67 && GetActiveWindow() == beforeWheelFocus, "Expanded native wheel changes volume without activation");
            SendMessage(hwnd, 0x020A, (nint)(30 << 16), wheelPoint);
            Check(vm.Volume.Percent == 67.75, "Native precision wheel does not double-handle input");
            SendMessage(hwnd, 0x020A, (nint)(120 << 16), WheelPosition(wheelRect.Right + 10, wheelRect.Top + 80));
            Check(vm.Volume.Percent == 67.75, "Wheel outside island is ignored");

            var player = ((MainDashboard)island.FindName("MainDashboard")).MediaSection;
            var playerScreen = player.PointToScreen(new Point(player.ActualWidth / 2, player.ActualHeight / 2));
            var musicWheelPoint = WheelPosition((int)playerScreen.X, (int)playerScreen.Y);
            SendMessage(hwnd, 0x020A, (nint)(120 << 16), musicWheelPoint);
            Check(vm.Volume.Percent == 70.75, "Wheel up over music raises volume by the existing three-point step exactly once");
            SendMessage(hwnd, 0x020A, (nint)(-120 << 16), musicWheelPoint);
            Check(vm.Volume.Percent == 67.75 && GetActiveWindow() == beforeWheelFocus, "Wheel down over music lowers volume without activation");
            vm.Media.Update(new("Track", "Artist", "Unavailable"));
            SendMessage(hwnd, 0x020A, (nint)(120 << 16), musicWheelPoint);
            Check(vm.Volume.Percent == 70.75, "Unavailable media still allows wheel volume");

            var mediaActions = new List<MediaAction>();
            using var clickMedia = new MediaViewModel(action => { mediaActions.Add(action); return Task.FromResult(true); });
            clickMedia.SetActive(true); clickMedia.Update(new("Track", "Artist", "Playing", true, true, true, true));
            player.SetCurrentValue(FrameworkElement.DataContextProperty, clickMedia);
            var playerBorder = (Border)player.FindName("InformationCard");
            var playerGrid = (Grid)playerBorder.Child;
            var metadata = (StackPanel)playerGrid.Children[1];
            foreach (var source in new UIElement[] { playerBorder, playerGrid.Children[0], metadata.Children[0], metadata.Children[1] })
            {
                var count = mediaActions.Count;
                var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent };
                source.RaiseEvent(click);
                Check(mediaActions.Count == count + 1 && mediaActions[^1] == MediaAction.Toggle && click.Handled && Field<bool>(island, "_isExpanded"), "Media surface click toggles once and never collapses island");
            }
            var transport = (StackPanel)((Border)player.FindName("TransportCard")).Child;
            foreach (var (button, action) in transport.Children.Cast<Button>().Zip(new[] { MediaAction.Previous, MediaAction.Toggle, MediaAction.Next }))
            {
                var count = mediaActions.Count;
                var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent, Source = button.Content };
                Invoke(player, "Card_MouseLeftButtonUp", player, click);
                Check(mediaActions.Count == count && !click.Handled, "Nested media button icon is excluded from parent toggle");
                typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
                Check(mediaActions.Count == count + 1 && mediaActions[^1] == action, "Transport button executes only its own command once");
            }
            clickMedia.Update(new("No media", "", "Unavailable"));
            var unavailableCount = mediaActions.Count;
            var unavailableClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent };
            playerBorder.RaiseEvent(unavailableClick);
            Check(mediaActions.Count == unavailableCount && unavailableClick.Handled && GetActiveWindow() == beforeWheelFocus, "Unavailable card click is consumed without commands or focus changes");
            player.GetBindingExpression(FrameworkElement.DataContextProperty)!.UpdateTarget();

            Invoke(island, "SetExpanded", false); services.Dispose();
            vm.Media.SetActive(true); vm.Media.Update(new("Midnight City — a long title with an intentional ellipsis", "M83", "Playing", true));
            await Task.Delay(400);
            Check(!Field<bool>(island, "_isExpanded") && Math.Abs(((IslandSurface)surface).VisualBounds.Width - 260) < .1, "Music widens pill without expanding dashboard");
            Check(!vm.Clock.IsRunning && ((Equalizer)island.FindName("MusicEqualizer")).IsRunning, "Only visible music bars animate; hidden clock stops");
            Check(Field<bool>(island, "_suppressMusicMorphHover"), "Collapsed music changes arm the geometry-hover guard");
            // Capture now, rather than assuming the user kept their cursor still during the preceding delay.
            var pointerField = typeof(MainWindow).GetField("_musicMorphPointer", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var pointer = pointerField.GetValue(island)!;
            var xField = pointer.GetType().GetField("X")!;
            GetCursorPos(out var currentPointer);
            xField.SetValue(pointer, currentPointer.X);
            pointer.GetType().GetField("Y")!.SetValue(pointer, currentPointer.Y);
            pointerField.SetValue(island, pointer);
            Invoke(island, "Surface_MouseEnter", surface, new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount));
            Check(!Field<bool>(island, "_isExpanded"), "Music morph does not expand under a stationary cursor");
            var musicInteraction = Field<IslandInteractionController>(island, "_interaction");
            // Exercise both gesture paths explicitly; saved/default settings
            // now select Hover or Click independently rather than Both.
            musicInteraction.Configure(new IslandInteractionOptions());
            musicInteraction.Click(); musicInteraction.SetInteractionInProgress(true); musicInteraction.SetInteractionInProgress(false);
            await Task.Delay(350);
            Check(Field<bool>(island, "_isExpanded"), "Clicking geometry-entered music pill remains expanded after capture release");
            musicInteraction.Click(); musicInteraction.PointerLeft();
            // Simulate the stored cursor position differing; do not move the user's physical pointer.
            xField.SetValue(pointer, int.MinValue);
            pointerField.SetValue(island, pointer);
            Invoke(island, "Surface_MouseEnter", surface, new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount));
            Check(Field<bool>(island, "_isExpanded"), "Genuine pointer movement permits hover after music morph");
            Invoke(island, "SetExpanded", false); services.Dispose(); vm.Media.SetActive(true);
            vm.Media.Update(new("Midnight City — a long title with an intentional ellipsis", "M83", "Playing", true));
            await Task.Delay(400);
            Render(island, "music", 1);
            vm.Media.Update(new("Next track", "Artist", "Playing", true));
            await Task.Delay(30);
            Check(!Field<IslandMorph>(island, "_morph").HasAnimatedProperties, "Track changes do not restart window transitions");
            vm.Media.Update(new("Next track", "Artist", "Paused"));
            await Task.Delay(350);
            Check(Math.Abs(((IslandSurface)surface).VisualBounds.Width - 106) < .1 && !((Equalizer)island.FindName("MusicEqualizer")).IsRunning, "Pause returns clock pill and stops equalizer");
            Check(!Field<ScaleTransform>((Equalizer)island.FindName("MusicEqualizer"), "Bar1").HasAnimatedProperties, "Equalizer clocks removed on pause");
            island.Hide(); await Task.Delay(150);
            Check(!vm.Clock.IsRunning && Field<object?>(services, "_hardwareCancellation") is null &&
                Field<object?>(services, "_mediaCancellation") is null && Field<object?>(services, "_volumeCancellation") is null, "Hiding stops every service and clock");
            island.Show(); await Task.Delay(350);
            Console.WriteLine($"Restore styles=0x{GetWindowLong(hwnd, -20):X}; active=0x{GetActiveWindow():X}; island=0x{hwnd:X}; initial active=0x{active:X}");
            Check((GetWindowLong(hwnd, -20) & 0x08000088) == 0x08000088 && GetActiveWindow() != hwnd,
                "Restoring preserves native styles and focus");
            island.WindowState = WindowState.Minimized; await Task.Delay(150);
            Check(island.WindowState == WindowState.Minimized && !vm.Clock.IsRunning && Field<object?>(services, "_mediaCancellation") is null,
                "Minimizing stops services without restoring the window");
            island.WindowState = WindowState.Normal; await Task.Delay(350);
            Check(GetActiveWindow() != hwnd && (GetWindowLong(hwnd, -20) & 0x08000088) == 0x08000088,
                "Restore from minimized preserves focus and native styles");
            var interaction = Field<IslandInteractionController>(island, "_interaction");
            interaction.PointerEntered(); await Task.Delay(330);
            Check(Field<bool>(island, "_isExpanded"), "Hover expands");
            interaction.PointerLeft(); await Task.Delay(100); interaction.PointerEntered(); await Task.Delay(200);
            Check(Field<bool>(island, "_isExpanded"), "Re-entry cancels delayed collapse");
            interaction.PointerLeft(); interaction.SetInteractionInProgress(true); await Task.Delay(300);
            Check(Field<bool>(island, "_isExpanded"), "Capture holds expansion outside widget");
            interaction.SetInteractionInProgress(false); await Task.Delay(550);
            Check(!Field<bool>(island, "_isExpanded"), "Release permits delayed collapse");
            interaction.PointerEntered(); await Task.Delay(330); interaction.Click(); await Task.Delay(300);
            interaction.PointerEntered(); await Task.Delay(180);
            Check(!Field<bool>(island, "_isExpanded"), "Click-close suppresses same-visit hover");
            interaction.PointerLeft(); interaction.PointerEntered(); await Task.Delay(330);
            Check(Field<bool>(island, "_isExpanded"), "Next visit expands again");
            Invoke(island, "RenameTitle"); await Task.Delay(200);
            var rename = Field<RenameWindow>(island, "_renameWindow");
            var input = (TextBox)rename.FindName("TitleInput");
            Check(Keyboard.FocusedElement == input && GetActiveWindow() != hwnd, "Only explicit rename tool accepts keyboard focus");
            input.Text = new string('a', 24);
            Check(input.Text.Length == 20, "Rename editor limits input");
            Render(rename, "rename", 1);
            rename.Close(); await Task.Delay(150);
            Check((GetWindowLong(hwnd, -20) & 0x08000088) == 0x08000088 && GetActiveWindow() != hwnd,
                "Rename close never activates island or changes styles");
        }
        finally { island.Close(); }
        await VerifyPreferencesAsync();
    }

    private static async Task VerifyPreferencesAsync()
    {
        var path = Path.Combine(Artifacts, "preferences-test.json");
        var store = new PreferencesStore(path);
        Check(PreferencesStore.NormalizeTitle(new string('x', 25)).Length == 20, "Title limited to 20 characters");
        var emoji = string.Concat(Enumerable.Repeat("🐾", 22));
        Check(new StringInfo(PreferencesStore.NormalizeTitle(emoji)).LengthInTextElements == 20, "Unicode title limit preserves characters");
        Check(await store.SaveTitleAsync("My Island"), "Title saves atomically");
        Check(await new PreferencesStore(path).LoadTitleAsync() == "My Island", "Title survives a new preferences instance");
        await File.WriteAllTextAsync(path, "invalid json");
        Check(await store.LoadTitleAsync() == "IslandDesk", "Corrupt preferences recover to default");
        var usage = new SystemUsageViewModel();
        Check(usage.CpuText == "—" && usage.GpuText == "—" && usage.MemoryText == "—", "Initializing sensors consistently show em dash");
        usage.Update(new(null, false, null, new(null, "GPU", false), new(null, null, NetworkReadingState.Unavailable)));
        Check(usage.CpuText == "N/A" && usage.GpuText == "N/A" && usage.MemoryText == "N/A" && usage.DownloadText == "N/A", "Unavailable sensors show N/A");
        usage.Reset(); Check(usage.DiskText == "—", "Disk initializes with em dash");
        usage.Update(new(null, false, null)); Check(usage.DiskText == "N/A", "Unavailable disk reports N/A");
        usage.Update(new(null, false, null, Disk: new(200UL * 1_073_741_824, 500UL * 1_073_741_824, "Z:\\")));
        Check(usage.DiskText == "40%" && usage.DiskDetail.Contains("200") && usage.DiskLabel.Contains("Z:"), "Disk capacity and actual drive label format correctly");
        var informationTask = DeviceInformationReader.GetAsync();
        Check(ReferenceEquals(informationTask, DeviceInformationReader.GetAsync()), "Device information uses one process-wide cached read");
        var information = await informationTask;
        Console.WriteLine($"Device: {information.ComputerName}; motherboard {information.Motherboard}");
        var board = new byte[14]; board[0] = 2; board[1] = 14; board[4] = 1; board[5] = 2; board[13] = 10;
        var table = board.Concat(System.Text.Encoding.UTF8.GetBytes("Example Inc.\0Board Model\0\0")).ToArray();
        Check(DeviceInformationReader.ParseBaseboard(table) == "Example Inc. · Board Model", "SMBIOS Type 2 manufacturer/model parse correctly");
        Check(DeviceInformationReader.ParseBaseboard(table.AsSpan(0, table.Length - 1)) is null, "Truncated SMBIOS string data is rejected");
        var disk = await Task.Run(() => new SystemDiskSampler().Read());
        Check(disk is { TotalBytes: > 0 } && disk.UsedBytes <= disk.TotalBytes && Directory.Exists(disk.Root), "Native system-volume storage query succeeds");
        Check(DashboardLayout.ScaleToFit(1920, 1000) == 1, "Normal working areas keep dashboard design size");
        var smallScale = DashboardLayout.ScaleToFit(360, 320);
        Check(400 * smallScale <= 360 && 392 * smallScale <= 320, "Constrained DPI working areas fit the complete dashboard");
    }

    private static async Task MeasureAsync(string label, int seconds, MainWindow? window = null)
    {
        var messages = new Dictionary<int, int>();
        nint Hook(nint h, int message, nint w, nint l, ref bool handled)
        { messages[message] = messages.GetValueOrDefault(message) + 1; return 0; }
        var source = window is null ? null : HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook(Hook);
        using var process = Process.GetCurrentProcess();
        process.Refresh(); var cpu = process.TotalProcessorTime; var elapsed = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        process.Refresh();
        Console.WriteLine($"PERF {label}: {(process.TotalProcessorTime - cpu).TotalMilliseconds / elapsed.Elapsed.TotalMilliseconds * 100:0.00}% of one CPU core; working set {process.WorkingSet64 / 1048576.0:0.0} MiB; private {process.PrivateMemorySize64 / 1048576.0:0.0} MiB ({elapsed.Elapsed.TotalSeconds:0.0}s sample, validation process included)");
        source?.RemoveHook(Hook);
        if (window is not null) Console.WriteLine($"  {window.Width:0}x{window.Height:0}; animated {window.HasAnimatedProperties}; window messages: {string.Join(", ", messages.OrderByDescending(x => x.Value).Take(6).Select(x => $"0x{x.Key:X}={x.Value}"))}");
    }

    private static void Render(Window window, string name, double scale, Color? background = null)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * scale), (int)Math.Ceiling(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        if (background is { } color)
        {
            var backdrop = new DrawingVisual();
            using (var drawing = backdrop.RenderOpen()) drawing.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
            bitmap.Render(backdrop);
        }
        bitmap.Render((Visual)window.Content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Artifacts, name + ".png")); png.Save(stream);
    }
    private static void Check(bool condition, string description)
    { if (!condition) throw new InvalidOperationException(description); _checks++; Console.WriteLine("PASS " + description); }
    private static bool SurfaceAlpha(Brush brush, byte alpha) => brush is SolidColorBrush solid ? solid.Color.A == alpha : brush is LinearGradientBrush gradient && gradient.GradientStops.All(stop => stop.Color.A == alpha);
    private static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(source)!;
    private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static void Hover(UIElement element, bool enter) => element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
        { RoutedEvent = enter ? Mouse.MouseEnterEvent : Mouse.MouseLeaveEvent });
    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("WPF transition did not finish.");
            await Task.Delay(30);
        }
    }
    [DllImport("user32.dll")] private static extern nint GetActiveWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rectangle);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint position);
}

