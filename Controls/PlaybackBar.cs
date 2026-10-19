using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace IslandDesk.Controls;

// Retained rounded geometry: progress changes compositor resources, never layout.
public sealed class PlaybackBar : FrameworkElement
{
    // Bound slow-motion lag to a tiny fraction of a physical pixel, retaining
    // the same rounded geometry/antialiasing rather than snapping to whole pixels.
    private const double DevicePixelStep = 1d / 32;
    private readonly DispatcherTimer _progress;
    private long _started;
    private double _seconds;
    private Rect _start;
    public PlaybackBar()
    {
        SetResourceReference(ForegroundProperty, "MusicTimelineBrush");
        _progress = new(DispatcherPriority.Render, Dispatcher);
        _progress.Tick += (_, _) => AdvanceProgress();
        Unloaded += (_, _) => StopAnimation();
        IsVisibleChanged += (_, _) => { if (!IsVisible) StopAnimation(); };
    }
    private readonly RectangleGeometry _track = new(), _fill = new();
    private AnimationClock? _clock;
    private Rect _target;
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(PlaybackBar), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.SubPropertiesDoNotAffectRender));
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    internal double Thickness { get; set; } = 3;
    internal double RemainingOpacity { get; set; } = .22;
    internal bool IsAnimating => _progress.IsEnabled || _fill.HasAnimatedProperties;
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
        if (rect == to) return;
        _target = to;
        var distance = Math.Abs(to.Width - rect.Width) * DeviceScaleX();
        var interval = DevicePixelStep * seconds / distance;
        // Keep the selected clock pacing whenever a frame can make a meaningful
        // spatial change. Only slow, subpixel progress uses demand-driven wakes.
        if (interval > 1d / (fps ?? 60))
        {
            _start = rect; _seconds = seconds; _started = Stopwatch.GetTimestamp();
            // DispatcherTimer accepts at most Int32.MaxValue milliseconds.
            _progress.Interval = TimeSpan.FromSeconds(Math.Min(Math.Min(interval, seconds), int.MaxValue / 1000d));
            _progress.Start();
            return;
        }
        var animation = new RectAnimation(rect, to, TimeSpan.FromSeconds(seconds)) { FillBehavior = FillBehavior.Stop };
        Timeline.SetDesiredFrameRate(animation, fps);
        _clock = (AnimationClock)animation.CreateClock(true);
        _clock.Completed += Animation_Completed;
        _fill.ApplyAnimationClock(RectangleGeometry.RectProperty, _clock);
    }
    private double DeviceScaleX()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.RootVisual is not Visual root) return VisualTreeHelper.GetDpi(this).DpiScaleX;
        var transform = TransformToAncestor(root);
        var origin = transform.Transform(new Point());
        var point = transform.Transform(new Point(1, 0));
        return source.CompositionTarget.TransformToDevice.Transform(point - origin).Length;
    }
    private void AdvanceProgress()
    {
        var elapsed = Stopwatch.GetElapsedTime(_started).TotalSeconds;
        if (elapsed >= _seconds)
        {
            var target = _target; StopAnimation(); _fill.Rect = target;
            return;
        }
        // Project from monotonic time so delayed dispatcher wakes never accumulate drift.
        _fill.Rect = new Rect(_start.X, _start.Y,
            _start.Width + (_target.Width - _start.Width) * elapsed / _seconds, _start.Height);
        var remaining = TimeSpan.FromSeconds(_seconds - elapsed);
        if (remaining < _progress.Interval) _progress.Interval = remaining;
    }
    private void Animation_Completed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _clock)) return;
        var target = _target;
        StopAnimation(); _fill.Rect = target;
    }
    private void StopAnimation()
    {
        _progress.Stop();
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
