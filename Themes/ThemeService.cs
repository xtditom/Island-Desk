using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using IslandDesk.Settings;
using Windows.UI.ViewManagement;
using ThemeMode = IslandDesk.Settings.ThemeMode;

namespace IslandDesk.Themes;

internal interface IWindowsThemeSource : IDisposable
{
    bool IsDark { get; }
    event EventHandler? Changed;
}

// Documented Windows app-color notifications. No registry watcher, timer or hidden window.
internal sealed class WindowsThemeSource : IWindowsThemeSource
{
    private readonly UISettings? _settings;
    private bool _subscribed;
    internal bool HasNotifications => _subscribed;
    public event EventHandler? Changed;
    public WindowsThemeSource()
    {
        try { _settings = new(); _settings.ColorValuesChanged += ColorsChanged; _subscribed = true; }
        catch (Exception e) { System.Diagnostics.Trace.TraceWarning("Windows theme notifications unavailable: {0}", e.Message); }
    }
    public bool IsDark
    {
        get
        {
            try
            {
                if (_settings is not null)
                {
                    var foreground = _settings.GetColorValue(UIColorType.Foreground);
                    return 5 * foreground.G + 2 * foreground.R + foreground.B > 8 * 128;
                }
            }
            catch (Exception e) { System.Diagnostics.Trace.TraceWarning("Windows app colors unavailable: {0}", e.Message); }
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch (Exception) { return false; }
        }
    }
    private void ColorsChanged(UISettings sender, object args) => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose()
    {
        try { if (_subscribed && _settings is not null) _settings.ColorValuesChanged -= ColorsChanged; _subscribed = false; }
        catch (Exception e) { System.Diagnostics.Trace.TraceWarning("Windows theme unsubscribe failed: {0}", e.Message); }
    }
}

internal sealed record ThemePalette(bool IsDark, Color Collapsed, Color ExpandedBase, Color ExpandedHigh,
    Color ExpandedMid, Color ExpandedLow, Color Card, Color NeutralMusic)
{
    internal static ThemePalette Read(ResourceDictionary dictionary, bool dark) => new(dark,
        (Color)dictionary["CollapsedColor"], (Color)dictionary["ExpandedBaseColor"], (Color)dictionary["ExpandedHighColor"],
        (Color)dictionary["ExpandedMidColor"], (Color)dictionary["ExpandedLowColor"], (Color)dictionary["CardBaseColor"], (Color)dictionary["NeutralMusicColor"]);
}

internal static class ThemeResources
{
    private static ResourceDictionary? _dark, _light;
    internal static ResourceDictionary Get(bool dark)
    {
        ref var cached = ref (dark ? ref _dark : ref _light);
        if (cached is not null) return cached;
        cached = new() { Source = new Uri($"/IslandDesk;component/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative) };
        foreach (var value in cached.Values) if (value is Freezable { CanFreeze: true } resource) resource.Freeze();
        return cached;
    }
    internal static ThemePalette Dark => ThemePalette.Read(Get(true), true);
}

internal sealed class ThemeService : IDisposable
{
    private readonly SettingsController _settings;
    private readonly Dispatcher _dispatcher;
    private readonly IWindowsThemeSource _source;
    private ResourceDictionary? _applied;
    private readonly ResourceDictionary? _original;
    private readonly int _slot;
    private ThemeMode? _mode;
    private int _pending;
    private bool _disposed;
    internal static ThemeService? Active { get; private set; }
    internal bool IsDark { get; private set; }
    internal ThemePalette Palette { get; private set; } = ThemeResources.Dark;
    internal event EventHandler? Changed;
    internal ThemeService(SettingsController settings, Dispatcher dispatcher, IWindowsThemeSource? source = null)
    {
        _settings = settings; _dispatcher = dispatcher; _source = source ?? new WindowsThemeSource();
        var merged = Application.Current.Resources.MergedDictionaries;
        _slot = Enumerable.Range(0, merged.Count).FirstOrDefault(index => merged[index].Contains("CollapsedColor"), -1);
        _original = _slot < 0 ? null : merged[_slot];
        Active = this;
        _settings.SettingsChanged += PreferencesChanged;
        _source.Changed += SystemChanged;
        Refresh();
    }
    private void PreferencesChanged(object? sender, EventArgs e) { if (_mode != _settings.Current.ThemeMode) Refresh(); }
    private void SystemChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _pending, 1) != 0 || _disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            Interlocked.Exchange(ref _pending, 0);
            if (!_disposed && _settings.Current.ThemeMode == ThemeMode.System) Refresh();
        }));
    }
    // Existing HWND notifications provide a fallback if an OS omits a color event.
    internal void RefreshSystem() { if (!_disposed && _settings.Current.ThemeMode == ThemeMode.System) Refresh(); }
    private void Refresh()
    {
        _dispatcher.VerifyAccess(); _mode = _settings.Current.ThemeMode;
        var dark = _mode == ThemeMode.Dark || (_mode == ThemeMode.System && _source.IsDark);
        if (_applied is not null && IsDark == dark) return;
        IsDark = dark; var dictionary = ThemeResources.Get(dark);
        var current = Application.Current.Resources;
        var dictionaries = current.MergedDictionaries.ToList();
        var index = _applied is null ? _slot : dictionaries.IndexOf(_applied);
        if (index >= 0) dictionaries[index] = dictionary; else dictionaries.Add(dictionary);
        // Assign the application root once to invalidate existing resource expressions,
        // including those nested inside UserControls/styles. Dictionaries/brushes are reused.
        var replacement = new ResourceDictionary();
        foreach (System.Collections.DictionaryEntry entry in current) replacement.Add(entry.Key, entry.Value);
        foreach (var merged in dictionaries) replacement.MergedDictionaries.Add(merged);
        Application.Current.Resources = replacement;
        _applied = dictionary; Palette = ThemePalette.Read(dictionary, dark);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _settings.SettingsChanged -= PreferencesChanged; _source.Changed -= SystemChanged; _source.Dispose();
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_applied is not null && merged.IndexOf(_applied) is var index && index >= 0)
        { if (_original is not null) merged[index] = _original; else merged.RemoveAt(index); }
        if (Active == this) Active = null;
    }
}
