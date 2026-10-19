using System.Windows;
using IslandDesk.Themes;
using IslandDesk.Windowing;

namespace IslandDesk.Views;

public partial class ConfirmationWindow : Window
{
    internal ConfirmationWindow(string title, string message, string confirm = "Confirm")
    {
        InitializeComponent(); Title = Heading.Text = title; Message.Text = message; ConfirmButton.Content = confirm;
        NativeWindowTheme.Track(this);
        Loaded += (_, _) => { SecondaryWindowActivation.Activate(this); CancelButton.Focus(); };
    }
    internal bool ShowConfirmation(Window? parent)
    {
        // Never let closing a modal reactivate the NOACTIVATE island. A normal
        // Settings/other secondary parent gets standard WPF ownership instead.
        if (parent is { IsVisible: true, ShowActivated: true } && parent is not MainWindow)
        { Owner = parent; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return ShowDialog() == true;
    }
    internal static bool Confirm(Window? owner, string title, string message, string confirm)
    {
        var dialog = new ConfirmationWindow(title, message, confirm);
        return dialog.ShowConfirmation(owner);
    }
    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
