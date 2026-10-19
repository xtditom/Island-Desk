namespace IslandDesk.Settings;

internal sealed record QuickControlInfo(string Id, string Name)
{
    public override string ToString() => Name;
}

internal static class QuickControlLayout
{
    public static readonly QuickControlInfo[] Controls =
    [
        new("Screenshot", "Screenshot"), new("ClipboardHistory", "Clipboard History"), new("TaskManager", "Task Manager"),
        new("Search", "Windows Search"), new("Lock", "Lock Device"), new("Emoji", "Emoji Panel"),
        new("KeepAwake", "Keep PC Awake"), new("EmptyRecycleBin", "Empty Recycle Bin"), new("OnScreenKeyboard", "On-Screen Keyboard"),
        new("Microphone", "Microphone Mute"), new("ShowDesktop", "Show Desktop"), new("SignOut", "Sign Out"),
        new("DisplayOff", "Turn Off Display"), new("Wifi", "Wi-Fi"), new("Bluetooth", "Bluetooth"),
        new("TaskView", "Task View"), new("Run", "Run Dialog"), new("Brightness", "Screen Brightness")
    ];
    public static string[] Normalize(IEnumerable<string>? saved)
    {
        var valid = Controls.Select(control => control.Id).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(Controls.Length);
        if (saved is not null)
            foreach (var id in saved)
                if (id is not null && valid.Contains(id) && seen.Add(id)) result.Add(id);
        foreach (var control in Controls)
            if (seen.Add(control.Id)) result.Add(control.Id);
        return result.ToArray();
    }
}
