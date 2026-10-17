using System.Windows;
using IslandDesk.Themes;

namespace IslandDesk.Views;

public partial class ConfirmationWindow : Window
{
    internal ConfirmationWindow(string title, string message, string confirm = "Confirm")
    {
        InitializeComponent(); Title = Heading.Text = title; Message.Text = message; ConfirmButton.Content = confirm;
        NativeWindowTheme.Track(this);
        Loaded += (_, _) => CancelButton.Focus();
    }
    internal static bool Confirm(Window? owner, string title, string message, string confirm)
    {
        var dialog = new ConfirmationWindow(title, message, confirm);
        if (owner is { IsVisible: true }) { dialog.Owner = owner; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }
    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
