using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using Forms = System.Windows.Forms;
using IslandDesk.Settings;
using IslandDesk.Themes;
using IslandDesk.Views;
using IslandDesk.Presentation;
using IslandDesk.Tray;
using IslandDesk.Infrastructure;

namespace IslandDesk;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private Icon? _icon;
    private SettingsController? _settings;
    private bool _exitRequested;
    private SettingsTrayMenu? _trayMenu;
    private SingleInstanceGuard? _instance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Acquire before awaiting settings or creating any windows/tray icons.
        _instance = SingleInstanceGuard.TryAcquire();
        if (_instance is null) { Shutdown(); return; }
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _settings = new(Dispatcher);
        await _settings.InitializeAsync(IslandDesk.MainWindow.ReadTopOffsetPixels());
        var window = new MainWindow(enableFullscreenDetection: true, _settings);
        MainWindow = window;
        _icon = ApplicationIconAssets.CreateTrayIcon() ?? CreateTrayIcon();
        _trayMenu = new(_settings, window.Theme, () => window.OpenSettings(), () => window.OpenSettings(editTitle: true), ExitAsync);

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "IslandDesk",
            ContextMenuStrip = _trayMenu.Menu,
            Visible = true
        };

        window.Closed += async (_, _) => await ExitAsync();
        window.Show();
    }

    internal async Task ExitAsync()
    {
        if (_exitRequested) return;
        _exitRequested = true;
        if (_settings is not null && !await _settings.FlushAsync() &&
            !ConfirmationWindow.Confirm(Windows.OfType<SettingsWindow>().FirstOrDefault(window => window.IsVisible),
                "Couldn't save preferences", "IslandDesk couldn't save preferences. Exit without saving?", "Exit anyway"))
        { _exitRequested = false; (MainWindow as MainWindow)?.OpenSettings(); return; }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip = null;
            _trayIcon.Dispose();
        }
        _trayMenu?.Dispose();
        _icon?.Dispose();
        _settings?.Dispose();
        base.OnExit(e);
        _instance?.Dispose();
    }

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var pill = new SolidBrush(Color.White))
        using (var background = new SolidBrush(Color.FromArgb(22, 23, 27)))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.FillEllipse(background, 1, 1, 30, 30);
            graphics.FillEllipse(pill, 7, 12, 8, 8);
            graphics.FillEllipse(pill, 17, 12, 8, 8);
            graphics.FillRectangle(pill, 11, 12, 10, 8);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
