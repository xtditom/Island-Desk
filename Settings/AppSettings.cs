using IslandDesk.Presentation;
using System.Text.Json.Serialization;

namespace IslandDesk.Settings;

internal enum ExpansionMode { Hover, Click, Both }
internal enum TransitionStyle { Smooth, Instant }
internal enum IslandAlignment { Left, Center, Right }
internal enum TimelineColorSource { Accent, Neutral, Custom }
internal enum IslandBackgroundStyle { CharcoalGradient, SolidDark }
internal enum EqualizerStyle { Bottom, Middle, Top }
internal enum EqualizerColorSource { AlbumArtwork, Custom, Neutral }
internal enum ThemeMode { System, Light, Dark }

internal sealed record AppSettings
{
    public int Version { get; init; } = 1;
    public string Title { get; init; } = "IslandDesk";
    public bool LaunchOnStartup { get; init; }
    public ExpansionMode ExpansionMode { get; init; } = ExpansionMode.Click;
    public ExpansionMode CollapseMode { get; init; } = ExpansionMode.Click;
    public int CollapseDelayMilliseconds { get; init; } = 250;
    public bool HideInFullscreen { get; init; } = true;
    public bool AlwaysOnTop { get; init; } = true;
    public double SurfaceOpacity { get; init; } = 80;
    public ThemeMode ThemeMode { get; init; } = ThemeMode.System;
    public IslandBackgroundStyle BackgroundStyle { get; init; } = IslandBackgroundStyle.CharcoalGradient;
    public double GradientIntensity { get; init; } = 60;
    public double SurfaceCornerRadius { get; init; } = 18;
    public double CardCornerRadius { get; init; } = 12;
    public double AnimationSpeed { get; init; } = 1;
    public TransitionStyle AnimationStyle { get; init; } = TransitionStyle.Smooth;
    public int? AnimationFrameRate { get; init; } // null = Max, using the current display mode.
    public bool Use24Hour { get; init; } = true;
    public bool ShowSeconds { get; init; }
    public IslandAlignment Alignment { get; init; } = IslandAlignment.Center;
    public bool UseWorkingArea { get; init; } = true;
    public int XOffset { get; init; }
    public int YOffset { get; init; }
    // Read previous settings; converted once using the widget's actual monitor.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ManualTopOffset { get; init; }
    public bool HomeClock { get; init; } = true;
    public bool HomeMedia { get; init; } = true;
    public bool HomeVolume { get; init; } = true;
    public bool Cpu { get; init; } = true;
    public bool Gpu { get; init; } = true;
    public bool Ram { get; init; } = true;
    public bool Disk { get; init; } = true;
    public bool Network { get; init; } = true;
    public bool Battery { get; init; }
    public bool MediaArtwork { get; init; } = true;
    public bool MediaArtist { get; init; } = true;
    public bool MediaStatus { get; init; } = true;
    public bool MediaControls { get; init; } = true;
    public bool MediaTimeline { get; init; } = true;
    public bool TimelineTimestamps { get; init; } = true;
    public bool TimelineSeeking { get; init; } = true;
    public double TimelineThickness { get; init; } = 3;
    public double TimelineRemainingOpacity { get; init; } = .22;
    public TimelineColorSource TimelineColor { get; init; } = TimelineColorSource.Accent;
    public string TimelineCustomColor { get; init; } = "#D5E0D8";
    public bool DynamicAccent { get; init; } = true;
    public bool PillTint { get; init; } = true;
    public double TintStrength { get; init; } = 4;
    public bool DynamicTimeline { get; init; } = true;
    public bool DynamicEqualizer { get; init; } = true;
    public bool DynamicVolume { get; init; } = true;
    public bool DynamicBackground { get; init; }
    public double BackgroundTintStrength { get; init; } = 8;
    public string FallbackAccent { get; init; } = "#D5E0D8";
    public bool EqualizerEnabled { get; init; } = true;
    public EqualizerStyle EqualizerStyle { get; init; } = EqualizerStyle.Bottom;
    public double EqualizerBarWidth { get; init; } = 2;
    public double EqualizerBarSpacing { get; init; } = 2;
    public int EqualizerBarCount { get; init; } = 4;
    public double EqualizerBarHeight { get; init; } = 14;
    public double EqualizerSpeed { get; init; } = 1;
    public EqualizerColorSource EqualizerColor { get; init; } = EqualizerColorSource.AlbumArtwork;
    public string EqualizerCustomColor { get; init; } = "#D5E0D8";

