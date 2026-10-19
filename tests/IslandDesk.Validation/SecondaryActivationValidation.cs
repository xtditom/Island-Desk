using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using IslandDesk;
using IslandDesk.Presentation;
using IslandDesk.Settings;
using IslandDesk.Views;

internal static class SecondaryActivationValidation
{
    private sealed class Startup : IStartupRegistration { public bool IsEnabled() => false; public void SetEnabled(bool value) { } }
    public static async Task RunAsync(string directory, Action<bool, string> check)
    {
        using var settings = new SettingsController(Application.Current.Dispatcher,
            new PreferencesStore(Path.Combine(directory, "secondary-activation-settings.json")), new Startup());
        settings.Update(new AppSettings { ExpansionMode = ExpansionMode.Click, CollapseMode = ExpansionMode.Click });
        var island = new MainWindow(false, settings);
        var islandActivations = 0;
        island.Activated += (_, _) => islandActivations++;
        Process? provider = null;
        try
        {
            island.Show(); await Task.Delay(200);
            var main = new WindowInteropHelper(island).Handle;
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { typeof(SecondaryActivationValidation).Assembly.Location, "--fullscreen-provider", main.ToInt64().ToString() }) start.ArgumentList.Add(arg);
            provider = Process.Start(start)!;
            async Task<nint> Command(string command)
            {
                await provider.StandardInput.WriteLineAsync(command); await provider.StandardInput.FlushAsync();
                var reply = await provider.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8));
                return new nint(long.Parse(reply ?? throw new InvalidOperationException("Provider closed: " + await provider.StandardError.ReadToEndAsync())));
            }
            async Task<nint> OtherApp(bool grant = true)
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundProcess);
                if (foregroundProcess == Environment.ProcessId && !AllowSetForegroundWindow((uint)provider.Id))
                    throw new InvalidOperationException("Could not grant controlled provider foreground permission.");
                // The controlled foreground process explicitly grants permission,
                // modeling the permission a real user click gives IslandDesk.
                var other = await Command(grant ? "activation-allow:" + Environment.ProcessId : "activation-plain");
                Console.WriteLine($"Foreground fixture HWND=0x{other:X}");
                check(GetForegroundWindow() == other, "Separate controlled application is foreground before explicit action");
                return other;
            }
            void Normal(Window window, string label, bool standalone = true)
            {
                var handle = new WindowInteropHelper(window).Handle;
                Console.WriteLine($"{label}: HWND=0x{handle:X}, foreground=0x{GetForegroundWindow():X}, active={window.IsActive}, visible={window.IsVisible}, owner=0x{GetWindow(handle, 4):X}, exstyle=0x{GetWindowLong(handle, -20):X}");
                check(window.ShowActivated && !window.Topmost && (GetWindowLong(handle, -20) & 0x08000088) == 0,
                    label + " has normal activatable, non-topmost native styles");
                var owner = GetWindow(handle, 4);
                if (standalone) check(window.Owner is null && owner != main && owner != GetWindow(main, 4) &&
                    (owner == 0 || (GetWindowLong(owner, -20) & 0x08000008) == 0),
                    label + " has no island owner or hidden-owner NOACTIVATE/topmost contamination");
                check(GetForegroundWindow() == handle, label + " is foreground on the first request");
                check(!island.IsActive && GetForegroundWindow() != main && (GetWindowLong(main, -20) & 0x08000088) == 0x08000088,
                    "Main island retains NOACTIVATE, TOOLWINDOW, TOPMOST and never activates");
            }

            await OtherApp();
            island.OpenSettings(); await Task.Delay(150);
            var first = Field<SettingsWindow>(island, "_settingsWindow"); Normal(first, "New Settings");
            var editor = (TextBox)first.FindName("TitleInput"); editor.Focus();
            check(editor.IsKeyboardFocused, "Settings accepts keyboard focus without a second open");
            await OtherApp(); island.OpenSettings(); await Task.Delay(100);
            check(ReferenceEquals(first, Field<SettingsWindow>(island, "_settingsWindow")) && Application.Current.Windows.OfType<SettingsWindow>().Count() == 1,
                "Reopen uses exactly the existing Settings instance"); Normal(first, "Existing Settings");
            first.WindowState = WindowState.Minimized;
            await OtherApp(); island.OpenSettings(); await Task.Delay(100);
            check(first.WindowState == WindowState.Normal, "Explicit reopen restores minimized Settings"); Normal(first, "Restored Settings");

            // A normal secondary owns its confirmation and is disabled by WPF modal behavior.
            var inspected = new TaskCompletionSource();
            _ = Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
            {
                var dialog = Application.Current.Windows.OfType<ConfirmationWindow>().Single();
                try
                {
                    Normal(dialog, "Owned confirmation", false);
                    check(ReferenceEquals(dialog.Owner, first) && !IsWindowEnabled(new WindowInteropHelper(first).Handle) && !dialog.ShowInTaskbar,
                        "Settings confirmation is owned, modal and above its relevant parent");
                    check(((Button)dialog.FindName("CancelButton")).IsKeyboardFocused, "Confirmation defaults keyboard focus to Cancel");
                    await OtherApp();
                    island.OpenSettings(); await Task.Delay(80);
                    Console.WriteLine($"Modal reopen: foreground=0x{GetForegroundWindow():X}, expected=0x{new WindowInteropHelper(dialog).Handle:X}, ownerEnabled={IsWindowEnabled(new WindowInteropHelper(first).Handle)}, owned={first.OwnedWindows.Count}, visible={dialog.IsVisible}, sameSettings={ReferenceEquals(first, Field<SettingsWindow>(island, "_settingsWindow"))}");
                    check(GetForegroundWindow() == new WindowInteropHelper(dialog).Handle && !IsWindowEnabled(new WindowInteropHelper(first).Handle),
                        "Reopening Settings with a pending modal brings that confirmation forward without bypassing modality");
                    await OtherApp(false); // A late close must not take focus back from the user's new app.
                    inspected.SetResult();
                }
                catch (Exception e) { inspected.SetException(e); }
                finally { dialog.Close(); }
            }));
            check(!ConfirmationWindow.Confirm(first, "Activation check", "Owned test confirmation", "Confirm"), "Owned confirmation cancels safely");
            await inspected.Task;
            var external = GetForegroundWindow();
            check(external != new WindowInteropHelper(first).Handle && external != main, "Closing an inactive modal preserves the other application's foreground");
            first.Close(); await Task.Delay(150);
            check(Field<SettingsWindow?>(island, "_settingsWindow") is null && GetForegroundWindow() == external,
                "Closing inactive Settings clears the instance without activating the island or another app");
            await OtherApp(); island.OpenSettings(); await Task.Delay(100);
            var second = Field<SettingsWindow>(island, "_settingsWindow");
            check(!ReferenceEquals(first, second), "Settings can create a fresh instance after closing"); Normal(second, "Reopened Settings");
            second.CloseImmediately(); await Task.Delay(80);
            check(!island.IsActive && GetForegroundWindow() != main, "Closing active Settings does not activate the island");

            await OtherApp(); Invoke(island, "SetExpanded", true); Invoke(island, "SelectDashboard", Dashboard.QuickControls);
            await Task.Delay(200);
            var quick = (QuickControlsDashboard)island.FindName("QuickControlsDashboard");
            var cancelled = new TaskCompletionSource();
            _ = Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<ConfirmationWindow>().Single();
                try { Normal(dialog, "Lock confirmation"); check(!dialog.ShowInTaskbar && dialog.IsEnabled, "Standalone modal preserves taskbar behavior without using the island as activation owner"); cancelled.SetResult(); }
                catch (Exception e) { cancelled.SetException(e); }
                finally { dialog.Close(); }
            }));
            ((Button)quick.FindName("LockButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await cancelled.Task;
            check(!quick.IsInteracting && GetForegroundWindow() != main, "Canceling Lock releases modality without locking or activating the island");

            await OtherApp(); Invoke(island, "RenameTitle"); await Task.Delay(120);
            var rename = Field<RenameWindow>(island, "_renameWindow"); Normal(rename, "Rename tool");
            check(((TextBox)rename.FindName("TitleInput")).IsKeyboardFocused, "Rename receives text input immediately");
            await OtherApp(); Invoke(island, "RenameTitle"); await Task.Delay(80);
            check(ReferenceEquals(rename, Field<RenameWindow>(island, "_renameWindow")), "Explicit rename reopens the same existing tool"); Normal(rename, "Existing rename");
            await OtherApp(false); rename.Close(); await Task.Delay(80);
            check(GetForegroundWindow() != main && !island.IsActive, "Closing rename never activates the island");
            check(islandActivations == 0, "Island receives zero activation events throughout secondary-window interactions");
            await Command("quit"); await provider.WaitForExitAsync();
        }
        finally
        {
            foreach (var dialog in Application.Current.Windows.OfType<ConfirmationWindow>().ToArray()) dialog.Close();
            island.Close();
            if (provider is { HasExited: false }) { provider.StandardInput.Close(); await provider.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            provider?.Dispose();
        }
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint process);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
}
