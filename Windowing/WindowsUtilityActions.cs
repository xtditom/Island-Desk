using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
namespace IslandDesk.Windowing;

internal readonly record struct UtilityResult(bool Success, string Message);
internal interface IWindowsUtilityActions
{
    UtilityResult OpenScreenshot(nint widget);
    UtilityResult OpenClipboardHistory(nint widget);
    UtilityResult OpenTaskManager();
    UtilityResult OpenWindowsSearch(nint widget);
    UtilityResult LockDevice();
    UtilityResult OpenEmojiPanel(nint widget);
}
internal readonly record struct ShortcutStroke(ushort Key, bool Up = false);
internal interface IShortcutInput
{
    nint Foreground { get; }
    bool IsPressed(int key);
    uint Send(ShortcutStroke[] strokes);
}

// Click-only shell integration. No registry writes, global hooks or polling.
internal sealed class WindowsUtilityActions : IWindowsUtilityActions
{
    private readonly IShortcutInput _input;
    internal WindowsUtilityActions(IShortcutInput? input = null) => _input = input ?? new NativeShortcutInput();
    public UtilityResult OpenScreenshot(nint widget) => SendShortcut(widget, 0x53, "Win + Shift + S", "Screen snipping", shift: true);
    public UtilityResult OpenWindowsSearch(nint widget) => SendShortcut(widget, 0x53, "Win + S", "Windows Search");
    public UtilityResult OpenEmojiPanel(nint widget) => SendShortcut(widget, 0xBE, "Win + .", "Emoji panel");
    public UtilityResult ShowDesktop(nint widget) => SendShortcut(widget, 0x44, "Win + D", "Show desktop");
    public UtilityResult OpenTaskView(nint widget) => SendShortcut(widget, 0x09, "Win + Tab", "Task View");
    public UtilityResult OpenRun(nint widget)
    {
        // The shell's existing Run dialog owns activation and text input. Do not
        // inject another shortcut while it is already visible.
        var run = FindWindow("#32770", "Run");
        if (run != 0 && GetDlgItem(run, 0x4D) != 0)
        {
            SetForegroundWindow(run);
            return new(true, "Existing Run dialog requested in the foreground.");
        }
        return SendShortcut(widget, 0x52, "Win + R", "Run dialog");
    }
    public UtilityResult OpenClipboardHistory(nint widget)
    {
        bool? enabled = null;
        try { enabled = Windows.ApplicationModel.DataTransfer.Clipboard.IsHistoryEnabled(); }
        catch (Exception e) when (e is COMException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException) { }
        var result = SendShortcut(widget, 0x56, "Win + V", "Clipboard history");
        return result.Success && enabled == false ? result with
        { Message = "Clipboard history is off. Windows may offer Turn on in the panel; policy can prevent enabling it." } : result;
    }
    public UtilityResult OpenTaskManager()
    {
        try
        {
            // Resolve the Windows executable explicitly, without PATH lookup or an elevation verb.
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Taskmgr.exe"))
                { UseShellExecute = false });
            return process is null ? new(false, "Windows could not launch Task Manager.") : new(true, "Task Manager launch requested.");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or UnauthorizedAccessException or IOException)
        { return new(false, "Windows could not launch Task Manager. It may be restricted by policy."); }
    }
    public UtilityResult LockDevice() => LockWorkStation()
        ? new(true, "Lock requested.") : new(false, "Windows could not lock this device. " + new Win32Exception(Marshal.GetLastWin32Error()).Message);

    private UtilityResult SendShortcut(nint widget, ushort key, string shortcut, string panel, bool shift = false)
    {
        var foreground = _input.Foreground;
        if (foreground == 0 || foreground == widget)
            return new(false, $"Focus your destination app, then try {panel} again.");
        // Never combine with or release keys the user is already holding.
        if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, key }.Any(_input.IsPressed))
            return new(false, "Release the modifier/shortcut keys, then try again.");
        ushort[] keys = shift ? [0x5B, 0x10, key] : [0x5B, key];
        var inputs = keys.Select(value => new ShortcutStroke(value)).Concat(keys.Reverse().Select(value => new ShortcutStroke(value, true))).ToArray();
        var sent = _input.Send(inputs);
        if (sent != inputs.Length)
        {
            // Clean up only keys from the accepted prefix, including a partial key-up sequence.
            var held = new List<ushort>();
            foreach (var input in inputs.Take((int)Math.Min(sent, (uint)inputs.Length)))
            {
                if (!input.Up) held.Add(input.Key); else held.Remove(input.Key);
            }
            var releases = held.AsEnumerable().Reverse().Select(value => new ShortcutStroke(value, true)).ToArray();
            if (releases.Length != 0) _input.Send(releases);
            return new(false, $"Windows blocked the {panel} shortcut. Try {shortcut} in your app; elevated apps or policy may block input.");
        }
        // Injection is acknowledged, not shell UI availability. Search uses Win+S, never Explorer.
        return new(true, $"{panel} shortcut sent. If no panel appears, use {shortcut}; Windows or policy may restrict it.");
    }
    private sealed class NativeShortcutInput : IShortcutInput
    {
        public nint Foreground => GetForegroundWindow();
        public bool IsPressed(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
        public uint Send(ShortcutStroke[] strokes)
        {
            var inputs = strokes.Select(stroke => Key(stroke.Key, stroke.Up)).ToArray();
            return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        }
    }
    private static Input Key(ushort key, bool up = false) => new() { Type = 1, Data = new() { Keyboard = new() { VirtualKey = key, Flags = up ? 2u : 0 } } };
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData
    { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool LockWorkStation();
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? className, string? title);
    [DllImport("user32.dll")] private static extern nint GetDlgItem(nint window, int id);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
