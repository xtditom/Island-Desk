using System.Windows.Controls;
using System.Windows;
using IslandDesk.Settings;
namespace IslandDesk.Views;
public partial class SystemDashboard : UserControl
{
    public SystemDashboard() => InitializeComponent();
    internal void ApplySettings(AppSettings settings)
    {
        var cards = new[] { (CpuCard, settings.Cpu), (GpuCard, settings.Gpu), (RamCard, settings.Ram), (DiskCard, settings.Disk) };
        var enabled = cards.Where(card => card.Item2).Select(card => card.Item1).ToArray();
        foreach (var (card, visible) in cards) card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        Metrics.RowDefinitions.Clear();
        for (var row = 0; row < Math.Ceiling(enabled.Length / 2.0); row++)
        {
            if (row > 0) Metrics.RowDefinitions.Add(new() { Height = new GridLength(8) });
            Metrics.RowDefinitions.Add(new() { Height = new GridLength(88) });
        }
        for (var i = 0; i < enabled.Length; i++)
        {
            Grid.SetRow(enabled[i], (i / 2) * 2); Grid.SetColumn(enabled[i], (i % 2) * 2);
            Grid.SetColumnSpan(enabled[i], i == enabled.Length - 1 && enabled.Length % 2 == 1 ? 3 : 1);
        }
        Metrics.Visibility = enabled.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        BatteryCard.Visibility = settings.Battery ? Visibility.Visible : Visibility.Collapsed;
        Body.RowDefinitions[1].Height = new GridLength(settings.Battery ? 8 : 0);
        Body.RowDefinitions[2].Height = new GridLength(settings.Battery ? 88 : 0);
        Body.RowDefinitions[3].Height = new GridLength(enabled.Length > 0 ? 8 : 0);
        Body.RowDefinitions[4].Height = new GridLength(enabled.Length > 0 ? Math.Ceiling(enabled.Length / 2.0) * 96 - 8 : 0);
        Body.RowDefinitions[5].Height = new GridLength(settings.Network ? 8 : 0);
        Body.RowDefinitions[6].Height = new GridLength(settings.Network ? 72 : 0);
        NetworkCard.Visibility = settings.Network ? Visibility.Visible : Visibility.Collapsed;
        Height = settings.SystemBodyHeight;
    }
}
