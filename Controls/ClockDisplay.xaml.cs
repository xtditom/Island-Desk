using System.Windows;
using System.Windows.Controls;
namespace IslandDesk.Controls;
public partial class ClockDisplay : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(ClockDisplay), new PropertyMetadata(""));
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(nameof(Caption), typeof(string), typeof(ClockDisplay), new PropertyMetadata(""));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public ClockDisplay() => InitializeComponent();
}
