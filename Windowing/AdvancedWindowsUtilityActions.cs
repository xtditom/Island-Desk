using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace IslandDesk.Windowing;

// The execution-state request belongs to the stable WPF UI thread. Both toggle
// and disposal verify that thread; hiding a dashboard does not revoke the toggle.
internal sealed class KeepAwakeRequest(Dispatcher dispatcher) : IDisposable
{
    public bool IsActive { get; private set; }
    public UtilityResult Toggle()
    {
        dispatcher.VerifyAccess();
        var enabled = !IsActive;
        if (SetThreadExecutionState(0x80000000u | (enabled ? 0x00000001u : 0)) == 0)
            return new(false, "Windows could not change the keep-awake request.");
        IsActive = enabled;
        return new(true, enabled ? "Keep awake is on. Automatic sleep is prevented; manual sleep still works." : "Keep awake is off.");
    }
    public void Dispose()
    {
        dispatcher.VerifyAccess();
        if (!IsActive) return;
        SetThreadExecutionState(0x80000000u); // ES_CONTINUOUS: release this thread's request.
        IsActive = false;
    }
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
}

internal static class AdvancedWindowsUtilityActions
{
    public static UtilityResult OpenSettings(string page)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(page) { UseShellExecute = true });
            return new(true, "Windows Settings launch requested.");
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        { return new(false, "Windows could not open Settings. " + error.Message); }
    }
    public static async Task<UtilityResult> TurnOffDisplayAsync()
    {
        // Let the initiating button-up and its queued pointer messages drain.
        // This is a one-shot delay, with no power-plan changes or sleep request.
        await Task.Delay(700);
        return await Task.Run(() => SendMessageTimeout(new nint(0xFFFF), 0x0112, new nint(0xF170), new nint(2),
            0x2, 1500, out _) != 0
            ? new UtilityResult(true, "Display-off requested. Move the mouse or press a key to wake displays.")
            : new UtilityResult(false, "Windows could not confirm display-off. This display or session may not support it."));
    }
    public static UtilityResult OpenOnScreenKeyboard()
    {
        try
        {
            var existing = Process.GetProcessesByName("osk");
            try
            {
                var session = Process.GetCurrentProcess();
                using (session)
                    if (existing.Any(process => process.SessionId == session.SessionId))
                        return new(true, "On-Screen Keyboard is already running.");
            }
            finally { foreach (var process in existing) process.Dispose(); }
            // Sysnative bypasses WOW64 redirection for a 32-bit build on 64-bit Windows.
            var folder = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative")
                : Environment.GetFolderPath(Environment.SpecialFolder.System);
            using var launched = Process.Start(new ProcessStartInfo(Path.Combine(folder, "osk.exe")) { UseShellExecute = true });
            return new(true, "On-Screen Keyboard launch requested.");
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or UnauthorizedAccessException or IOException or NotSupportedException)
        { return new(false, "Windows could not open On-Screen Keyboard. " + error.Message); }
    }

    public static Task<UtilityResult> EmptyRecycleBinAsync() => Task.Run(() =>
    {
        // Our explicit confirmation precedes this call. No duplicate native dialog,
        // sound or progress window; the potentially slow operation stays off the UI.
        var result = SHEmptyRecycleBin(0, null, 0x1 | 0x2 | 0x4);
        if (result >= 0) return new UtilityResult(true, "Recycle Bin emptied.");
        if (result is unchecked((int)0x800704C7) or unchecked((int)0x80004004))
            return new UtilityResult(false, "Empty Recycle Bin was cancelled.");
        return new UtilityResult(false, "Windows could not empty the Recycle Bin. " + Marshal.GetExceptionForHR(result)?.Message);
    });

    public static UtilityResult SignOut() => ExitWindowsEx(0, 0) // EWX_LOGOFF; never FORCE or FORCEIFHUNG.
        ? new(true, "Sign-out requested. Windows or an application may cancel it.")
        : new(false, "Windows could not sign out. " + new Win32Exception(Marshal.GetLastWin32Error()).Message);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(nint owner, string? root, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ExitWindowsEx(uint flags, uint reason);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nuint result);
}
