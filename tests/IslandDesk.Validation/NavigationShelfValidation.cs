using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IslandDesk;
using IslandDesk.Audio;
using IslandDesk.Controls;
using IslandDesk.Media;
using IslandDesk.Presentation;
using IslandDesk.Settings;
using IslandDesk.Views;
using IslandDesk.Windowing;
using ThemeMode = IslandDesk.Settings.ThemeMode;

internal static class NavigationShelfValidation
{
    private sealed class Startup : IStartupRegistration { public bool IsEnabled() => false; public void SetEnabled(bool enabled) { } }
    private sealed class Utilities : IWindowsUtilityActions
    {
        public int Screen, Clipboard, TaskManager, Search, Lock, Emoji;
        public UtilityResult OpenScreenshot(nint widget) { Screen++; return new(false, "Controlled launch failure"); }
        public UtilityResult OpenClipboardHistory(nint widget) { Clipboard++; return new(true, "Controlled clipboard request"); }
        public UtilityResult OpenTaskManager() { TaskManager++; return new(true, "Controlled task manager request"); }
        public UtilityResult OpenWindowsSearch(nint widget) { Search++; return new(true, "Controlled search request"); }
        public UtilityResult LockDevice() { Lock++; return new(true, "Controlled lock request"); }
        public UtilityResult OpenEmojiPanel(nint widget) { Emoji++; return new(false, "Controlled input failure"); }
    }
    private sealed class FileActions : IShelfFileActions
    {
        public string? Opened, Revealed, Copied;
        public UtilityResult Open(string path) { Opened = path; return new(true, "Opened"); }
        public UtilityResult Reveal(string path) { Revealed = path; return new(true, "Revealed"); }
        public UtilityResult CopyPath(string path) { Copied = path; return new(true, "Copied"); }
    }
    public static async Task RunAsync(string directory, Action<bool, string> check)
    {
        check(new AppSettings().QuickControlsTab && new AppSettings().FileShelfTab &&
            JsonSerializer.Deserialize<AppSettings>("{}") is { QuickControlsTab: true, FileShelfTab: true }, "New optional dashboards default enabled in both new and legacy JSON");
        using var settings = new SettingsController(Application.Current.Dispatcher, new PreferencesStore(Path.Combine(directory, "step18-settings.json")), new Startup());
        settings.Update(new AppSettings { AnimationStyle = TransitionStyle.Instant, ThemeMode = ThemeMode.Dark });
        var island = new MainWindow(false, settings);
        try
        {
            island.Show(); ((UIElement)island.FindName("IslandSurface")).IsHitTestVisible = false;
            Invoke(island, "SetExpanded", true); await Task.Delay(200);
            var header = (IslandHeader)island.FindName("Header");
            var vm = Field<IslandViewModel>(island, "_viewModel");
            var services = Field<IslandServices>(island, "_services");
            var region = (Border)header.FindName("NavigationRegion"); region.IsHitTestVisible = false;
            var menu = region.Child;
            var homeTab = (Button)header.FindName("HomeTab"); var systemTab = (Button)header.FindName("SystemTab");
            var home = (Button)header.FindName("HomeItem"); var quick = (Button)header.FindName("QuickControlsItem"); var shelfItem = (Button)header.FindName("FileShelfItem");
            var quickView = (QuickControlsDashboard)island.FindName("QuickControlsDashboard");
            var shelf = (FileShelfDashboard)island.FindName("FileShelfDashboard");
            var size = new Size(island.ActualWidth, island.ActualHeight);
            var focus = GetForegroundWindow();
            var titleBefore = (Button)header.FindName("MainTab");
            var originalTitleWidth = titleBefore.ActualWidth;
            var originalTitlePosition = titleBefore.TranslatePoint(new Point(), header);
            Hover(homeTab, true); await Task.Delay(50);
            check(header.IsNavigationOpen && vm.IsHome && size == new Size(island.ActualWidth, island.ActualHeight), "Home hover opens the retained overlay without expanding the island");
            check(GetForegroundWindow() == focus && HwndSource.FromVisual(region) is HwndSource popupSource &&
                (GetWindowLong(popupSource.Handle, -20) & 0x08000080) == 0x08000080,
                "Strip shares the island NOACTIVATE/TOOLWINDOW surface and preserves foreground focus");
            settings.AlwaysOnTop = false;
            check((GetWindowLong(((HwndSource)PresentationSource.FromVisual(region)).Handle, -20) & 8) == 0,
                "Flyout follows the disabled Always-on-Top setting without remaining a topmost window");
            settings.AlwaysOnTop = true;
            services.Dispose();
            var title = (Button)header.FindName("MainTab");
            var titleCenter = title.TranslatePoint(new Point(title.ActualWidth / 2, 0), header).X;
            check(((Button)header.FindName("SettingsButton")).Visibility == Visibility.Hidden,
                "Settings is covered while the strip is open");
            check(originalTitleWidth > 150 && title.ActualWidth == originalTitleWidth &&
                title.TranslatePoint(new Point(), header) == originalTitlePosition &&
                ((FrameworkElement)header.FindName("NavigationViewport")).Parent is Canvas,
                "Navigation overlay preserves original title width/position and contributes no reserved strip width");
            var menuTop = region.PointToScreen(new Point()).Y;
            check(menuTop >= header.PointToScreen(new Point()).Y && menuTop < header.PointToScreen(new Point(0, header.ActualHeight)).Y &&
                homeTab.TranslatePoint(new Point(), header).X < systemTab.TranslatePoint(new Point(), header).X &&
                ((StackPanel)region.Child).Orientation == Orientation.Horizontal &&
                ((StackPanel)region.Child).Children.OfType<Button>().Select(button => button.Tag).SequenceEqual(new[] { "Main", "System", "QuickControls", "FileShelf" }),
                "Compact horizontal strip stays inside the header with Home, System, Quick Controls, File Shelf in order");
            Invoke(header, "Navigation_MouseLeave", homeTab, MouseArgs()); await Task.Delay(90);
            check(header.IsNavigationOpen, "Strip survives the 150ms pointer transit grace");
            Invoke(header, "Navigation_MouseEnter", region, MouseArgs()); await Task.Delay(210);
            check(header.IsNavigationOpen && !Field<DispatcherTimer>(header, "_dismiss").IsEnabled, "Entering the menu cancels dismissal; no timer runs while hovered");
            Hover(quick, true); await Task.Delay(100);
            check(vm.IsQuickControls && quickView.IsVisible && ((MainDashboard)island.FindName("MainDashboard")).DataContext is null &&
                Field<object?>(services, "_hardwareCancellation") is null, "Quick Controls navigation is immediate and detaches hidden Home/System work");
            Hover(shelfItem, true); await Task.Delay(100);
            check(vm.IsFileShelf && shelf.IsVisible && ReferenceEquals(menu, region.Child), "File Shelf is retained and navigation never recreates its flyout");
            Hover(systemTab, true);
            check(vm.IsSystem && header.IsNavigationOpen, "System hover switches immediately while the strip stays open");
            Hover(homeTab, true); check(vm.IsSystem && header.IsNavigationOpen, "Opening Home navigation preserves the current System selection");
            Hover(home, true); check(vm.IsHome, "Home flyout item supports hover navigation");
            quick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); check(vm.IsQuickControls, "Flyout items also support clicks");
            Invoke(header, "Navigation_MouseLeave", region, MouseArgs()); await Task.Delay(240);
            check(!header.IsNavigationOpen && !Field<DispatcherTimer>(header, "_dismiss").IsEnabled &&
                ((Button)header.FindName("SettingsButton")).IsVisible &&
                Math.Abs(titleCenter - title.TranslatePoint(new Point(title.ActualWidth / 2, 0), header).X) < .1,
                "Exit closes the strip, restores Settings, stops the timer and preserves title position");
            check(ReferenceEquals(((System.Windows.Shapes.Path)header.FindName("PrimaryIcon")).Data, header.FindResource("QuickControlsIcon")),
                "Closed primary icon reflects the last selected dashboard");
            settings.QuickControlsTab = false;
            check(vm.IsHome && quick.Visibility == Visibility.Collapsed && home.Visibility == Visibility.Visible && systemTab.Visibility == Visibility.Visible,
                "Disabling the active optional dashboard returns Home and preserves permanent navigation");
            Invoke(island, "SelectDashboard", Dashboard.QuickControls); check(vm.IsHome, "Disabled optional dashboards cannot be selected indirectly");
            settings.QuickControlsTab = true; Invoke(island, "SelectDashboard", Dashboard.FileShelf); settings.FileShelfTab = false;
            check(vm.IsHome && shelfItem.Visibility == Visibility.Collapsed, "Disabling File Shelf applies immediately and returns Home");
            await settings.FlushAsync();
            var saved = await new PreferencesStore(Path.Combine(directory, "step18-settings.json")).LoadAsync();
            check(saved.QuickControlsTab && !saved.FileShelfTab, "Dashboard preferences persist in the existing JSON store");
            settings.FileShelfTab = true;
            Invoke(island, "SelectDashboard", Dashboard.QuickControls); services.Dispose();
            var actions = new Utilities(); quickView.ActionsService = actions;
            var lockButton = (Button)quickView.FindName("LockButton");
            Hover(lockButton, true);
            var utilityGrid = (Grid)quickView.FindName("Actions");
            foreach (var button in utilityGrid.Children.OfType<Button>()) Hover(button, true);
            check(actions.Lock == 0 && actions.Screen == 0 && actions.Clipboard == 0 && actions.TaskManager == 0 && actions.Search == 0 && actions.Emoji == 0,
                "Hovering all six utility cards never invokes a Windows action");
            check(utilityGrid.Children.OfType<Button>().Select(button => button.Tag).SequenceEqual(new[] { "Screenshot", "ClipboardHistory", "TaskManager", "Search", "Lock", "Emoji" }) &&
                utilityGrid.Children.OfType<Button>().Select(button => (Grid.GetRow(button), Grid.GetColumn(button))).SequenceEqual(new[] { (0, 0), (0, 2), (0, 4), (2, 0), (2, 2), (2, 4) }),
                "Six compact Windows cards occupy the required three-column/two-row order without Night Light or Explorer");
            quickView.ConfirmLock = () => false; lockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(actions.Lock == 0 && !quickView.IsInteracting, "Canceling lock confirmation performs no lock and releases the interaction hold");
            quickView.ConfirmLock = () => true; lockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(actions.Lock == 1, "A deliberate confirmed click invokes exactly one lock request through the native-action boundary");
            ((Button)quickView.FindName("ScreenshotButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(actions.Screen == 1 && ((TextBlock)quickView.FindName("Status")).Text == "Controlled launch failure", "Screenshot launch failures are presented honestly");
            foreach (var name in new[] { "ClipboardButton", "TaskManagerButton", "SearchButton" })
                ((Button)quickView.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(actions.Clipboard == 1 && actions.TaskManager == 1 && actions.Search == 1 && !quickView.IsInteracting,
                "Clipboard History, Task Manager and native Windows Search clicks route exactly once and release the interaction hold");
            ((Button)quickView.FindName("EmojiButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(actions.Emoji == 1 && ((TextBlock)quickView.FindName("Status")).Text == "Controlled input failure", "Emoji input failures are presented without claiming the panel opened");
            check(Marshal.SizeOf(typeof(WindowsUtilityActions).GetNestedType("Input", BindingFlags.NonPublic)!) == (IntPtr.Size == 8 ? 40 : 28),
                "Native emoji keyboard input has the correct architecture-dependent INPUT structure size");
            quickView.ConfirmLock = null;
            var focusBeforeDialog = GetForegroundWindow();
            _ = Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<ConfirmationWindow>().Single();
                check(dialog.Owner is null, "Lock confirmation uses a separate activating dialog without making the island its activation owner");
                dialog.Close();
            }));
            lockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(actions.Lock == 1, "The real default confirmation dialog cancels safely without locking the workstation");
            await Task.Delay(50);
            check(GetForegroundWindow() == focusBeforeDialog && GetForegroundWindow() != new WindowInteropHelper(island).Handle,
                "Closing lock confirmation restores the previous foreground window without activating the widget");
            await VerifyFiles(directory, shelf, check);
            foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light, ThemeMode.System })
            {
                settings.ThemeMode = mode;
                Invoke(island, "SelectDashboard", Dashboard.QuickControls); services.Dispose(); await Task.Delay(60);
                Save(island, Path.Combine(directory, $"step18-quick-{mode}.png"));
                Invoke(island, "SelectDashboard", Dashboard.FileShelf); services.Dispose(); await Task.Delay(60);
                Save(island, Path.Combine(directory, $"step18-shelf-{mode}.png"));
                header.OpenNavigation(); await Task.Delay(180);
                Save(island, Path.Combine(directory, $"step181-navigation-{mode}.png"));
                Save(region, Path.Combine(directory, $"step18-menu-{mode}.png")); header.CloseNavigation();
                check(quickView.IsLoaded && shelf.IsLoaded && ((Button)quickView.FindName("ScreenshotButton")).ActualWidth > 100,
                    $"{mode}: retained dashboards and compact cards lay out without clipping");
            }
            settings.AnimationStyle = TransitionStyle.Smooth;
            settings.Update(settings.Current with { AnimationFrameRate = 60 });
            header.OpenNavigation(); await Task.Delay(220);
            var navigationTransition = Field<IslandTransitions>(header, "_navigationTransition");
            check(!navigationTransition.IsAnimating && navigationTransition.DesiredFrameRate == 60,
                "Horizontal reveal completes and removes its clocks at the selected frame rate");
            var layouts = 0;
            void Layout(object? sender, EventArgs e) => layouts++;
            header.LayoutUpdated += Layout;
            await Task.Delay(200);
            header.LayoutUpdated -= Layout;
            check(layouts == 0 && !Field<DispatcherTimer>(header, "_dismiss").IsEnabled,
                "Static open navigation performs no layout or dismissal timer work");
            Invoke(header, "Navigation_MouseLeave", region, MouseArgs()); await Task.Delay(180);
            header.OpenNavigation(); await Task.Delay(200);
            check(header.IsNavigationOpen && ((FrameworkElement)header.FindName("NavigationViewport")).IsVisible &&
                !navigationTransition.IsAnimating && !((Button)header.FindName("SettingsButton")).IsVisible,
                "Reentering during hide cancels the old completion without flicker or stale Settings restoration");
            header.OpenNavigation(); Invoke(island, "SetExpanded", false); await Task.Delay(450);
            check(vm.IsHome && !header.IsNavigationOpen && !shelf.IsVisible && !quickView.IsVisible, "Collapse resets Home, closes navigation and hides optional views after the outgoing fade");
            Invoke(island, "SetExpanded", true); await Task.Delay(350); header.OpenNavigation(); island.Hide(); await Task.Delay(100);
            check(!header.IsNavigationOpen && !Field<DispatcherTimer>(header, "_dismiss").IsEnabled && !Field<ClockViewModel>(island, "_clock").IsRunning,
                "Hiding closes the flyout and stops its timer and clock work");
        }
        finally { island.Close(); await settings.FlushAsync(); }
    }
    private static async Task VerifyFiles(string directory, FileShelfDashboard shelf, Action<bool, string> check)
    {
        var folder = Path.Combine(directory, "shelf-owned-fixtures"); Directory.CreateDirectory(folder);
        var paths = Enumerable.Range(0, 60).Select(i => Path.Combine(folder, $"résumé & report {i:00}.txt")).ToArray();
        foreach (var path in paths) await File.WriteAllBytesAsync(path, new byte[1024]);
        var model = shelf.Model;
        while (model.ViewStyle != ShelfViewStyle.Line) model.CycleView();
        await model.AddAsync(paths.Take(9).Concat(new[] { paths[0].ToUpperInvariant(), folder, Path.Combine(folder, "missing.txt"), @"\\server\not-local.txt", "relative.txt" }).ToArray());
        check(model.Count == 9 && model.PageEntries.Count == 4 && model.CanNext && !model.CanPrevious && model.Status.Contains("already"), "Multiple files add once, rejecting duplicate, missing, directory, relative and remote paths");
        check(model.PageEntries[0].Name == "résumé & report 00.txt" && model.PageEntries[0].Details.Contains("TXT") && model.PageEntries[0].Details.Contains("1 KB"),
            "File entries retain Unicode names, extensions and bounded metadata sizes");
        model.Next(); model.Next(); check(model.PageEntries.Count == 1 && model.CanPrevious && !model.CanNext, "File Shelf paginates without any scroll viewer or wheel interception");
        var last = model.PageEntries[0]; model.Remove(last);
        check(model.Count == 8 && model.PageEntries.Count == 4 && File.Exists(last.Path), "Removing the final item on a page clamps pagination and keeps the original file");
        model.Previous();
        var file = model.PageEntries[0]; var actions = new FileActions(); shelf.FileActions = actions;
        var sender = new Button { DataContext = file };
        shelf.OpenFile(file); Invoke(shelf, "Reveal_Click", sender, new RoutedEventArgs(Button.ClickEvent)); Invoke(shelf, "Copy_Click", sender, new RoutedEventArgs(Button.ClickEvent));
        check(actions.Opened == file.Path && actions.Revealed == file.Path && actions.Copied == file.Path, "Explicit file actions route the original path to open, reveal and copy operations");
        File.Move(file.Path, file.Path + ".moved", true);
        check(!model.CheckAvailable(file) && model.Status.Contains("missing"), "Moved/missing files remain removable and report their unavailable state");
        actions.Copied = null; Invoke(shelf, "Copy_Click", sender, new RoutedEventArgs(Button.ClickEvent));
        check(actions.Copied == file.Path, "A missing file's stored reference can still be copied");
        File.Move(file.Path + ".moved", file.Path, true);
        model.Clear(); check(model.Count == 0 && paths.All(File.Exists), "Clear removes references only; every original file survives");
        await model.AddAsync(paths); check(model.Count == FileShelfViewModel.MaximumEntries && !model.CanAdd && model.Status.Contains("limit"), "The shelf enforces its 50-file maximum");
        using (var fresh = new FileShelfViewModel()) check(fresh.Count == 0, "New sessions start empty without loading or persisting file paths");
        model.Clear(); var adding = model.AddAsync(paths); model.Clear(); await adding;
        check(model.Count == 0 && !model.IsBusy, "Clear cancels an in-flight add without resurrecting references");
        check(await shelf.ReceiveFilesAsync(new DataObject(DataFormats.Text, "text"), DragDropEffects.Copy) == DragDropEffects.None &&
            await shelf.ReceiveFilesAsync(new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Move) == DragDropEffects.None,
            "Drop handling rejects unsupported formats and move-only operations");
        check(await shelf.ReceiveFilesAsync(new DataObject(DataFormats.FileDrop, paths.Take(5).ToArray()), DragDropEffects.Copy) == DragDropEffects.Copy && model.Count == 5,
            "A standard Explorer-format file drop adds multiple references with copy-only semantics");
        model.Clear();
        var drop = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { new DataObject(DataFormats.FileDrop, paths.Take(5).ToArray()), DragDropKeyStates.None, DragDropEffects.Copy, shelf, new Point() }, null)!;
        drop.RoutedEvent = DragDrop.DropEvent;
        Invoke(shelf, "Files_Drop", shelf, drop);
        check(drop.Effects == DragDropEffects.Copy && drop.Handled, "OLE receives the copy effect synchronously before asynchronous file metadata collection");
        var deadline = Stopwatch.StartNew();
        while (model.IsBusy && deadline.ElapsedMilliseconds < 3000) await Task.Delay(10);
        check(model.Count == 5 && !model.IsBusy, "The native drop event completes its asynchronous import without losing references");
    }
    public static async Task ProfileAsync(string directory)
    {
        using var settings = new SettingsController(Application.Current.Dispatcher, new PreferencesStore(Path.Combine(directory, "step18-profile-settings.json")), new Startup());
        settings.Update(new AppSettings { ThemeMode = ThemeMode.Dark });
        var island = new MainWindow(false, settings); var rows = new List<object>();
        try
        {
            island.Show(); ((UIElement)island.FindName("IslandSurface")).IsHitTestVisible = false; Invoke(island, "SetExpanded", true);
            var header = (IslandHeader)island.FindName("Header");
            ((UIElement)header.FindName("NavigationRegion")).IsHitTestVisible = false;
            var fixture = Path.Combine(directory, "shelf-profile-fixtures"); Directory.CreateDirectory(fixture);
            var paths = Enumerable.Range(0, FileShelfViewModel.MaximumEntries).Select(i => Path.Combine(fixture, $"file-{i}.txt")).ToArray();
            foreach (var file in paths) await File.WriteAllTextAsync(file, "Owned performance fixture");
            var shelf = (FileShelfDashboard)island.FindName("FileShelfDashboard");
            await shelf.Model.AddAsync(paths);
            foreach (var state in new[] { "quick-idle", "shelf-grid", "shelf-line", "shelf-two-column", "strip-idle", "quick-idle-warm", "hidden" })
            {
                var view = state == "shelf-two-column" ? ShelfViewStyle.TwoColumn : state is "shelf-line" or "strip-idle" ? ShelfViewStyle.Line : ShelfViewStyle.Grid;
                while (shelf.Model.ViewStyle != view) shelf.Model.CycleView();
                Invoke(island, "SelectDashboard", state.StartsWith("quick") ? Dashboard.QuickControls : Dashboard.FileShelf);
                Field<IslandServices>(island, "_services").Dispose();
                if (state == "strip-idle") header.OpenNavigation();
                if (state == "hidden") island.Hide();
                await Task.Delay(2500);
                var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
                var path = Path.Combine(directory, "step18-gpu-" + state + ".json");
                foreach (var arg in new[] { typeof(NavigationShelfValidation).Assembly.Location, "--gpu-probe", Environment.ProcessId.ToString(), "8", path }) info.ArgumentList.Add(arg);
                using var probe = Process.Start(info)!; using var process = Process.GetCurrentProcess();
                var layouts = 0; void Layout(object? s, EventArgs e) => layouts++;
                island.LayoutUpdated += Layout; var cpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
                await probe.WaitForExitAsync(); process.Refresh(); island.LayoutUpdated -= Layout;
                var gpu = JsonSerializer.Deserialize<double[]>(await File.ReadAllTextAsync(path))!;
                var row = new { State = state, Seconds = watch.Elapsed.TotalSeconds,
                    CpuPercent = (process.TotalProcessorTime - cpu).TotalSeconds / watch.Elapsed.TotalSeconds / Environment.ProcessorCount * 100,
                    GpuPercent = gpu.Length == 0 ? (double?)null : gpu.Average(), Samples = gpu.Length, Layouts = layouts,
                    WorkingSetMB = process.WorkingSet64 / 1048576d, PrivateMB = process.PrivateMemorySize64 / 1048576d,
                    VisibleFiles = shelf.Model.PageEntries.Count, View = shelf.Model.ViewStyle.ToString(),
                    DismissTimer = Field<DispatcherTimer>(header, "_dismiss").IsEnabled, StripOpen = header.IsNavigationOpen,
                    ClearTimer = Field<DispatcherTimer>(shelf, "_clearExpiry").IsEnabled,
                    DragTimer = Field<DispatcherTimer>(island, "_fileDragLeave").IsEnabled,
                    NavigationAnimating = Field<IslandTransitions>(header, "_navigationTransition").IsAnimating };
                rows.Add(row); Console.WriteLine(JsonSerializer.Serialize(row)); header.CloseNavigation();
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "step182-profile.json"), JsonSerializer.Serialize(new {
                Environment.ProcessorCount, WpfTier = RenderCapability.Tier >> 16, Dpi = VisualTreeHelper.GetDpi(island).PixelsPerDip,
                Notes = "Release validation host, native services off, 50 file references with six Grid or four Line/Two-Column entries visible, 2.5s settle plus 8s sample per state. CPU normalized to machine; GPU mean busiest process engine sampled externally via PDH, excludes DWM. Working set/private bytes include the validation host. No Rendering subscription.", Samples = rows
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { island.Close(); await settings.FlushAsync(); }
    }
    private static void Save(FrameworkElement element, string file)
    {
        element.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(file); encoder.Save(stream);
    }
    private static MouseEventArgs MouseArgs() => new(Mouse.PrimaryDevice, Environment.TickCount);
    private static void Hover(UIElement element, bool enter) => element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = enter ? Mouse.MouseEnterEvent : Mouse.MouseLeaveEvent });
    private static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
    private static void Invoke(object source, string name, params object[] arguments) => source.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(source, arguments);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
}
