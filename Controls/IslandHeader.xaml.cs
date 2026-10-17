using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace IslandDesk.Controls;
public partial class IslandHeader : UserControl
{
    public event EventHandler? SystemRequested;
    public event EventHandler? MainRequested;
    public event EventHandler? RenameRequested;
    public event EventHandler? SettingsRequested;
    public IslandHeader()
    {
        InitializeComponent();
    }
    private void System_MouseEnter(object sender, MouseEventArgs e) => SystemRequested?.Invoke(this, EventArgs.Empty);
    private void Home_MouseEnter(object sender, MouseEventArgs e) => MainRequested?.Invoke(this, EventArgs.Empty);
    private void System_Click(object sender, RoutedEventArgs e) => SystemRequested?.Invoke(this, EventArgs.Empty);
    private void Home_Click(object sender, RoutedEventArgs e) => MainRequested?.Invoke(this, EventArgs.Empty);
    private void Title_Click(object sender, RoutedEventArgs e) => RenameRequested?.Invoke(this, EventArgs.Empty);
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
}
