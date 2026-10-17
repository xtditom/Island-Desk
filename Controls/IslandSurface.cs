using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IslandDesk.Controls;

// A fixed-layout Border whose visible chrome/clip can morph without Measure/Arrange.
// Only the surface geometry changes; child text, icons, and controls are never scaled.
public sealed class IslandSurface : Border
{
    public static readonly DependencyProperty CardBackgroundProperty = DependencyProperty.Register(nameof(CardBackground), typeof(Brush), typeof(IslandSurface), new PropertyMetadata(Brushes.Transparent));
    public Brush CardBackground { get => (Brush)GetValue(CardBackgroundProperty); set => SetValue(CardBackgroundProperty, value); }
    private readonly RectangleGeometry _roundedClip = new();
    private readonly RectangleGeometry _chrome = new();
    private Pen? _stroke;
    public Rect VisualBounds { get; private set; }

    // UIElement.Clip subproperty changes invalidate Arrange. A retained visual
    // layout clip updates the compositor/hit test without that per-frame pass.
    protected override Geometry GetLayoutClip(Size layoutSlotSize) => _roundedClip;

    public void SetVisualBounds(Rect bounds)
    {
        if (VisualBounds == bounds && _roundedClip.RadiusX == CornerRadius.TopLeft) return;
        VisualBounds = bounds;
        _roundedClip.Rect = bounds;
        _roundedClip.RadiusX = _roundedClip.RadiusY = CornerRadius.TopLeft;
        UpdateChrome();
    }

    private void UpdateChrome()
    {
        var thickness = BorderThickness.Left;
        var bounds = VisualBounds;
        if (bounds.Width > thickness && bounds.Height > thickness) bounds.Inflate(-thickness / 2, -thickness / 2);
        _chrome.Rect = bounds;
        _chrome.RadiusX = _chrome.RadiusY = Math.Max(0, CornerRadius.TopLeft - thickness / 2);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var thickness = BorderThickness.Left;
        if (_stroke?.Brush != BorderBrush || _stroke?.Thickness != thickness)
        {
            _stroke = BorderBrush is null || thickness <= 0 ? null : new Pen(BorderBrush, thickness);
            if (_stroke?.CanFreeze == true) _stroke.Freeze();
        }
        if (VisualBounds.Width <= thickness || VisualBounds.Height <= thickness) return;
        UpdateChrome();
        drawing.DrawGeometry(Background, _stroke, _chrome);
    }
}
