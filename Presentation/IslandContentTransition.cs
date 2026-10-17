using System.Windows;
using System.Windows.Media.Animation;

namespace IslandDesk.Presentation;

/// <summary>A reversible content handoff: outgoing content clears before incoming content draws.</summary>
internal sealed class IslandContentTransition(Action<double, double> applyOpacity) : Animatable
{
    public static readonly DependencyProperty BlendProperty = DependencyProperty.Register(
        "Blend", typeof(double), typeof(IslandContentTransition), new PropertyMetadata(0d, BlendChanged));

    private static void BlendChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
    {
        var blend = (double)e.NewValue;
        ((IslandContentTransition)source).Apply(Math.Max(0, 1 - 2 * blend), Math.Max(0, 2 * blend - 1));
    }

    private void Apply(double pill, double panel) => applyOpacity(pill, panel);
    protected override Freezable CreateInstanceCore() => new IslandContentTransition(applyOpacity);
}
