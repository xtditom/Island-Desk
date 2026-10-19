using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using IslandDesk;
using IslandDesk.Controls;
using IslandDesk.Presentation;
using IslandDesk.Settings;
using IslandDesk.Views;
using IslandDesk.Windowing;
using ThemeMode = IslandDesk.Settings.ThemeMode;

// Interactive owned-fixture host. Only real mouse/OLE input produces drag events here.
internal static class ShelfLiveValidation
{
    private sealed class Startup : IStartupRegistration { public bool IsEnabled() => false; public void SetEnabled(bool value) { } }
    public static void Run(Application app, string directory)
    {
        var fixture = Path.Combine(directory, "live-owned-files"); Directory.CreateDirectory(fixture);
        var paths = new[] { Path.Combine(fixture, "Live drag one.txt"), Path.Combine(fixture, "Live drag two.txt") };
        foreach (var path in paths) File.WriteAllText(path, "Owned live-validation file. Keep the original.");
        var settings = new SettingsController(app.Dispatcher, new PreferencesStore(Path.Combine(directory, "live-settings.json")), new Startup());
        settings.Update(new AppSettings { ThemeMode = ThemeMode.Dark, YOffset = 200, ExpansionMode = ExpansionMode.Click, CollapseMode = ExpansionMode.Click });
        var island = new MainWindow(false, settings) { Title = "IslandDesk Step18.2 drop target" };
        var vm = Field<IslandViewModel>(island, "_viewModel");
        var shelf = (FileShelfDashboard)island.FindName("FileShelfDashboard");
        var source = new Window { Title = "IslandDesk Step18.2 live validation", Width = 1100, Height = 680, Left = Math.Max(10, SystemParameters.PrimaryScreenWidth / 2 - 550), Top = 110,
            Background = new SolidColorBrush(Color.FromRgb(32, 37, 45)), Foreground = Brushes.White };
        var grid = new Grid { Margin = new Thickness(24) }; source.Content = grid;
        var instructions = new TextBlock { Text = "Real OLE drag validation\nDrag the blue source card onto the separate IslandDesk pill above.\nOnly owned temporary files are used. File opens are logged, not launched.", FontSize = 17, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 590, 0) };
        grid.Children.Add(instructions);
        var sourceCard = new Border { Width = 300, Height = 110, Background = Brushes.SteelBlue, CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(25, 265, 0, 0),
            Child = new TextBlock { Text = "DRAG TWO TEST FILES\nLive drag one.txt + Live drag two.txt", TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 15, Foreground = Brushes.White } };
        grid.Children.Add(sourceCard);
        var status = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Bottom, Height = 155, Foreground = Brushes.White, Background = Brushes.Transparent };
        grid.Children.Add(status);
        var trace = new List<string>();
        void Log(string value)
        {
            trace.Add(value); status.Text = string.Join(Environment.NewLine, trace.TakeLast(8));
            File.WriteAllText(Path.Combine(directory, "step182-live.json"), JsonSerializer.Serialize(new { Events = trace, Count = shelf.Model.Count, OriginalsExist = paths.All(File.Exists), Foreground = GetForegroundWindow().ToInt64(), Source = new WindowInteropHelper(source).Handle.ToInt64(), Target = new WindowInteropHelper(island).Handle.ToInt64() }, new JsonSerializerOptions { WriteIndented = true }));
        }
        var actions = new LogFiles(Log); shelf.FileActions = actions;
        Point? start = null;
        sourceCard.MouseLeftButtonDown += (_, e) => { start = e.GetPosition(sourceCard); Mouse.Capture(sourceCard); e.Handled = true; };
        sourceCard.MouseLeftButtonUp += (_, _) => { start = null; if (sourceCard.IsMouseCaptured) Mouse.Capture(null); };
        sourceCard.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || start is not { } point) return;
            var p = e.GetPosition(sourceCard);
            if (Math.Abs(p.X - point.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - point.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            start = null;
            if (sourceCard.IsMouseCaptured) Mouse.Capture(null);
            Log("Source begins native OLE drag; count=" + shelf.Model.Count);
            var result = DragDrop.DoDragDrop(sourceCard, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy);
            Log("Native source result=" + result);
        };
        island.AddHandler(DragDrop.PreviewDragEnterEvent, new DragEventHandler((_, e) => Log($"Native DragEnter: expanded={Field<bool>(island, "_isExpanded")}, shelf={vm.IsFileShelf}, count={shelf.Model.Count}, effect={e.Effects}")), true);
        island.AddHandler(DragDrop.PreviewDropEvent, new DragEventHandler(async (_, e) => { Log("Native Drop effect=" + e.Effects); await Task.Delay(300); Log("Drop complete: count=" + shelf.Model.Count + "; focusPreserved=" + (GetForegroundWindow() == new WindowInteropHelper(source).Handle)); }), true);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right };
        void Button(string text, Action action) { var b = new Button { Content = text, Padding = new Thickness(10), Margin = new Thickness(4), MinWidth = 95 }; b.Click += (_, _) => action(); controls.Children.Add(b); }
        Button("Reset pill", () => { Invoke(island, "SetExpanded", false); shelf.Model.Clear(); Log("Reset to collapsed pill"); });
        Button("Show Quick Controls", () => { Invoke(island, "SetExpanded", true); Invoke(island, "SelectDashboard", Dashboard.QuickControls); Log("Quick Controls shown"); });
        Button("Stop validation", () => source.Close());
        grid.Children.Add(controls);
        source.Closed += (_, _) => { island.Close(); settings.Dispose(); app.Shutdown(); };
        source.ContentRendered += (_, _) => { island.Show(); Log("Ready: collapsed non-activating pill"); };
        source.Show(); app.Run();
    }
    private sealed class LogFiles(Action<string> log) : IShelfFileActions
    {
        public UtilityResult Open(string path) { log("OPEN " + Path.GetFileName(path)); return new(true, "File open logged"); }
        public UtilityResult Reveal(string path) { log("REVEAL " + Path.GetFileName(path)); return new(true, "Reveal logged"); }
        public UtilityResult CopyPath(string path) { log("COPY " + Path.GetFileName(path)); return new(true, "Copy logged"); }
    }
    private static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
    private static void Invoke(object source, string name, params object[] args) => source.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(source, args);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
