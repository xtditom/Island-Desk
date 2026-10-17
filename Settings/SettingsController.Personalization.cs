namespace IslandDesk.Settings;

internal sealed partial class SettingsController
{
    public ThemeMode ThemeMode { get => Current.ThemeMode; set => Update(Current with { ThemeMode = value }); }
    public ThemeOption[] ThemeOptions { get; } =
    [
        new(ThemeMode.System, "System", "M1,2 L15,2 15,11 1,11 Z M8,11 L8,15 M4,15 L12,15"),
        new(ThemeMode.Light, "Light", "M11,8 A3,3 0 1 1 5,8 A3,3 0 1 1 11,8 M8,0 L8,3 M8,13 L8,16 M0,8 L3,8 M13,8 L16,8 M2,2 L4,4 M12,12 L14,14 M2,14 L4,12 M12,4 L14,2"),
        new(ThemeMode.Dark, "Dark", "M11,1 A7,7 0 1 0 15,12 A7,7 0 0 1 11,1 Z")
    ];
    public Array BackgroundStyles => Enum.GetValues<IslandBackgroundStyle>();
    public Array TimelineColors => Enum.GetValues<TimelineColorSource>();
    public Array EqualizerStyles => Enum.GetValues<EqualizerStyle>();
    public Array EqualizerColors => Enum.GetValues<EqualizerColorSource>();
    public IslandBackgroundStyle BackgroundStyle { get => Current.BackgroundStyle; set => Update(Current with { BackgroundStyle = value }); }
    public bool CanUseDynamicBackground => Current.BackgroundStyle == IslandBackgroundStyle.CharcoalGradient;
    public double GradientIntensity { get => Current.GradientIntensity; set => Update(Current with { GradientIntensity = value }); }
    public double SurfaceCornerRadius { get => Current.SurfaceCornerRadius; set => Update(Current with { SurfaceCornerRadius = value }); }
    public double CardCornerRadius { get => Current.CardCornerRadius; set => Update(Current with { CardCornerRadius = value }); }
    public bool MediaArtwork { get => Current.MediaArtwork; set => Update(Current with { MediaArtwork = value }); }
    public bool MediaArtist { get => Current.MediaArtist; set => Update(Current with { MediaArtist = value }); }
    public bool MediaStatus { get => Current.MediaStatus; set => Update(Current with { MediaStatus = value }); }
    public bool MediaControls { get => Current.MediaControls; set => Update(Current with { MediaControls = value }); }
    public bool MediaTimeline { get => Current.MediaTimeline; set => Update(Current with { MediaTimeline = value }); }
    public bool TimelineTimestamps { get => Current.TimelineTimestamps; set => Update(Current with { TimelineTimestamps = value }); }
    public bool TimelineSeeking { get => Current.TimelineSeeking; set => Update(Current with { TimelineSeeking = value }); }
    public double TimelineThickness { get => Current.TimelineThickness; set => Update(Current with { TimelineThickness = value }); }
    public double TimelineRemainingOpacity { get => Current.TimelineRemainingOpacity; set => Update(Current with { TimelineRemainingOpacity = value }); }
    public TimelineColorSource TimelineColor { get => Current.TimelineColor; set => Update(Current with { TimelineColor = value }); }
    public string TimelineCustomColor { get => Current.TimelineCustomColor; set => Update(Current with { TimelineCustomColor = ValidateColor(value) }); }
    public bool DynamicAccent { get => Current.DynamicAccent; set => Update(Current with { DynamicAccent = value }); }
    public bool PillTint { get => Current.PillTint; set => Update(Current with { PillTint = value }); }
    public double TintStrength { get => Current.TintStrength; set => Update(Current with { TintStrength = value }); }
    public bool DynamicTimeline { get => Current.DynamicTimeline; set => Update(Current with { DynamicTimeline = value }); }
    public bool DynamicEqualizer { get => Current.DynamicEqualizer; set => Update(Current with { DynamicEqualizer = value }); }
    public bool DynamicVolume { get => Current.DynamicVolume; set => Update(Current with { DynamicVolume = value }); }
    public bool DynamicBackground { get => Current.DynamicBackground; set => Update(Current with { DynamicBackground = value }); }
    public double BackgroundTintStrength { get => Current.BackgroundTintStrength; set => Update(Current with { BackgroundTintStrength = value }); }
    public string FallbackAccent { get => Current.FallbackAccent; set => Update(Current with { FallbackAccent = ValidateColor(value) }); }
    public bool EqualizerEnabled { get => Current.EqualizerEnabled; set => Update(Current with { EqualizerEnabled = value }); }
    public EqualizerStyle EqualizerStyle { get => Current.EqualizerStyle; set => Update(Current with { EqualizerStyle = value }); }
    public double EqualizerBarWidth { get => Current.EqualizerBarWidth; set => Update(Current with { EqualizerBarWidth = value }); }
    public double EqualizerBarSpacing { get => Current.EqualizerBarSpacing; set => Update(Current with { EqualizerBarSpacing = value }); }
    public int EqualizerBarCount { get => Current.EqualizerBarCount; set => Update(Current with { EqualizerBarCount = value }); }
    public double EqualizerBarHeight { get => Current.EqualizerBarHeight; set => Update(Current with { EqualizerBarHeight = value }); }
    public double EqualizerSpeed { get => Current.EqualizerSpeed; set => Update(Current with { EqualizerSpeed = value }); }
    public EqualizerColorSource EqualizerColor { get => Current.EqualizerColor; set => Update(Current with { EqualizerColor = value }); }
    public string EqualizerCustomColor { get => Current.EqualizerCustomColor; set => Update(Current with { EqualizerCustomColor = ValidateColor(value) }); }
    private static string ValidateColor(string value) => AppSettings.NormalizeColor(value, "") is { Length: 7 } color
        ? color : throw new ArgumentException("Enter a color as #RRGGBB, for example #D5E0D8.");
}
