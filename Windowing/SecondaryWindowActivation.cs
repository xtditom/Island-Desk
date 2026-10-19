using System.Windows;
using System.Windows.Input;

namespace IslandDesk.Windowing;

// Only explicit user requests use this path. Fullscreen/visibility restoration
// continues to use the existing non-activating paths.
internal static class SecondaryWindowActivation
{
    internal static void Show(Window window)
    {
        window.ShowActivated = true;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        if (!window.IsVisible) window.Show();
        Activate(window);
    }

    internal static void Activate(Window window)
    {
        // A disabled Settings owner cannot take foreground while its modal is
        // pending. Bring the visible owned dialog forward instead; do not bypass it.
        var owned = window.OwnedWindows.OfType<Window>().FirstOrDefault(child => child.IsVisible);
        if (owned is not null) { Activate(owned); return; }
        // WPF Activate uses SetForegroundWindow and respects its restrictions.
        // Do not retry, join input queues, or toggle Topmost if Windows refuses.
        // IsActive describes this thread's active window; it can be true while
        // another process still owns the foreground. Always make this one request.
        if (window.Activate() && FocusManager.GetFocusedElement(window) is { } focused)
            Keyboard.Focus(focused);
    }
}