    public AppSettings Normalize() => this with
    {
        Version = 1, Title = PreferencesStore.NormalizeTitle(Title),
        ExpansionMode = NormalizeInteractionMode(ExpansionMode),
        CollapseMode = NormalizeInteractionMode(CollapseMode),
        CollapseDelayMilliseconds = Math.Clamp(CollapseDelayMilliseconds, 150, 600),
        AnimationStyle = Enum.IsDefined(AnimationStyle) ? AnimationStyle : TransitionStyle.Smooth,
        Alignment = Enum.IsDefined(Alignment) ? Alignment : IslandAlignment.Center,
        SurfaceOpacity = double.IsFinite(SurfaceOpacity) ? Math.Clamp(SurfaceOpacity, 60, 100) : 80,
        ThemeMode = Enum.IsDefined(ThemeMode) ? ThemeMode : ThemeMode.System,
        BackgroundStyle = Enum.IsDefined(BackgroundStyle) ? BackgroundStyle : IslandBackgroundStyle.CharcoalGradient,
        GradientIntensity = double.IsFinite(GradientIntensity) ? Math.Clamp(GradientIntensity, 0, 100) : 60,
        SurfaceCornerRadius = double.IsFinite(SurfaceCornerRadius) ? Math.Clamp(SurfaceCornerRadius, 12, 22) : 18,
        CardCornerRadius = double.IsFinite(CardCornerRadius) ? Math.Clamp(CardCornerRadius, 6, 16) : 12,
        AnimationSpeed = double.IsFinite(AnimationSpeed) ? Math.Clamp(AnimationSpeed, .5, 2) : 1,
        AnimationFrameRate = AnimationFrameRate is null or 30 or 60 or 90 or 120 or 180 or 270 or 360 ? AnimationFrameRate : null,
        XOffset = Math.Clamp(XOffset, -10000, 10000), YOffset = Math.Clamp(YOffset, -10000, 10000),
        ManualTopOffset = ManualTopOffset is { } offset ? Math.Clamp(offset, 0, 10000) : null,
        TimelineThickness = double.IsFinite(TimelineThickness) ? Math.Clamp(TimelineThickness, 2, 6) : 3,
        TimelineRemainingOpacity = double.IsFinite(TimelineRemainingOpacity) ? Math.Clamp(TimelineRemainingOpacity, .08, .5) : .22,
        TimelineColor = Enum.IsDefined(TimelineColor) ? TimelineColor : TimelineColorSource.Accent,
        TimelineCustomColor = NormalizeColor(TimelineCustomColor, "#D5E0D8"),
        TintStrength = double.IsFinite(TintStrength) ? Math.Clamp(TintStrength, 0, 10) : 4,
        BackgroundTintStrength = double.IsFinite(BackgroundTintStrength) ? Math.Clamp(BackgroundTintStrength, 0, 30) : 8,
        FallbackAccent = NormalizeColor(FallbackAccent, "#D5E0D8"),
        EqualizerStyle = Enum.IsDefined(EqualizerStyle) ? EqualizerStyle : EqualizerStyle.Bottom,
        EqualizerColor = Enum.IsDefined(EqualizerColor) ? EqualizerColor : EqualizerColorSource.AlbumArtwork,
        EqualizerBarWidth = double.IsFinite(EqualizerBarWidth) ? Math.Clamp(EqualizerBarWidth, 1, 4) : 2,
        EqualizerBarSpacing = double.IsFinite(EqualizerBarSpacing) ? Math.Clamp(EqualizerBarSpacing, 1, 4) : 2,
        EqualizerBarCount = Math.Clamp(EqualizerBarCount, 2, 12),
        EqualizerBarHeight = double.IsFinite(EqualizerBarHeight) ? Math.Clamp(EqualizerBarHeight, 6, 18) : 14,
        EqualizerSpeed = double.IsFinite(EqualizerSpeed) ? Math.Clamp(EqualizerSpeed, .5, 2) : 1,
        EqualizerCustomColor = NormalizeColor(EqualizerCustomColor, "#D5E0D8")
    };
    private static ExpansionMode NormalizeInteractionMode(ExpansionMode mode) =>
        mode is ExpansionMode.Hover or ExpansionMode.Click ? mode : ExpansionMode.Click;
    internal static string NormalizeColor(string? value, string fallback) => value is { Length: 7 } && value[0] == '#' &&
        value.AsSpan(1).ToString().All(Uri.IsHexDigit) ? value.ToUpperInvariant() : fallback;
    [JsonIgnore] public double MediaHeight => 2 + 88 + (MediaTimeline ? (TimelineTimestamps ? 40 : 24) : 0);
    [JsonIgnore] public double HomeBodyHeight => (HomeClock ? 76 : 0) + (HomeMedia ? MediaHeight : 0) + (HomeVolume ? 48 : 0)
        + Math.Max(0, (HomeClock ? 1 : 0) + (HomeMedia ? 1 : 0) + (HomeVolume ? 1 : 0) - 1) * 8;
    [JsonIgnore] public int MetricCount => (Cpu ? 1 : 0) + (Gpu ? 1 : 0) + (Ram ? 1 : 0) + (Disk ? 1 : 0);
    [JsonIgnore] public double SystemBodyHeight => 44 + (Battery ? 96 : 0) + (MetricCount > 0 ? 8 + Math.Ceiling(MetricCount / 2.0) * 96 - 8 : 0) + (Network ? 80 : 0);
    [JsonIgnore] public double HomeHeight => 76 + HomeBodyHeight;
    [JsonIgnore] public double SystemHeight => 76 + SystemBodyHeight;
}
