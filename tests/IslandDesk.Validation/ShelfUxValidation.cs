using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IslandDesk;
using IslandDesk.Controls;
using IslandDesk.Interaction;
using IslandDesk.Presentation;
using IslandDesk.Settings;
using IslandDesk.Views;
using IslandDesk.Windowing;
using ThemeMode = IslandDesk.Settings.ThemeMode;

internal static class ShelfUxValidation
{
    private sealed class Startup : IStartupRegistration { public bool IsEnabled() => false; public void SetEnabled(bool value) { } }
    private sealed class Files : IShelfFileActions
    {
        public int Opens, Reveals, Copies;
        public UtilityResult Open(string path) { Opens++; return new(true, "Controlled open"); }
        public UtilityResult Reveal(string path) { Reveals++; return new(true, "Controlled reveal"); }
        public UtilityResult CopyPath(string path) { Copies++; return new(true, "Controlled copy"); }
    }
    private sealed class Input : IShortcutInput
    {
        public nint Foreground { get; set; } = new(123);
        public int? Pressed;
        public uint? Accepted;
        public readonly List<ShortcutStroke[]> Batches = [];
        public bool IsPressed(int key) => key == Pressed;
        public uint Send(ShortcutStroke[] strokes) { Batches.Add(strokes); return Batches.Count == 1 ? Accepted ?? (uint)strokes.Length : (uint)strokes.Length; }
    }
    public static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var input = new Input(); var utilities = new WindowsUtilityActions(input);
        check(utilities.OpenWindowsSearch(999).Success && input.Batches.Single().SequenceEqual(new[] { new ShortcutStroke(0x5B), new ShortcutStroke(0x53), new ShortcutStroke(0x53, true), new ShortcutStroke(0x5B, true) }),
            "Windows Search sends the native Win+S chord, including balanced key releases; no Explorer launch");
        input.Batches.Clear();
        check(utilities.OpenScreenshot(999).Success && input.Batches.Single().Select(stroke => (stroke.Key, stroke.Up)).SequenceEqual(new[] { ((ushort)0x5B, false), ((ushort)0x10, false), ((ushort)0x53, false), ((ushort)0x53, true), ((ushort)0x10, true), ((ushort)0x5B, true) }),
            "Screenshot sends the documented Win+Shift+S capture chord");
        input.Batches.Clear(); utilities.OpenClipboardHistory(999);
        check(input.Batches.Single()[1].Key == 0x56, "Clipboard History sends Win+V without changing clipboard settings");
        input.Batches.Clear(); input.Pressed = 0x11;
        check(!utilities.OpenWindowsSearch(999).Success && input.Batches.Count == 0, "Physically held modifier keys block shortcut injection without releasing user input");
        input.Pressed = null; input.Foreground = 999;
        check(!utilities.OpenWindowsSearch(999).Success && input.Batches.Count == 0, "A focused widget is not used as a text destination");
        input.Foreground = 123; input.Accepted = 3;
        check(!utilities.OpenWindowsSearch(999).Success && input.Batches.Count == 2 && input.Batches[1].SequenceEqual(new[] { new ShortcutStroke(0x5B, true) }),
            "Partial injection releases only synthetic keys still held after the accepted prefix");
        input.Batches.Clear(); input.Accepted = 0;
        check(!utilities.OpenScreenshot(999).Success && input.Batches.Count == 1, "Blocked injection sends no unnecessary key releases and reports failure");
        var fixture = Path.Combine(directory, "step182-owned-files"); Directory.CreateDirectory(fixture);
        var paths = Enumerable.Range(0, 12).Select(i => Path.Combine(fixture, $"Résumé & a deliberately very long name {i:00}.unusual-extension")).ToArray();
        foreach (var path in paths) await File.WriteAllTextAsync(path, "Owned validation fixture");
        var originals = paths.Select(File.GetLastWriteTimeUtc).ToArray();
        using var settings = new SettingsController(Application.Current.Dispatcher, new PreferencesStore(Path.Combine(directory, "step182-settings.json")), new Startup());
        settings.Update(new AppSettings { AnimationStyle = TransitionStyle.Instant, CollapseMode = ExpansionMode.Hover, ThemeMode = ThemeMode.Dark });
        var island = new MainWindow(false, settings);
        try
        {
            island.Show(); var surface = (IslandSurface)island.FindName("IslandSurface"); surface.IsHitTestVisible = false;
            var vm = Field<IslandViewModel>(island, "_viewModel");
            var shelf = (FileShelfDashboard)island.FindName("FileShelfDashboard"); var model = shelf.Model;
            var header = (IslandHeader)island.FindName("Header");
            var foreground = GetForegroundWindow();
            check(island.AllowDrop && (GetWindowLong(new WindowInteropHelper(island).Handle, -20) & 0x080000A0) == 0x08000080,
                "Actual window accepts OLE drops while retaining NOACTIVATE/TOOLWINDOW and avoiding WS_EX_TRANSPARENT");
            foreach (var invalid in new IDataObject[] { new DataObject(DataFormats.Text, "text"), new DataObject(DataFormats.FileDrop, new[] { fixture }), new DataObject(DataFormats.FileDrop, new[] { @"\\server\remote.txt" }) })
            {
                var e = Drag(island, invalid, DragDropEffects.Copy, DragDrop.PreviewDragEnterEvent); island.RaiseEvent(e);
                check(e.Effects == DragDropEffects.None && !Field<bool>(island, "_isExpanded"), "Text, directory-only and remote drags do not expand the pill");
                Invoke(island, "EndFileDrag");
            }
            var moveOnly = Drag(island, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Move, DragDrop.PreviewDragEnterEvent); island.RaiseEvent(moveOnly);
            check(moveOnly.Effects == DragDropEffects.None && !Field<bool>(island, "_isExpanded"), "Move-only drags are rejected without altering originals");
            Invoke(island, "EndFileDrag");
            var data = new DataObject(DataFormats.FileDrop, paths.Take(2).ToArray());
            var enter = Drag(island, data, DragDropEffects.Copy, DragDrop.PreviewDragEnterEvent); island.RaiseEvent(enter);
            check(enter.Effects == DragDropEffects.Copy && Field<bool>(island, "_isExpanded") && vm.IsFileShelf && model.Count == 0,
                "A valid file drag expands the collapsed pill directly into File Shelf before any drop");
            check(Field<bool>(island, "_fileDragActive") && !header.IsHitTestVisible && Field<bool>(Field<IslandInteractionController>(island, "_interaction"), "_interacting"),
                "Incoming drag holds expansion and prevents header hover navigation from taking the shelf away");
            Invoke(island, "SelectDashboard", Dashboard.System); await Task.Delay(350);
            check(vm.IsFileShelf && Field<bool>(island, "_isExpanded"), "Hover-collapse and other dashboard requests cannot interrupt an active file drag");
            island.RaiseEvent(Drag(island, data, DragDropEffects.Copy, DragDrop.PreviewDragLeaveEvent)); await Task.Delay(60);
            island.RaiseEvent(Drag(island, data, DragDropEffects.Copy, DragDrop.PreviewDragOverEvent)); await Task.Delay(180);
            check(Field<bool>(island, "_fileDragActive") && !Field<DispatcherTimer>(island, "_fileDragLeave").IsEnabled,
                "Moving between child targets cancels drag-leave dismissal without flickering or a running timer");
            var changedFormat = Drag(island, new DataObject(DataFormats.Text, "not a file"), DragDropEffects.Copy, DragDrop.PreviewDragOverEvent);
            island.RaiseEvent(changedFormat);
            check(changedFormat.Effects == DragDropEffects.None && !Field<bool>(island, "_fileDragActive"),
                "A changed or replaced non-file payload cannot reuse a previous drag's cached copy effect");
            island.RaiseEvent(Drag(island, data, DragDropEffects.Copy, DragDrop.PreviewDragEnterEvent));
            island.RaiseEvent(Drag(island, data, DragDropEffects.Copy, DragDrop.PreviewDragLeaveEvent)); await Task.Delay(190);
            check(!Field<bool>(island, "_fileDragActive") && header.ReadLocalValue(UIElement.IsHitTestVisibleProperty) is true && !Field<DispatcherTimer>(island, "_fileDragLeave").IsEnabled,
                "DragLeave/cancellation releases the interaction and one-shot timer even with a stationary cursor");
            Invoke(island, "SetExpanded", false);
            island.RaiseEvent(Drag(island, data, DragDropEffects.Copy, DragDrop.PreviewDragEnterEvent));
            var drop = Drag(island, data, DragDropEffects.Copy, DragDrop.PreviewDropEvent); island.RaiseEvent(drop);
            check(drop.Handled && drop.Effects == DragDropEffects.Copy, "Root drop returns the copy effect synchronously before metadata work completes");
            await Until(() => !model.IsBusy, 2000);
            check(model.Count == 2 && !Field<bool>(island, "_fileDragActive") && !shelf.IsInteracting,
                "Multiple files dropped on the actual island event route are staged and all drag holds are released");
            check(GetForegroundWindow() == foreground, "Drag expansion and staging preserve foreground focus");
            settings.FileShelfTab = false; Invoke(island, "SetExpanded", false);
            var disabled = Drag(island, data, DragDropEffects.Copy, DragDrop.PreviewDragEnterEvent); island.RaiseEvent(disabled);
            check(disabled.Effects == DragDropEffects.None && !Field<bool>(island, "_isExpanded"), "Disabling the optional File Shelf also disables drag-to-expand");
            Invoke(island, "EndFileDrag"); settings.FileShelfTab = true;
            Invoke(island, "SetExpanded", true); Invoke(island, "SelectDashboard", Dashboard.FileShelf);
            // Explicit pointer-inside prevents the configured hover-close during other UI checks.
            Field<IslandInteractionController>(island, "_interaction").PointerEntered(false);
            var clear = (Button)shelf.FindName("ClearButton");
            Click(clear); check(shelf.IsClearArmed && model.Count == 2 && ((TextBlock)shelf.FindName("ClearLabel")).Text == "Confirm Clear",
                "First Clear click only arms a red Confirm Clear button without removing references");
            check(Field<DispatcherTimer>(shelf, "_clearExpiry").IsEnabled, "Clear expiry timer runs only while confirmation is armed");
            Click(clear); check(!shelf.IsClearArmed && model.Count == 0 && !Field<DispatcherTimer>(shelf, "_clearExpiry").IsEnabled,
                "Second Clear click removes staged references and resets the button/timer");
            await model.AddAsync(paths);
            Click(clear); Invoke(island, "SelectDashboard", Dashboard.Main);
            check(!shelf.IsClearArmed && !Field<DispatcherTimer>(shelf, "_clearExpiry").IsEnabled && model.Count == paths.Length,
                "Changing dashboards cancels Clear confirmation and retains files");
            Invoke(island, "SelectDashboard", Dashboard.FileShelf); Click(clear);
            ((Button)header.FindName("MainTab")).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            check(!shelf.IsClearArmed && model.Count == paths.Length, "Clicking elsewhere in the island cancels Clear without opening or removing files");
            Click(clear); await Task.Delay(5200);
            check(!shelf.IsClearArmed && !Field<DispatcherTimer>(shelf, "_clearExpiry").IsEnabled && model.Count == paths.Length,
                "Five-second Clear confirmation expires once and leaves no idle timer");
            check(model.ViewStyle == ShelfViewStyle.Grid && model.PageEntries.Count == 6, "Shelf defaults to a compact six-file Grid page");
            var collection = model.PageEntries; var first = collection[0];
            foreach (var view in new[] { ShelfViewStyle.Grid, ShelfViewStyle.Line, ShelfViewStyle.TwoColumn })
            {
                while (model.ViewStyle != view) model.CycleView();
                while (model.CanPrevious) model.Previous();
                var ordered = new List<string>();
                do { ordered.AddRange(model.PageEntries.Select(file => file.Path)); if (!model.CanNext) break; model.Next(); } while (true);
                check(ordered.SequenceEqual(paths), $"{view}: paging enumerates every staged file in original order exactly once");
            }
            var actions = new Files(); shelf.FileActions = actions;
            foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light, ThemeMode.System })
            {
                settings.ThemeMode = mode;
                foreach (var style in new[] { ShelfViewStyle.Grid, ShelfViewStyle.Line, ShelfViewStyle.TwoColumn })
                {
                    while (model.ViewStyle != style) model.CycleView();
                    while (model.CanPrevious) model.Previous();
                    island.UpdateLayout();
                    var items = (ItemsControl)shelf.FindName("Files");
                    var buttons = Descendants<Button>(items).ToArray();
                    check(ReferenceEquals(collection, model.PageEntries) && ReferenceEquals(first, model.PageEntries[0]) && model.PageEntries.Count <= model.PageSize,
                        $"{mode}/{style}: one ordered collection and the same file records back the bounded visible page");
                    check(buttons.Length == model.PageEntries.Count * (style == ShelfViewStyle.Line ? 3 : 1) &&
                        buttons.Count(button => AutomationProperties.GetName(button) == "Copy file path") == (style == ShelfViewStyle.Line ? 4 : 0),
                        $"{mode}/{style}: Reveal and Copy exist only in Line view; no separate Open button is rendered");
                    check(Descendants<TextBlock>(items).Where(text => text.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path == "Name")
                        .All(text => text.TextTrimming == TextTrimming.CharacterEllipsis), $"{mode}/{style}: long names use ellipsis and file cards retain full-path tooltips");
                    Save(island, Path.Combine(directory, $"step182-shelf-{mode}-{style}.png"));
                }
                Invoke(island, "SelectDashboard", Dashboard.QuickControls); await Task.Delay(50);
                Save(island, Path.Combine(directory, $"step182-quick-{mode}.png"));
                Invoke(island, "SelectDashboard", Dashboard.FileShelf); header.OpenNavigation(); await Task.Delay(30);
                Save(island, Path.Combine(directory, $"step182-overlay-{mode}.png")); header.CloseNavigation();
            }
            while (model.ViewStyle != ShelfViewStyle.Grid) model.CycleView();
            Click((Button)shelf.FindName("ViewButton")); check(model.ViewStyle == ShelfViewStyle.Line, "View button cycles Grid to Line");
            Click((Button)shelf.FindName("ViewButton")); check(model.ViewStyle == ShelfViewStyle.TwoColumn, "View button cycles Line to Two-Column");
            Invoke(island, "SelectDashboard", Dashboard.Main); Invoke(island, "SelectDashboard", Dashboard.FileShelf);
            check(model.ViewStyle == ShelfViewStyle.TwoColumn, "Selected view survives dashboard changes for the session");
            Click((Button)shelf.FindName("ViewButton")); check(model.ViewStyle == ShelfViewStyle.Grid, "View button cycles Two-Column to Grid");
            shelf.OpenFile(first); check(actions.Opens == 1, "Opening a staged file routes its original path to the registered application boundary");
            model.CycleView(); island.UpdateLayout();
            var nested = Descendants<Button>((ItemsControl)shelf.FindName("Files")).ToArray();
            Click(nested.First(button => AutomationProperties.GetName(button) == "Show in File Explorer"));
            Click(nested.First(button => AutomationProperties.GetName(button) == "Copy file path"));
            check(actions.Opens == 1 && actions.Reveals == 1 && actions.Copies == 1, "Nested Reveal/Copy actions do not open the file");
            File.Move(first.Path, first.Path + ".moved", true);
            shelf.OpenFile(first); check(actions.Opens == 1 && model.Status.Contains("missing"), "Missing or inaccessible files show an error instead of opening");
            File.Move(first.Path + ".moved", first.Path, true);
            Click(clear); island.Hide();
            check(!shelf.IsClearArmed && !Field<DispatcherTimer>(shelf, "_clearExpiry").IsEnabled && !Field<DispatcherTimer>(island, "_fileDragLeave").IsEnabled,
                "Hiding releases Clear confirmation and drag-leave timers");
            check(paths.All(File.Exists) && paths.Select(File.GetLastWriteTimeUtc).SequenceEqual(originals),
                "Clear, paging, view changes and controlled actions preserve every original file and its modification time");
        }
        finally { island.Close(); await settings.FlushAsync(); }
    }
    internal static DragEventArgs Drag(MainWindow island, IDataObject data, DragDropEffects allowed, RoutedEvent routed)
    {
        var bounds = ((IslandSurface)island.FindName("IslandSurface")).VisualBounds;
        var point = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        var e = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { data, DragDropKeyStates.LeftMouseButton, allowed, island, point }, null)!;
        e.RoutedEvent = routed; return e;
    }
    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static async Task Until(Func<bool> condition, int milliseconds)
    { var time = Stopwatch.StartNew(); while (!condition() && time.ElapsedMilliseconds < milliseconds) await Task.Delay(15); }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal static void Save(FrameworkElement element, string path)
    {
        element.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
    private static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
    private static void Invoke(object source, string name, params object[] args) => source.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(source, args);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
}
