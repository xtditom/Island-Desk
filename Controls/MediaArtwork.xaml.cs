using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace IslandDesk.Controls;
public partial class MediaArtwork : UserControl
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(MediaArtwork));
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(nameof(Radius), typeof(CornerRadius), typeof(MediaArtwork), new PropertyMetadata(new CornerRadius(9)));
    public ImageSource? Source { get => (ImageSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public CornerRadius Radius { get => (CornerRadius)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public MediaArtwork() => InitializeComponent();
}
