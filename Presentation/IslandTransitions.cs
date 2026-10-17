using System.Windows;
using System.Windows.Media.Animation;

namespace IslandDesk.Presentation;

internal sealed record AnimationPreset(int ExpandMilliseconds, int CollapseMilliseconds, int FadeMilliseconds)
{
    public static AnimationPreset Default { get; } = new(280, 240, 150);
}

/// <summary>Retarget from the current presentation value and remove clocks on completion.</summary>
internal sealed class IslandTransitions : IDisposable
{
    private sealed record ActiveAnimation(AnimationClock Clock, Action? Completed);
    private readonly Dictionary<(DependencyObject, DependencyProperty), ActiveAnimation> _clocks = new();
    public int? DesiredFrameRate { get; private set; }
    public bool IsAnimating => _clocks.Count != 0;

    public void ConfigureFrameRate(int? rate)
    {
        if (DesiredFrameRate == rate) return;
        DesiredFrameRate = rate;
        foreach (var (key, active) in _clocks.ToArray())
        {
            var animation = (DoubleAnimation)active.Clock.Timeline.Clone();
            Timeline.SetDesiredFrameRate(animation, rate);
            var elapsed = active.Clock.CurrentTime ?? TimeSpan.Zero;
            Start(key.Item1, key.Item2, animation, active.Completed, elapsed);
            active.Clock.Controller?.Remove();
        }
    }

    public void Animate(DependencyObject target, DependencyProperty property, double to, int milliseconds,
        Action? completed = null)
    {
        var key = (target, property);
        var from = (double)target.GetValue(property);
        if (_clocks.Remove(key, out var previous)) previous.Clock.Controller?.Remove();
        if (Math.Abs(from - to) < 0.001 || milliseconds <= 0)
        {
            target.SetValue(property, to);
            Apply(target, property, null); completed?.Invoke(); return;
        }
        // Window size setters update the HWND synchronously. Keep the base at the
        // displayed size until the new clock starts, rather than briefly applying To.
        target.SetValue(property, from);
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        Timeline.SetDesiredFrameRate(animation, DesiredFrameRate);
        Start(target, property, animation, completed);
    }

    private void Start(DependencyObject target, DependencyProperty property, DoubleAnimation animation,
        Action? completed, TimeSpan? elapsed = null)
    {
        var key = (target, property);
        var clock = (AnimationClock)animation.CreateClock(true);
        _clocks[key] = new(clock, completed);
        clock.Completed += (_, _) =>
        {
            if (!_clocks.TryGetValue(key, out var current) || current.Clock != clock) return;
            _clocks.Remove(key);
            target.SetValue(property, animation.To!.Value);
            Apply(target, property, null);
            clock.Controller?.Remove();
            completed?.Invoke();
        };
        Apply(target, property, clock);
        if (elapsed is { } time) clock.Controller?.SeekAlignedToLastTick(time, TimeSeekOrigin.BeginTime);
    }

    private static void Apply(DependencyObject target, DependencyProperty property, AnimationClock? clock)
    {
        if (target is UIElement element) element.ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
        else if (target is Animatable animatable) animatable.ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
        else throw new ArgumentException("Target must support animation.", nameof(target));
    }

    public void Dispose()
    {
        foreach (var (key, active) in _clocks)
        {
            Apply(key.Item1, key.Item2, null);
            active.Clock.Controller?.Remove();
        }
        _clocks.Clear();
    }
}
