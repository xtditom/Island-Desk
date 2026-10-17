using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using IslandDesk.Settings;

namespace IslandDesk.Presentation;

internal sealed class PreferencesStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _writes = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public PreferencesStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IslandDesk", "preferences.json");

    public static string LimitTitleInput(string? title)
    {
        var clean = new string((title ?? "").Where(c => !char.IsControl(c)).ToArray());
        var starts = StringInfo.ParseCombiningCharacters(clean);
        if (starts.Length > 20) clean = clean[..starts[20]];
        return clean;
    }
    public static string NormalizeTitle(string? title)
    { var clean = LimitTitleInput(title).Trim(); return string.IsNullOrWhiteSpace(clean) ? "IslandDesk" : clean; }

    public async Task<AppSettings> LoadAsync(int? legacyTopOffset = null)
    {
        try
        {
            await using var stream = File.OpenRead(_path);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var settings = document.RootElement.Deserialize<AppSettings>(JsonOptions) ?? new();
            // Previously the expansion preference also controlled collapse. Keep
            // that behavior until the user chooses the new independent setting.
            if (!document.RootElement.TryGetProperty(nameof(AppSettings.CollapseMode), out _))
                settings = settings with { CollapseMode = settings.ExpansionMode };
            if (!document.RootElement.TryGetProperty(nameof(AppSettings.YOffset), out _) &&
                !document.RootElement.TryGetProperty(nameof(AppSettings.ManualTopOffset), out _))
                settings = settings with { ManualTopOffset = legacyTopOffset };
            return settings.Normalize();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { return new AppSettings { ManualTopOffset = legacyTopOffset }.Normalize(); }
    }

    public async Task<bool> SaveAsync(AppSettings settings)
    {
        await _writes.WaitAsync().ConfigureAwait(false);
        var temporary = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings.Normalize(), JsonOptions)).ConfigureAwait(false);
            File.Move(temporary, _path, true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("Cannot save preferences: {0}", e.Message);
            return false;
        }
        finally { _writes.Release(); }
    }
    public async Task<string> LoadTitleAsync() => (await LoadAsync()).Title;
    public async Task<bool> SaveTitleAsync(string title) => await SaveAsync((await LoadAsync()) with { Title = title });
}
