using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IslandDesk.Presentation;
namespace IslandDesk.Controls;
public partial class IslandHeader : UserControl
{
    public event EventHandler? MainRequested;
    public event EventHandler? RenameRequested;
    public event EventHandler? SettingsRequested;
    internal event Action<Dashboard>? NavigationRequested;
    internal event EventHandler? NavigationVisibilityChanged;
    private readonly DispatcherTimer _dismiss;
    private readonly IslandTransitions _navigationTransition = new();
    private INotifyPropertyChanged? _observed;
    private bool _navigationOpen;
    private bool _smooth = true;
    private double _speed = 1;
    internal bool IsNavigationOpen => _navigationOpen;
    public IslandHeader()
    {
        InitializeComponent();
        _dismiss = new(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(150) };
        _dismiss.Tick += (_, _) => HideNavigation(true);
        DataContextChanged += (_, _) => { ObserveSelection(); UpdatePrimaryIcon(); };
        Loaded += (_, _) => { ObserveSelection(); UpdatePrimaryIcon(); };
        Unloaded += (_, _) => { CloseNavigation(); ObserveSelection(false); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) CloseNavigation(); };
    }
    internal void ConfigureNavigation(bool quick, bool shelf)
    {
        QuickControlsItem.Visibility = quick ? Visibility.Visible : Visibility.Collapsed;
        FileShelfItem.Visibility = shelf ? Visibility.Visible : Visibility.Collapsed;
    }
    internal void ConfigureAnimation(int? frameRate, bool smooth, double speed)
    {
        _navigationTransition.ConfigureFrameRate(frameRate);
        _smooth = smooth;
        _speed = speed;
        if (!smooth && _navigationTransition.IsAnimating)
        {
            _navigationTransition.Dispose();
            NavigationOffset.X = IsNavigationOpen ? 0 : -108;
            if (!IsNavigationOpen) FinishClose();
        }
    }
    private void ObserveSelection(bool observe = true)
    {
        if (_observed is not null) _observed.PropertyChanged -= SelectionChanged;
        _observed = observe ? DataContext as INotifyPropertyChanged : null;
        if (_observed is not null) _observed.PropertyChanged += SelectionChanged;
    }
    private void SelectionChanged(object? sender, PropertyChangedEventArgs e)
    { if (!IsNavigationOpen && (e.PropertyName is nameof(IslandViewModel.Dashboard) or null or "")) UpdatePrimaryIcon(); }
    private void UpdatePrimaryIcon()
    {
        // Collapse retains a snapshot DataContext for the outgoing header fade.
        if (DataContext is not IslandViewModel viewModel) return;
        var dashboard = viewModel.Dashboard;
        PrimaryIcon.Data = (Geometry)FindResource((dashboard == Dashboard.Main ? "Home" : dashboard.ToString()) + "Icon");
        HomeTab.ToolTip = $"{(dashboard == Dashboard.Main ? "Home" : dashboard == Dashboard.QuickControls ? "Quick Controls" : dashboard == Dashboard.FileShelf ? "File Shelf" : "System")} · Choose dashboard";
    }
    internal void OpenNavigation()
    {
        if (!IsLoaded || !IsVisible) return;
        _dismiss.Stop();
        if (IsNavigationOpen) return;
        _navigationOpen = true;
        SettingsButton.Visibility = Visibility.Hidden;
        NavigationViewport.Visibility = Visibility.Visible;
        NavigationViewport.IsHitTestVisible = true;
        _navigationTransition.Animate(NavigationOffset, TranslateTransform.XProperty, 0, _smooth ? (int)Math.Round(140 / _speed) : 0);
        NavigationVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
    // Immediate lifecycle close: no clocks survive hiding, collapse, deactivation or unload.
    internal void CloseNavigation() => HideNavigation(false);
    private void HideNavigation(bool animate)
    {
        _dismiss.Stop();
        var wasOpen = _navigationOpen;
        _navigationOpen = false;
        NavigationViewport.IsHitTestVisible = false;
        UpdatePrimaryIcon();
        _navigationTransition.Animate(NavigationOffset, TranslateTransform.XProperty, -108,
            animate && _smooth ? (int)Math.Round(110 / _speed) : 0, FinishClose);
        if (wasOpen) NavigationVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
    private void FinishClose()
    {
        NavigationViewport.Visibility = Visibility.Hidden;
        SettingsButton.Visibility = Visibility.Visible;
    }
    private void Navigation_MouseEnter(object sender, MouseEventArgs e) => _dismiss.Stop();
    private void Navigation_MouseLeave(object sender, MouseEventArgs e)
    { if (IsNavigationOpen) { _dismiss.Stop(); _dismiss.Start(); } }
    private void NavigationItem_MouseEnter(object sender, MouseEventArgs e) => Navigate((Button)sender);
    private void NavigationItem_Click(object sender, RoutedEventArgs e) { e.Handled = true; Navigate((Button)sender); }
    private void Navigate(Button item)
    {
        _dismiss.Stop();
        if (!IsNavigationOpen || item.Visibility != Visibility.Visible || !Enum.TryParse<Dashboard>(item.Tag as string, out var dashboard)) return;
        if (dashboard == Dashboard.Main) MainRequested?.Invoke(this, EventArgs.Empty);
        else NavigationRequested?.Invoke(dashboard);
    }
    private void Home_MouseEnter(object sender, MouseEventArgs e) => OpenNavigation();
    private void Home_Click(object sender, RoutedEventArgs e) { e.Handled = true; OpenNavigation(); }
    private void Title_Click(object sender, RoutedEventArgs e) { CloseNavigation(); RenameRequested?.Invoke(this, EventArgs.Empty); }
    private void Settings_Click(object sender, RoutedEventArgs e) { CloseNavigation(); SettingsRequested?.Invoke(this, EventArgs.Empty); }
}
