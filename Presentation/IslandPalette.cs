using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using IslandDesk.Settings;
using IslandDesk.Themes;

namespace IslandDesk.Presentation;

/// <summary>Window-scoped reusable brushes. Colors animate only when appearance changes.</summary>
internal sealed class IslandPalette : IDisposable
{
    private readonly LinearGradientBrush _gradient = new() { StartPoint = new(0, 0), EndPoint = new(1, 1) };
    private readonly SolidColorBrush _solid = new();
    private readonly SolidColorBrush _collapsed = new();
    private sealed record ActiveColor(AnimationClock Clock, Color Target, int? Fps);
    private readonly Dictionary<Animatable, ActiveColor> _clocks = new();
    public SolidColorBrush TimelineBrush { get; } = new();
    public SolidColorBrush EqualizerBrush { get; } = new();
    public SolidColorBrush VolumeFillBrush { get; } = new();
    public SolidColorBrush VolumeTrackBrush { get; } = new();
    public SolidColorBrush VolumeThumbBrush { get; } = new();
    public SolidColorBrush CardBrush { get; } = new();
    public Brush Surface { get; private set; }
    public IslandPalette()
    {
        _gradient.GradientStops.Add(new(Colors.Transparent, 0));
        _gradient.GradientStops.Add(new(Colors.Transparent, .55));
        _gradient.GradientStops.Add(new(Colors.Transparent, 1));
        Surface = _gradient;
    }
    public void ApplyBackground(AppSettings settings, Color? tint, int? fps, bool animate, bool expanded = true, ThemePalette? theme = null, Color? backgroundAccent = null)
    {
        theme ??= ThemeService.Active?.Palette ?? ThemeResources.Dark;
        var alpha = SurfaceContrast.Alpha(settings.SurfaceOpacity, theme.IsDark);
        var intensity = settings.GradientIntensity / 100;
        var dark = theme.ExpandedBase;
        var colors = new[] { Mix(dark, theme.ExpandedHigh, intensity),
            Mix(dark, theme.ExpandedMid, intensity), theme.ExpandedLow };
        var dynamicGradient = settings.DynamicBackground && expanded && settings.BackgroundStyle == IslandBackgroundStyle.CharcoalGradient;
        var backgroundColor = backgroundAccent ?? (Color)ColorConverter.ConvertFromString(settings.FallbackAccent);
        for (var i = 0; i < colors.Length; i++)
        {
            var color = colors[i];
            if (dynamicGradient && settings.BackgroundTintStrength > 0)
                color = TintSurface(color, backgroundColor, settings.BackgroundTintStrength / 100, theme.IsDark, alpha);
            color.A = alpha;
            Animate(_gradient.GradientStops[i], GradientStop.ColorProperty, color, settings, fps,
                animate && dynamicGradient, milliseconds: 300);
        }
        // Neutral, nearly opaque cards otherwise mask most of the island tint.
        var cardAlpha = SurfaceContrast.Alpha(settings.SurfaceOpacity, theme.IsDark, card: true);
        var card = dynamicGradient ? TintSurface(theme.Card, backgroundColor, settings.BackgroundTintStrength / 100 * .65, theme.IsDark, cardAlpha) : theme.Card;
        card.A = cardAlpha;
        Animate(CardBrush, SolidColorBrush.ColorProperty, card, settings, fps, animate && dynamicGradient, milliseconds: 300);
        var solid = dark; solid.A = alpha;
        Animate(_solid, SolidColorBrush.ColorProperty, solid, settings, fps,
            animate && expanded && settings.BackgroundStyle == IslandBackgroundStyle.SolidDark);
        var collapsed = tint is { } c ? Mix(theme.Collapsed, c, settings.TintStrength / 100) : theme.Collapsed; collapsed.A = alpha;
        Animate(_collapsed, SolidColorBrush.ColorProperty, collapsed, settings, fps, animate && !expanded);
        Surface = !expanded ? _collapsed : settings.BackgroundStyle == IslandBackgroundStyle.CharcoalGradient ? _gradient : _solid;
    }
    public void Apply(AppSettings settings, Color? artwork, bool musicPill, int? fps, bool animate, bool timelineVisible = false, bool expanded = true, bool volumeVisible = false)
    {
        var theme = ThemeService.Active?.Palette ?? ThemeResources.Dark;
        var neutral = theme.NeutralMusic;
        Color Custom(string value) => ArtworkAccentExtractor.EnsureContrast((Color)ColorConverter.ConvertFromString(value), theme.IsDark);
        var accent = settings.DynamicAccent && artwork is { } color ? ArtworkAccentExtractor.EnsureContrast(color, theme.IsDark) : Custom(settings.FallbackAccent);
        var timeline = settings.TimelineColor switch
        {
            TimelineColorSource.Custom => Custom(settings.TimelineCustomColor),
            TimelineColorSource.Accent when settings.DynamicAccent && settings.DynamicTimeline => accent,
            _ => neutral
        };
        var equalizer = settings.EqualizerColor switch
        {
            EqualizerColorSource.Custom => Custom(settings.EqualizerCustomColor),
            EqualizerColorSource.AlbumArtwork when settings.DynamicAccent && settings.DynamicEqualizer => accent,
            _ => neutral
        };
        Animate(TimelineBrush, SolidColorBrush.ColorProperty, timeline, settings, fps, animate && timelineVisible);
        Animate(EqualizerBrush, SolidColorBrush.ColorProperty, equalizer, settings, fps, animate && musicPill && settings.EqualizerEnabled);
        // The volume toggle is independent; reuse the already-extracted artwork color.
        var volume = artwork is { } artworkColor ? ArtworkAccentExtractor.EnsureContrast(artworkColor, theme.IsDark) : Custom(settings.FallbackAccent);
        var remaining = volume; remaining.A = 89; // 35% of the same hue; no opacity clock.
        var tokens = ThemeResources.Get(theme.IsDark);
        Color Normal(string key) => ((SolidColorBrush)tokens[key]).Color;
        Animate(VolumeFillBrush, SolidColorBrush.ColorProperty, settings.DynamicVolume ? volume : Normal("VolumeFill"), settings, fps, animate && volumeVisible);
        Animate(VolumeThumbBrush, SolidColorBrush.ColorProperty, settings.DynamicVolume ? volume : Normal("VolumeThumb"), settings, fps, animate && volumeVisible);
        Animate(VolumeTrackBrush, SolidColorBrush.ColorProperty, settings.DynamicVolume ? remaining : Normal("VolumeTrack"), settings, fps, animate && volumeVisible);
        ApplyBackground(settings, musicPill && settings.DynamicAccent && settings.PillTint ? accent : null, fps, animate, expanded, theme, artwork);
    }
    // Event-time only. Cached accents were brightened for foreground use; recover
    // their hue before tinting. Guard readable contrast, not identical luminance,
    // which previously crushed dark tints and bleached light ones back to gray.
    internal static Color TintSurface(Color original, Color accent, double strength, bool dark, byte alpha)
    {
        if (strength <= 0) return original;
        var minimum = Math.Min(accent.R, Math.Min(accent.G, accent.B));
        var range = Math.Max(accent.R, Math.Max(accent.G, accent.B)) - minimum;
        if (range >= 16) accent = Color.FromRgb((byte)((accent.R - minimum) * 255 / range),
            (byte)((accent.G - minimum) * 255 / range), (byte)((accent.B - minimum) * 255 / range));
        var tinted = Mix(original, accent, Math.Clamp(strength, 0, .30));
        var desktop = dark ? Colors.White : Colors.Black;
        double On(Color color) => Luminance(Mix(desktop, color, alpha / 255d));
        var tokens = ThemeResources.Get(dark);
        var text = new[] { "Primary", "Secondary", "Muted" }.Select(key => Luminance(((SolidColorBrush)tokens[key]).Color));
        var readableLimit = dark ? (text.Min() + .05) / 4.5 - .05 : (text.Max() + .05) * 4.5 - .05;
        // At low user opacity the desktop can already defeat AA. Do not make that
        // existing worst-case contrast worse; leave the opacity preference intact.
        var limit = dark ? Math.Max(On(original), readableLimit) : Math.Min(On(original), readableLimit);
        bool Readable(Color color) => dark ? On(color) <= limit : On(color) >= limit;
        if (Readable(tinted)) return tinted;
        var endpoint = dark ? Colors.Black : Colors.White;
        double low = 0, high = 1;
        // Fixed work and guaranteed convergence, including nearly white/black stops.
        for (var i = 0; i < 10; i++)
        {
            var middle = (low + high) / 2;
            if (Readable(Mix(tinted, endpoint, middle))) high = middle; else low = middle;
        }
        return Mix(tinted, endpoint, high);
    }
    private static double Luminance(Color color)
    {
        static double Linear(byte channel) { var value = channel / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
    internal void Animate(Animatable target, DependencyProperty property, Color to, AppSettings settings, int? fps, bool animate, int milliseconds = 180)
    {
        var from = (Color)target.GetValue(property);
        if (_clocks.TryGetValue(target, out var active) && active.Target == to && active.Fps == fps && animate) return;
        if (from == to && active is null) return;
        if (_clocks.Remove(target, out var old)) old.Clock.Controller?.Remove();
        target.SetValue(property, from);
        if (!animate || settings.AnimationStyle == TransitionStyle.Instant)
        { target.ApplyAnimationClock(property, null); target.SetValue(property, to); return; }
        var animation = new ColorAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds / settings.AnimationSpeed));
        Timeline.SetDesiredFrameRate(animation, fps);
        var clock = (AnimationClock)animation.CreateClock(true); _clocks[target] = new(clock, to, fps);
        clock.Completed += (_, _) =>
        {
            if (!_clocks.TryGetValue(target, out var current) || current.Clock != clock) return;
            _clocks.Remove(target); target.SetValue(property, to); target.ApplyAnimationClock(property, null); clock.Controller?.Remove();
        };
        target.ApplyAnimationClock(property, clock);
    }
    internal static Color Mix(Color a, Color b, double fraction) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * fraction), (byte)Math.Round(a.G + (b.G - a.G) * fraction), (byte)Math.Round(a.B + (b.B - a.B) * fraction));
    public void Dispose() { foreach (var active in _clocks.Values) active.Clock.Controller?.Remove(); _clocks.Clear(); }
}
