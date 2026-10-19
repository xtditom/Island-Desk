using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using IslandDesk.Presentation;
using IslandDesk.Settings;
using IslandDesk.Themes;

namespace IslandDesk.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsController _settings;
    private bool _allowClose, _closing, _closed;
    private HwndSource? _source;
    internal SettingsWindow(SettingsController settings)
    {
        _settings = settings;
        InitializeComponent(); DataContext = settings;
        _settings.SettingsChanged += QuickLayout_Changed;
        UpdateQuickLayoutEditor();
        NativeWindowTheme.Track(this);
        var pages = new PersonalizationPages(this);
        AppearanceExtras.Children.Add(pages.Appearance());
        MediaSettingsTab.Content = pages.Media(); MusicColorsTab.Content = pages.Dynamic(); EqualizerTab.Content = pages.Equalizer();
        SourceInitialized += (_, _) =>
        { _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); _source?.AddHook(WindowMessage); };
        Loaded += (_, _) => FitToWorkingArea();
        Closed += (_, _) => { _closed = true; _settings.SettingsChanged -= QuickLayout_Changed; _source?.RemoveHook(WindowMessage); };
    }
    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is 0x02E0 or 0x001A or 0x007E)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitToWorkingArea));
        return 0;
    }
    private void FitToWorkingArea()
    {
        if (_closed || _source?.CompositionTarget is null || WindowState != WindowState.Normal) return;
        var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var transform = _source.CompositionTarget.TransformFromDevice;
        var origin = transform.Transform(new Point(area.Left, area.Top));
        var size = transform.Transform(new Vector(area.Width, area.Height));
        MinWidth = Math.Min(620, size.X); MinHeight = Math.Min(480, size.Y);
        MaxWidth = size.X; MaxHeight = size.Y;
        Width = Math.Min(Width, size.X); Height = Math.Min(Height, size.Y);
        Left = Math.Clamp(Left, origin.X, origin.X + size.X - Width);
        Top = Math.Clamp(Top, origin.Y, origin.Y + size.Y - Height);
    }
    private void Title_TextChanged(object sender, TextChangedEventArgs e)
    {
        var input = (TextBox)sender;
        var limited = PreferencesStore.LimitTitleInput(input.Text);
        if (input.Text == limited) return;
        input.Text = limited; input.CaretIndex = limited.Length;
    }
    internal void FocusTitle()
    {
        Categories.SelectedIndex = 0;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (_closed) return;
            TitleInput.BringIntoView(); TitleInput.Focus(); TitleInput.SelectAll();
        }));
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private string[]? _displayedQuickOrder;
    private void QuickLayout_Changed(object? sender, EventArgs args) => UpdateQuickLayoutEditor();
    private void UpdateQuickLayoutEditor()
    {
        var order = _settings.Current.QuickControlOrder;
        if (_displayedQuickOrder is not null && _displayedQuickOrder.SequenceEqual(order)) return;
        var selected = (QuickControlOrderList.SelectedItem as QuickControlInfo)?.Id;
        _displayedQuickOrder = order.ToArray();
        QuickControlOrderList.ItemsSource = order.Select(id => QuickControlLayout.Controls.First(control => control.Id == id)).ToArray();
        QuickControlOrderList.SelectedIndex = selected is null ? 0 : Math.Max(0, Array.IndexOf(order, selected));
        UpdateQuickMoveButtons();
    }
    private void QuickControlOrder_SelectionChanged(object sender, SelectionChangedEventArgs args) => UpdateQuickMoveButtons();
    private void UpdateQuickMoveButtons()
    {
        if (MoveQuickControlEarlier is null || MoveQuickControlLater is null) return;
        var index = QuickControlOrderList.SelectedIndex;
        MoveQuickControlEarlier.IsEnabled = index > 0;
        MoveQuickControlLater.IsEnabled = index >= 0 && index < QuickControlOrderList.Items.Count - 1;
    }
    private void MoveQuickControlEarlier_Click(object sender, RoutedEventArgs args) => MoveQuickControl(-1);
    private void MoveQuickControlLater_Click(object sender, RoutedEventArgs args) => MoveQuickControl(1);
    private void MoveQuickControl(int direction)
    {
        var order = _settings.Current.QuickControlOrder.ToList();
        var index = QuickControlOrderList.SelectedIndex;
        if (index < 0 || index + direction < 0 || index + direction >= order.Count) return;
        var id = order[index]; order.RemoveAt(index); order.Insert(index + direction, id);
        _settings.SetQuickControlOrder(order);
        QuickControlOrderList.ScrollIntoView(QuickControlOrderList.SelectedItem);
    }
    private void ResetQuickControlLayout_Click(object sender, RoutedEventArgs args)
    {
        if (ConfirmationWindow.Confirm(this, "Reset Quick Controls layout?", "Restore the original order on all Quick Controls pages? Your other IslandDesk preferences will stay the same.", "Reset layout"))
            _settings.ResetQuickControlLayout();
    }
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmationWindow.Confirm(this, "Restore defaults", "Restore all IslandDesk preferences and turn off Windows startup?", "Restore")) return;
        RestoreButton.IsEnabled = false;
        try { await _settings.RestoreDefaultsAsync(); }
        finally { RestoreButton.IsEnabled = true; }
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        IsEnabled = false;
        if (await _settings.FlushAsync())
        {
            _allowClose = true;
            // Flush may complete synchronously; never re-enter Close during Closing.
            await Dispatcher.InvokeAsync(() => { if (!_closed) Close(); });
        }
        else { _closing = false; IsEnabled = true; }
    }
    internal void CloseImmediately() { _allowClose = true; Close(); }
}
