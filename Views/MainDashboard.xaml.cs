using System.Windows.Controls;
using System.Windows;
using IslandDesk.Settings;
namespace IslandDesk.Views;
public partial class MainDashboard : UserControl
{
    public MainDashboard() => InitializeComponent();
    internal void ApplySettings(AppSettings settings)
    {
        Body.RowDefinitions.Clear();
        foreach (var (element, enabled, height) in new (FrameworkElement, bool, double)[]
        { (ClockSection, settings.HomeClock, 76), (MediaSection, settings.HomeMedia, settings.MediaHeight), (VolumeSection, settings.HomeVolume, 48) })
        {
            element.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            if (!enabled) continue;
            if (Body.RowDefinitions.Count > 0) Body.RowDefinitions.Add(new() { Height = new GridLength(8) });
            Grid.SetRow(element, Body.RowDefinitions.Count);
            Body.RowDefinitions.Add(new() { Height = new GridLength(height) });
        }
        Height = settings.HomeBodyHeight;
    }
}
