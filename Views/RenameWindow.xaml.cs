using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IslandDesk.Themes;
namespace IslandDesk.Views;
public partial class RenameWindow : Window
{
    private readonly Func<string, Task<bool>> _save;
    private bool _saving;
    private bool _closed;
    private bool _focusInitially = true;
    public RenameWindow(string title, Func<string, Task<bool>> save)
    {
        _save = save;
        InitializeComponent();
        NativeWindowTheme.Track(this);
        TitleInput.Text = title;
        // An explicitly requested, activatable text-entry tool; the island's styles never change.
        Loaded += (_, _) =>
        {
            if (!_focusInitially) return;
            _focusInitially = false;
            TitleInput.Focus(); TitleInput.SelectAll();
        };
        Closed += (_, _) => _closed = true;
    }
    internal void RestoreWithoutActivation()
    {
        if (_closed) return;
        ShowActivated = false;
        Show();
    }
    private void TitleInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var starts = StringInfo.ParseCombiningCharacters(TitleInput.Text);
        if (starts.Length <= 20) return;
        TitleInput.Text = TitleInput.Text[..starts[20]];
        TitleInput.CaretIndex = TitleInput.Text.Length;
    }
    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();
    private async Task SaveAsync()
    {
        if (_saving) return;
        _saving = true; SaveButton.IsEnabled = false;
        var saved = await _save(TitleInput.Text);
        if (_closed) return;
        if (saved) Close();
        else { Feedback.Text = "Couldn't save. Please try again."; _saving = false; SaveButton.IsEnabled = true; }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        if (e.Key == Key.Enter) { e.Handled = true; await SaveAsync(); }
    }
}
