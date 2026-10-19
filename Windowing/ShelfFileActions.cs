using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace IslandDesk.Windowing;

internal interface IShelfFileActions
{
    UtilityResult Open(string path);
    UtilityResult Reveal(string path);
    UtilityResult CopyPath(string path);
}
internal sealed class ShelfFileActions : IShelfFileActions
{
    public UtilityResult Open(string path) => Try(() => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }), "Open requested.");
    public UtilityResult Reveal(string path) => Try(() =>
    {
        nint item = 0;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(path, 0, out item, 0, out _));
            Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(item, 0, 0, 0));
        }
        finally { if (item != 0) Marshal.FreeCoTaskMem(item); }
        return null;
    }, "Show in File Explorer requested.");
    public UtilityResult CopyPath(string path) => Try(() => { Clipboard.SetText(path); return null; }, "File path copied.");
    private static UtilityResult Try(Func<Process?> action, string message)
    {
        try { using var process = action(); return new(true, message); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or UnauthorizedAccessException or System.IO.IOException or ExternalException)
        { return new(false, "Windows could not complete this action. The file may be unavailable, or the clipboard may be busy."); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint context, out nint item, uint mask, out uint attributes);
    [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(nint folder, uint count, nint children, uint flags);
}
