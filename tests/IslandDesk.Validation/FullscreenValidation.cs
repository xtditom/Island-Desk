using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using IslandDesk;
using IslandDesk.Presentation;
using IslandDesk.Views;
using IslandDesk.Windowing;
using IslandDesk.Settings;

internal static class FullscreenValidation
{
    public static void VerifyPolicy(Action<bool, string> check)
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);
        var borderless = new FullscreenCandidate(monitor, monitor, 0, false);
        check(FullscreenPolicy.IsFullscreen(borderless, monitor), "Borderless foreground covering monitor is fullscreen");
        check(!FullscreenPolicy.IsFullscreen(borderless with { Frame = new(0, 0, 1920, 1040) }, monitor), "Working-area-only window is not fullscreen");
        check(!FullscreenPolicy.IsFullscreen(borderless with { IsMaximized = true, Style = 0x00C40000 }, monitor), "Ordinary maximized window is excluded even with auto-hide taskbar");
        check(!FullscreenPolicy.IsFullscreen(borderless with { Style = 0x00C00000, Client = new(0, 30, 1920, 1080) }, monitor), "Captioned window with non-fullscreen client is excluded");
        check(!FullscreenPolicy.IsFullscreen(borderless with { IsOwnProcess = true }, monitor), "IslandDesk-owned windows are excluded");
        check(!FullscreenPolicy.IsFullscreen(borderless with { WindowClass = "WorkerW" }, monitor) &&
            !FullscreenPolicy.IsFullscreen(borderless with { WindowClass = "Shell_TrayWnd" }, monitor), "Desktop and taskbar are excluded");
        check(!FullscreenPolicy.IsFullscreen(borderless with { ProcessName = "MyDockFinder" }, monitor), "MyDockFinder is excluded");
        check(!FullscreenPolicy.IsFullscreen(borderless with { IsCloaked = true }, monitor) &&
            !FullscreenPolicy.IsFullscreen(borderless with { IsMinimized = true }, monitor), "Cloaked and minimized windows are excluded");
        check(!FullscreenPolicy.IsFullscreen(borderless with { Frame = new(1920, 0, 3840, 1080) }, monitor), "Fullscreen on another monitor does not hide the island");
        var leftMonitor = new PixelRect(-2560, -200, 0, 1240);
        check(FullscreenPolicy.IsFullscreen(borderless with { Frame = leftMonitor, Client = leftMonitor }, leftMonitor), "Negative monitor coordinates are supported");
        check(FullscreenPolicy.IsFullscreen(borderless with { Frame = new(-2560, -200, 1920, 1240) }, monitor), "Spanning fullscreen covers the island monitor");
        check(FullscreenPolicy.IsFullscreen(borderless with { Frame = new(1, 1, 1919, 1079) }, monitor), "Tiny physical-pixel frame differences are tolerated");
    }

    public static async Task VerifyNativeAsync(Action<bool, string> check)
    {
        VerifyPolicy(check);
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "artifacts", "fullscreen-settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        using var testSettings = new SettingsController(Application.Current.Dispatcher, new PreferencesStore(settingsPath));
        var island = new MainWindow(enableFullscreenDetection: true, testSettings);
        Process? provider = null;
        try
        {
            island.Show(); ((Border)island.FindName("IslandSurface")).IsHitTestVisible = false;
            await Task.Delay(350);
            var hwnd = new WindowInteropHelper(island).Handle;
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true, RedirectStandardOutput = true };
            start.ArgumentList.Add(typeof(FullscreenValidation).Assembly.Location);
            start.ArgumentList.Add("--fullscreen-provider"); start.ArgumentList.Add(hwnd.ToInt64().ToString());
            provider = Process.Start(start)!;
            var writer = provider.StandardInput;
            var reader = provider.StandardOutput;
            async Task<nint> Command(string command)
            {
                await writer.WriteLineAsync(command);
                await writer.FlushAsync();
                var reply = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
                if (reply is null) throw new InvalidOperationException("Fullscreen provider closed.");
                return new nint(long.Parse(reply));
            }
            var foreground = await Command("normal");
            Console.WriteLine($"Provider HWND=0x{foreground:X}; foreground=0x{GetForegroundWindow():X}; island visible={island.IsVisible}; fullscreen={Field<bool>(island, "_hiddenForFullscreen")}");
            await WaitAsync(() => GetForegroundWindow() == foreground && island.IsVisible);
            var observer = Field<FullscreenObserver>(island, "_fullscreenObserver");
            await Task.Delay(150);
            check(Field<nint>(observer, "_foregroundHook") != 0 && Field<nint>(observer, "_locationHook") != 0, "Foreground and process-scoped location WinEvent hooks installed");
            check(Field<System.Windows.Threading.DispatcherTimer>(observer, "_fallbackTimer").Interval == TimeSpan.FromSeconds(2), "Foreground fallback is limited to a two-second interval");
            ((IslandDesk.Controls.Equalizer)island.FindName("MusicEqualizer")).IsRunning = false;
            using (var currentProcess = Process.GetCurrentProcess())
            {
                currentProcess.Refresh(); var cpu = currentProcess.TotalProcessorTime; var sample = Stopwatch.StartNew();
                await Task.Delay(5000); currentProcess.Refresh();
                Console.WriteLine($"Fullscreen observer sample, collapsed/normal foreground with bars stopped: {(currentProcess.TotalProcessorTime - cpu).TotalMilliseconds / sample.Elapsed.TotalMilliseconds * 100:0.00}% of one CPU core; {currentProcess.PrivateMemorySize64 / 1048576.0:0.0} MiB private (5 s validation-process sample)");
            }
            Invoke(island, "SetExpanded", true); Invoke(island, "SelectDashboard", Dashboard.System);
            await Task.Delay(350);
            await Command("maximized");
            Console.WriteLine($"Maximized provider: foreground=0x{GetForegroundWindow():X}; expected=0x{foreground:X}; visible={island.IsVisible}; hiddenForFullscreen={Field<bool>(island, "_hiddenForFullscreen")}");
            await WaitAsync(() => island.IsVisible && GetForegroundWindow() == foreground);
            check(island.IsVisible && GetForegroundWindow() == foreground, "Native maximized foreground application leaves island visible without activation");
            var elapsed = Stopwatch.StartNew();
            await Command("fullscreen");
            await WaitAsync(() => !island.IsVisible);
            Console.WriteLine($"Native fullscreen hide latency: {elapsed.Elapsed.TotalMilliseconds:0} ms");
            check(!island.IsVisible && GetForegroundWindow() == foreground, "Foreground fullscreen hides island without activation");
            var services = Field<IslandServices>(island, "_services");
            check(Field<object?>(services, "_hardwareCancellation") is null && Field<object?>(services, "_mediaCancellation") is null &&
                Field<object?>(services, "_volumeCancellation") is null, "Fullscreen hiding cancels hardware, media and volume lifetimes");
            island.Show(); await Task.Delay(100);
            check(!island.IsVisible, "Explicit show cannot expose island while fullscreen remains active");
            var settings = Field<SettingsController>(island, "_settings");
            // Keep validation changes away from the user's preferences file.
            settings.Update(settings.Current with { HideInFullscreen = false });
            check(island.IsVisible && Field<FullscreenObserver?>(island, "_fullscreenObserver") is null && GetForegroundWindow() == foreground,
                "Disabling fullscreen hiding disposes hooks and restores without activation");
            settings.HideInFullscreen = true;
            await WaitAsync(() => !island.IsVisible);
            check(Field<FullscreenObserver?>(island, "_fullscreenObserver") is not null, "Re-enabling fullscreen hiding installs hooks and hides immediately");
            await Command("normal"); await WaitAsync(() => island.IsVisible);
            check(Field<bool>(island, "_isExpanded") && Field<IslandViewModel>(island, "_viewModel").IsSystem,
                "Leaving fullscreen preserves expanded state and selected dashboard");
            check(GetForegroundWindow() == foreground && (GetWindowLong(hwnd, -20) & 0x08000088) == 0x08000088,
                "Automatic restore preserves foreground, NOACTIVATE, TOOLWINDOW and TOPMOST");
            Invoke(island, "SetExpanded", false); await Task.Delay(300);
            await Command("fullscreen"); await WaitAsync(() => !island.IsVisible);
            await Command("normal"); await WaitAsync(() => island.IsVisible);
            check(!Field<bool>(island, "_isExpanded"), "Fullscreen restore preserves collapsed state");
            island.Hide(); await Command("fullscreen");
            await WaitAsync(() => Field<bool>(island, "_hiddenForFullscreen"));
            await Command("normal"); await WaitAsync(() => !Field<bool>(island, "_hiddenForFullscreen"));
            check(!island.IsVisible, "Fullscreen exit does not reopen an independently hidden widget");
            island.Show(); Invoke(island, "SetExpanded", true); Invoke(island, "RenameTitle");
            var rename = Field<RenameWindow>(island, "_renameWindow");
            await Task.Delay(150);
            await Command("fullscreen"); await WaitAsync(() => !island.IsVisible);
            check(!rename.IsVisible, "Fullscreen also hides an open rename tool");
            await Command("normal"); await WaitAsync(() => island.IsVisible && rename.IsVisible);
            check(GetForegroundWindow() == foreground, "Rename tool restores without stealing foreground");
            rename.Close();
            await Command("quit");
            await provider.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            check(provider.ExitCode == 0, "Native test provider exits cleanly");
        }
        finally
        {
            island.Close();
            await testSettings.FlushAsync(); File.Delete(settingsPath);
            if (provider is not null)
            {
                if (!provider.HasExited) provider.Kill();
                provider.Dispose();
            }
        }
    }

    public static void RunProvider(Application app, nint island)
    {
        var window = new Window { Title = "IslandDesk fullscreen validation provider", Width = 460, Height = 260,
            Content = new TextBlock { Text = "IslandDesk native fullscreen test", Margin = new Thickness(24) } };
        var started = false;
        window.Loaded += async (_, _) =>
        {
            if (started) return; started = true;
            try
            {
                string? command;
                while ((command = await Task.Run(() => Console.ReadLine())) is not null)
                {
                    var monitor = MonitorFromWindow(island, 2);
                    var info = new MonitorInformation { Size = (uint)Marshal.SizeOf<MonitorInformation>() };
                    if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("Monitor unavailable.");
                    window.WindowState = WindowState.Normal;
                    if (command == "fullscreen")
                    {
                        window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.NoResize;
                        SetWindowPos(new WindowInteropHelper(window).Handle, 0, info.Monitor.Left, info.Monitor.Top,
                            info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top, 4);
                    }
                    else
                    {
                        window.WindowStyle = WindowStyle.SingleBorderWindow; window.ResizeMode = ResizeMode.CanResize;
                        window.Left = info.Work.Left + 80; window.Top = info.Work.Top + 120; window.Width = 460; window.Height = 260;
                        if (command == "maximized") window.WindowState = WindowState.Maximized;
                    }
                    window.Activate();
                    // Test provider only: Windows may deny ordinary background-process
                    // activation. Briefly join input queues to foreground this controlled
                    // test window; IslandDesk production code never manipulates focus.
                    var handle = new WindowInteropHelper(window).Handle;
                    if (GetForegroundWindow() != handle)
                    {
                        var currentThread = GetCurrentThreadId();
                        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                        var joined = foregroundThread != 0 && foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
                        try { SetForegroundWindow(handle); }
                        finally { if (joined) AttachThreadInput(currentThread, foregroundThread, false); }
                    }
                    Console.WriteLine(new WindowInteropHelper(window).Handle.ToInt64().ToString());
                    if (command == "quit") break;
                }
            }
            finally { window.Close(); app.Shutdown(); }
        };
        window.Show(); app.Run();
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (!condition())
        { if (DateTime.UtcNow > deadline) throw new TimeoutException("Fullscreen state did not settle."); await Task.Delay(30); }
    }
    private static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
    private static void Invoke(object source, string name, params object[] args) => source.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(source, args);
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInformation { public uint Size; public PixelRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInformation information);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
}
