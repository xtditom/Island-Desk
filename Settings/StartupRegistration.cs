using System.IO;
using Microsoft.Win32;

namespace IslandDesk.Settings;

internal interface IStartupRegistration
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}

internal sealed class StartupRegistration : IStartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _name;
    private readonly string _command;
    internal StartupRegistration(string name = "IslandDesk", string? command = null)
    { _name = name; _command = command ?? BuildCommand(ResolveExecutable(), typeof(App).Assembly.Location); }
    private static string ResolveExecutable()
    {
        // Setup records the installed copy. Even a development copy must not
        // overwrite its startup entry with a temporary publish/build path.
        try
        {
            using var deployment = Registry.CurrentUser.OpenSubKey(@"Software\IslandDesk\Deployment");
            return SelectExecutable(Environment.ProcessPath!, deployment?.GetValue("ExecutablePath") as string);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return Environment.ProcessPath!; }
    }
    internal static string SelectExecutable(string currentExecutable, string? installedExecutable) =>
        !string.IsNullOrWhiteSpace(installedExecutable) && Path.IsPathFullyQualified(installedExecutable) &&
        string.Equals(Path.GetFileName(installedExecutable), "IslandDesk.exe", StringComparison.OrdinalIgnoreCase) &&
        File.Exists(installedExecutable) ? installedExecutable : currentExecutable;
    internal static string BuildCommand(string executable, string assembly) =>
        string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"\"{executable}\" \"{assembly}\"" : $"\"{executable}\"";
    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(_name) is string value && !string.IsNullOrWhiteSpace(value);
    }
    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
        if (enabled)
        {
            if (_command.Length > 260) throw new IOException("The application path is too long for Windows startup. Move IslandDesk to a shorter path.");
            key.SetValue(_name, _command, RegistryValueKind.String);
        }
        else key.DeleteValue(_name, false);
    }
}
