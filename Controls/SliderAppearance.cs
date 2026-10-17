using System.Windows;
using System.Windows.Media;

namespace IslandDesk.Controls;

// Carry a live brush through the Slider rather than a shared template resource.
public static class SliderAppearance
{
    public static readonly DependencyProperty ThumbBrushProperty = DependencyProperty.RegisterAttached(
        "ThumbBrush", typeof(Brush), typeof(SliderAppearance), new FrameworkPropertyMetadata(null));
    public static Brush? GetThumbBrush(DependencyObject target) => (Brush?)target.GetValue(ThumbBrushProperty);
    public static void SetThumbBrush(DependencyObject target, Brush? value) => target.SetValue(ThumbBrushProperty, value);
}
