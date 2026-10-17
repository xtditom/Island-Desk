using System.Windows;
using System.Windows.Media;

namespace IslandDesk.Controls;

// Eight retained, disjoint rounded bands approximate a small ambient shadow.
// No bitmap effects, offscreen blur, animation clocks, or per-frame layout.
public sealed class IslandShadow : FrameworkElement
{
    internal const double MarginSize = 8;
    private readonly RectangleGeometry[] _edges = Enumerable.Range(0, 9).Select(_ => new RectangleGeometry()).ToArray();
    private readonly GeometryGroup[] _bands = new GeometryGroup[8];
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Color), typeof(IslandShadow),
        new FrameworkPropertyMetadata(Colors.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public IslandShadow()
    {
        IsHitTestVisible = false;
        for (var i = 0; i < _bands.Length; i++)
        {
            _bands[i] = new() { FillRule = FillRule.EvenOdd };
            _bands[i].Children.Add(_edges[i]); _bands[i].Children.Add(_edges[i + 1]);
        }
    }
    internal void SetVisualBounds(Rect bounds, double radius)
    {
        radius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2);
        for (var i = 0; i < _edges.Length; i++)
        {
            var edge = bounds; edge.Inflate(i, i);
            _edges[i].Rect = edge; _edges[i].RadiusX = _edges[i].RadiusY = radius + i;
        }
    }
    protected override void OnRender(DrawingContext drawing)
    {
        for (var i = 0; i < _bands.Length; i++)
        {
            var color = Color; color.A = (byte)Math.Round(color.A * Math.Pow(1 - i / 8d, 2));
            var brush = new SolidColorBrush(color); brush.Freeze();
            drawing.DrawGeometry(brush, null, _bands[i]);
        }
    }
}
