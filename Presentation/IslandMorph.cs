using System.Windows;
using System.Windows.Media.Animation;

namespace IslandDesk.Presentation;

/// <summary>One clock drives bounds, including the pixel-rounded origin and interrupted transitions.</summary>
internal sealed class IslandMorph(Action<Rect> applyBounds) : Animatable
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        "Progress", typeof(double), typeof(IslandMorph), new PropertyMetadata(0d, ProgressChanged));
    private Rect _from, _to;
    private bool _configuring;

    public void Retarget(IslandTransitions transitions, Rect current, Rect target, int milliseconds, Action? completed = null)
    {
        _configuring = true;
        transitions.Animate(this, ProgressProperty, 0, 0);
        _from = current; _to = target;
        _configuring = false;
        transitions.Animate(this, ProgressProperty, 1, current == target ? 0 : milliseconds, completed);
    }

    private static void ProgressChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
    {
        var morph = (IslandMorph)source;
        if (morph._configuring) return;
        var progress = (double)e.NewValue;
        morph.Apply(new(morph._from.X + (morph._to.X - morph._from.X) * progress,
            morph._from.Y + (morph._to.Y - morph._from.Y) * progress,
            morph._from.Width + (morph._to.Width - morph._from.Width) * progress,
            morph._from.Height + (morph._to.Height - morph._from.Height) * progress));
    }
    private void Apply(Rect bounds) => applyBounds(bounds);
    protected override Freezable CreateInstanceCore() => new IslandMorph(applyBounds);
}
