using System.ComponentModel;
using IslandDesk.Settings;
using IslandDesk.Themes;
using Forms = System.Windows.Forms;

namespace IslandDesk.Tray;

/// <summary>Shortcuts into the same settings controller used by the Settings window.</summary>
internal sealed class SettingsTrayMenu : IDisposable
{
    private readonly SettingsController _settings;
    private readonly ThemeService _theme;
    private readonly TrayThemeRenderer _renderer = new();
    private readonly List<(Forms.ToolStripMenuItem Item, Func<bool> Selected)> _choices = new();
    private readonly List<(Forms.ToolStripMenuItem Item, int? Rate)> _frameRates = new();
    public Forms.ContextMenuStrip Menu { get; } = new() { ShowImageMargin = false, ShowCheckMargin = true };

    public SettingsTrayMenu(SettingsController settings, ThemeService theme, Action openSettings, Action editTitle, Func<Task> exit)
    {
        _settings = settings; _theme = theme;
        Menu.Renderer = _renderer;
        Menu.Items.Add("Settings…", null, (_, _) => openSettings());
        Menu.Items.Add(new Forms.ToolStripSeparator());
        var clock = Submenu("Clock Format");
        Choice(clock, "12-hour", () => !settings.Use24Hour, () => settings.Use24Hour = false);
        Choice(clock, "24-hour", () => settings.Use24Hour, () => settings.Use24Hour = true);
        clock.DropDownItems.Add(new Forms.ToolStripSeparator());
        Choice(clock, "Show Seconds", () => settings.ShowSeconds, () => settings.ShowSeconds = !settings.ShowSeconds);
        var position = Submenu("Island Position");
        foreach (var alignment in Enum.GetValues<IslandAlignment>())
            Choice(position, alignment.ToString(), () => settings.Alignment == alignment, () => settings.Alignment = alignment);
        var themes = Submenu("Theme");
        foreach (var mode in Enum.GetValues<ThemeMode>())
            Choice(themes, mode.ToString(), () => settings.ThemeMode == mode, () => settings.ThemeMode = mode);
        var fps = Submenu("Animation FPS");
        foreach (var option in settings.FrameRateOptions)
        {
            var rate = option.Rate;
            var item = new Forms.ToolStripMenuItem(option.Label);
            item.Click += (_, _) =>
            {
                var available = settings.FrameRateOptions.First(candidate => candidate.Rate == rate);
                if (available.IsAvailable) settings.SelectedFrameRate = available;
            };
            fps.DropDownItems.Add(item); _frameRates.Add((item, rate));
        }
        var title = Submenu("Island Title");
        title.DropDownItems.Add("Edit title…", null, (_, _) => editTitle());
        Menu.Items.Add(new Forms.ToolStripSeparator());
        Menu.Items.Add("Exit", null, async (_, _) => await exit());
        Menu.Opening += Menu_Opening;
        settings.SettingsChanged += Settings_Changed;
        settings.PropertyChanged += Settings_PropertyChanged;
        theme.Changed += Theme_Changed;
        Refresh();
    }
    private Forms.ToolStripMenuItem Submenu(string title)
    { var item = new Forms.ToolStripMenuItem(title); Menu.Items.Add(item); return item; }
    private void Choice(Forms.ToolStripMenuItem parent, string label, Func<bool> selected, Action apply)
    {
        var item = new Forms.ToolStripMenuItem(label);
        item.Click += (_, _) => apply();
        parent.DropDownItems.Add(item); _choices.Add((item, selected));
    }
    private void Menu_Opening(object? sender, CancelEventArgs args) => Refresh();
    private void Settings_Changed(object? sender, EventArgs args) => Refresh();
    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(SettingsController.FrameRateOptions)) Refresh(); }
    private void Theme_Changed(object? sender, EventArgs args) => _renderer.Refresh(Menu);
    private void Refresh()
    {
        foreach (var (item, selected) in _choices) item.Checked = selected();
        foreach (var (item, rate) in _frameRates)
        {
            var option = _settings.FrameRateOptions.First(candidate => candidate.Rate == rate);
            item.Text = option.Label; item.Enabled = option.IsAvailable;
            item.Checked = _settings.Current.AnimationFrameRate == rate;
            item.ToolTipText = option.IsAvailable ? "Animation target; actual frame pacing depends on WPF and Windows."
                : "Unavailable at this monitor's current refresh rate.";
        }
        _renderer.Refresh(Menu);
    }
    public void Dispose()
    {
        _settings.SettingsChanged -= Settings_Changed;
        _settings.PropertyChanged -= Settings_PropertyChanged;
        _theme.Changed -= Theme_Changed;
        Menu.Opening -= Menu_Opening;
        Menu.Dispose();
    }
}
