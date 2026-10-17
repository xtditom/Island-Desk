using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IslandDesk.Controls;

// Retained rounded geometry: animation changes compositor resources, never layout.
public sealed class PlaybackBar : FrameworkElement
{
    public PlaybackBar() => SetResourceReference(ForegroundProperty, "MusicTimelineBrush");
    private readonly RectangleGeometry _track = new(), _fill = new();
    private AnimationClock? _clock;
    private Rect _target;
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(PlaybackBar), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.SubPropertiesDoNotAffectRender));
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    internal double Thickness { get; set; } = 3;
    internal double RemainingOpacity { get; set; } = .22;
    internal bool IsAnimating => _fill.HasAnimatedProperties;
    protected override void OnRender(DrawingContext context)
    {
        var bounds = new Rect(0, Math.Max(0, (ActualHeight - Thickness) / 2), ActualWidth, Thickness);
        _track.Rect = bounds; _track.RadiusX = _track.RadiusY = _fill.RadiusX = _fill.RadiusY = Thickness / 2;
        context.PushOpacity(RemainingOpacity); context.DrawGeometry(Foreground, null, _track); context.Pop();
        context.DrawGeometry(Foreground, null, _fill);
    }
    internal void SetProgress(double fraction, double? end = null, double seconds = 0, int? fps = null)
    {
        StopAnimation();
        var rect = new Rect(0, Math.Max(0, (ActualHeight - Thickness) / 2), ActualWidth * Math.Clamp(fraction, 0, 1), Thickness);
        _fill.Rect = rect;
        if (end is null || seconds <= 0 || ActualWidth <= 0) return;
        var to = new Rect(rect.X, rect.Y, ActualWidth * Math.Clamp(end.Value, 0, 1), rect.Height);
        var animation = new RectAnimation(rect, to, TimeSpan.FromSeconds(seconds)) { FillBehavior = FillBehavior.Stop };
        Timeline.SetDesiredFrameRate(animation, fps);
        _target = to;
        _clock = (AnimationClock)animation.CreateClock(true);
        _clock.Completed += Animation_Completed;
        _fill.ApplyAnimationClock(RectangleGeometry.RectProperty, _clock);
    }
    private void Animation_Completed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _clock)) return;
        var target = _target;
        StopAnimation(); _fill.Rect = target;
    }
    private void StopAnimation()
    {
        var clock = _clock; _clock = null;
        if (clock is not null)
        {
            clock.Completed -= Animation_Completed;
            // Detaching a property animation alone leaves its clock active
            // until collection. Stop its timing work without waiting for GC.
            clock.Controller?.Remove();
        }
        _fill.ApplyAnimationClock(RectangleGeometry.RectProperty, null);
    }
    internal void RefreshStyle() => InvalidateVisual(); // Settings/size events only, never per animation tick.
}
