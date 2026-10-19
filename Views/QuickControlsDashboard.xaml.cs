using System.Windows.Controls;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using IslandDesk.Audio;
using IslandDesk.Presentation;
using IslandDesk.Windowing;

namespace IslandDesk.Views;

public partial class QuickControlsDashboard : UserControl, IDisposable
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive", typeof(bool), typeof(QuickControlsDashboard), new PropertyMetadata(false));
    public static bool GetIsActive(DependencyObject target) => (bool)target.GetValue(IsActiveProperty);
    public static void SetIsActive(DependencyObject target, bool value) => target.SetValue(IsActiveProperty, value);
    internal IWindowsUtilityActions ActionsService { get; set; } = new WindowsUtilityActions();
    internal Func<bool>? ConfirmLock { get; set; }
    internal bool IsInteracting { get; private set; }
    internal event EventHandler? InteractionChanged;
    private readonly KeepAwakeRequest _awake;
    private readonly WindowsUtilityActions _shellActions = new();
    private readonly IslandTransitions _pageTransition = new();
    private ConfirmationWindow? _confirmation;
    private CoreAudioVolumeService? _microphone;
    private CancellationTokenSource? _microphoneCancellation;
    private bool _active, _disposed, _smooth = true, _microphonePending, _busy, _contextMenuOpen;
    private double _speed = 1;

    public QuickControlsDashboard()
    {
        _awake = new(Dispatcher);
        InitializeComponent();
        InitializeHardware(); InitializeReordering();
        Actions.PageChanged += (_, _) =>
        {
            UpdatePagination(); UpdateMicrophoneMonitoring(); UpdateHardwareMonitoring();
            StopPageAnimation();
            Actions.Opacity = _smooth && _active && !_dragging ? 0.65 : 1;
            PageOffset.X = _smooth && _active && !_dragging ? 8 : 0;
            var duration = _smooth && _active && !_dragging ? (int)Math.Round(130 / _speed) : 0;
            _pageTransition.Animate(Actions, OpacityProperty, 1, duration);
            _pageTransition.Animate(PageOffset, TranslateTransform.XProperty, 0, duration);
        };
        Loaded += (_, _) => { UpdatePagination(); UpdateMicrophoneMonitoring(); UpdateHardwareMonitoring(); };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { _confirmation?.Close(); StopPageAnimation(); }
            UpdateMicrophoneMonitoring();
            UpdateHardwareMonitoring();
        };
        Unloaded += (_, _) => { _confirmation?.Close(); StopMicrophoneMonitoring(); StopPageAnimation(); _radios.SetActive(false); StopBrightness(); };
    }

    internal void SetActive(bool active)
    {
        if (_active == active || _disposed) return;
        if (active) Actions.Reset();
        _active = active;
        if (!active)
        {
            _confirmation?.Close();
            StopPageAnimation();
        }
        UpdateMicrophoneMonitoring();
        UpdateHardwareMonitoring();
    }
    internal void ConfigureAnimation(int? frameRate, bool smooth, double speed)
    {
        _pageTransition.ConfigureFrameRate(frameRate);
        _smooth = smooth; _speed = speed;
        if (!smooth) StopPageAnimation();
    }
    private void StopPageAnimation()
    {
        _pageTransition.Dispose(); Actions.Opacity = 1; PageOffset.X = 0;
    }

    // Intercept before the native wheel-to-volume handler. Screen conversion
    // respects both per-monitor DPI and the existing island layout scale.
    internal bool HandleGridWheel(Point screen, int delta)
    {
        if (!_active || !IsVisible) return false;
        var point = Actions.PointFromScreen(screen);
        if (point.X < 0 || point.Y < 0 || point.X >= Actions.ActualWidth || point.Y >= Actions.ActualHeight) return false;
        if (!IsInteracting) Actions.ConsumeWheel(delta);
        return true;
    }
    private void UpdatePagination()
    {
        if (PageIndicators.Children.Count != Actions.PageCount)
        {
            PageIndicators.Children.Clear();
            for (var page = 0; page < Actions.PageCount; page++)
            {
                var indicator = new Button
                {
                    Width = 20, Height = 22, Tag = page,
                    Style = (Style)FindResource("ControlButton"),
                    ToolTip = $"Page {page + 1} of {Actions.PageCount}",
                    Content = new Ellipse { Width = 5, Height = 5 }
                };
                indicator.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, $"Quick Controls page {page + 1}");
                indicator.Click += Page_Click;
                PageIndicators.Children.Add(indicator);
            }
        }
        PreviousPage.IsEnabled = Actions.PageIndex > 0;
        NextPage.IsEnabled = Actions.PageIndex + 1 < Actions.PageCount;
        for (var page = 0; page < PageIndicators.Children.Count; page++)
        {
            var button = (Button)PageIndicators.Children[page];
            ((Ellipse)button.Content).SetResourceReference(Shape.FillProperty, page == Actions.PageIndex ? "Primary" : "Muted");
        }
    }
    private void Previous_Click(object sender, RoutedEventArgs args) { args.Handled = true; Actions.Navigate(Actions.PageIndex - 1); }
    private void Next_Click(object sender, RoutedEventArgs args) { args.Handled = true; Actions.Navigate(Actions.PageIndex + 1); }
    private void Page_Click(object sender, RoutedEventArgs args) { args.Handled = true; Actions.Navigate((int)((Button)sender).Tag); }

    private void UpdateMicrophoneMonitoring()
    {
        if (_disposed || !_active || !IsVisible || MicrophoneButton.Visibility != Visibility.Visible)
        { StopMicrophoneMonitoring(); return; }
        if (_microphone is not null) return;
        MicrophoneButton.IsEnabled = false;
        MicrophoneLabel.Text = "Microphone · Checking";
        var cancellation = new CancellationTokenSource();
        _microphoneCancellation = cancellation;
        _microphone = new CoreAudioVolumeService(flow: 1); // Default capture, multimedia role.
        _ = MonitorMicrophoneAsync(_microphone, cancellation);
    }
    private async Task MonitorMicrophoneAsync(CoreAudioVolumeService service, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            await service.RunAsync(snapshot => Dispatcher.InvokeAsync(() =>
            {
                if (_disposed || !ReferenceEquals(_microphone, service)) return;
                var pending = _microphonePending;
                _microphonePending = false;
                MicrophoneButton.IsEnabled = snapshot.IsAvailable;
                SetIsActive(MicrophoneButton, snapshot.IsAvailable && snapshot.IsMuted);
                MicrophoneSlash.Visibility = snapshot.IsAvailable && snapshot.IsMuted ? Visibility.Visible : Visibility.Hidden;
                MicrophoneLabel.Text = !snapshot.IsAvailable ? "Microphone · Unavailable" : snapshot.IsMuted ? "Microphone · Muted" : "Microphone · On";
                MicrophoneButton.ToolTip = !snapshot.IsAvailable ? "No accessible default recording device." : snapshot.IsMuted ? "Unmute the default microphone" : "Mute the default microphone";
                if (pending) Status.Text = snapshot.IsAvailable ? (snapshot.IsMuted ? "Microphone muted." : "Microphone unmuted.") : "Windows could not change the microphone. Check the default recording device.";
            }, DispatcherPriority.Background, token).Task, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private void StopMicrophoneMonitoring()
    {
        var cancellation = _microphoneCancellation;
        _microphoneCancellation = null; _microphone = null; _microphonePending = false;
        cancellation?.Cancel();
        cancellation?.Dispose();
        MicrophoneButton.IsEnabled = false;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy; Actions.IsEnabled = !busy; Pagination.IsEnabled = !busy;
        NotifyInteraction();
    }
    private void NotifyInteraction()
    {
        IsInteracting = _busy || _dragging || _contextMenuOpen || _brightnessDragging;
        InteractionChanged?.Invoke(this, EventArgs.Empty);
    }
    private async void Utility_Click(object sender, RoutedEventArgs args)
    {
        args.Handled = true; if (IsInteracting || _disposed || _suppressClick) return;
        SetBusy(true);
        try
        {
            var widget = new WindowInteropHelper(Window.GetWindow(this)).Handle;
            switch (((Button)sender).Tag as string)
            {
                case "KeepAwake":
                    Status.Text = _awake.Toggle().Message;
                    SetIsActive(AwakeButton, _awake.IsActive);
                    AwakeLabel.Text = _awake.IsActive ? "Keep PC Awake · On" : "Keep PC Awake · Off";
                    break;
                case "Microphone":
                    if (_microphone is not null)
                    {
                        _microphonePending = true; MicrophoneButton.IsEnabled = false;
                        _microphone.ToggleMute(); Status.Text = "Updating microphone…";
                    }
                    break;
                case "EmptyRecycleBin":
                    if (Confirm("Empty Recycle Bin?", "Permanently delete all files in the Recycle Bin on all drives? This deletion cannot normally be undone.", "Empty Recycle Bin") && IsVisible && _active)
                    {
                        Status.Text = "Emptying Recycle Bin…";
                        var result = await AdvancedWindowsUtilityActions.EmptyRecycleBinAsync();
                        if (!_disposed) Status.Text = result.Message;
                    }
                    break;
                case "SignOut":
                    if (Confirm("Sign out of Windows?", "Save your work before signing out. Unsaved changes may be lost. Windows will let applications respond; IslandDesk will not force them to close.", "Sign out") && IsVisible && _active)
                        Status.Text = AdvancedWindowsUtilityActions.SignOut().Message;
                    break;
                case "OnScreenKeyboard": Status.Text = AdvancedWindowsUtilityActions.OpenOnScreenKeyboard().Message; break;
                case "ShowDesktop": Status.Text = _shellActions.ShowDesktop(widget).Message; break;
                case "DisplayOff": Status.Text = "Turning off displays…"; Status.Text = (await AdvancedWindowsUtilityActions.TurnOffDisplayAsync()).Message; break;
                case "Wifi": await ToggleRadioAsync(Windows.Devices.Radios.RadioKind.WiFi); break;
                case "Bluetooth": await ToggleRadioAsync(Windows.Devices.Radios.RadioKind.Bluetooth); break;
                case "TaskView": Status.Text = _shellActions.OpenTaskView(widget).Message; break;
                case "Run": Status.Text = _shellActions.OpenRun(widget).Message; break;
                case "Brightness":
                    if (!_brightnessAvailable) { Status.Text = "Hardware brightness is unavailable on this monitor."; OpenHardwareSettings("Brightness"); }
                    break;
                default:
                    Status.Text = ((((Button)sender).Tag as string) switch
                    {
                        "Screenshot" => ActionsService.OpenScreenshot(widget),
                        "ClipboardHistory" => ActionsService.OpenClipboardHistory(widget),
                        "TaskManager" => ActionsService.OpenTaskManager(),
                        "Search" => ActionsService.OpenWindowsSearch(widget),
                        "Emoji" => ActionsService.OpenEmojiPanel(widget),
                        _ => new UtilityResult(false, "This Windows action is unavailable.")
                    }).Message;
                    break;
            }
        }
        finally { if (!_disposed) SetBusy(false); }
    }
    private bool Confirm(string title, string message, string label)
    {
        _confirmation = new(title, message, label);
        try { return _confirmation.ShowConfirmation(Window.GetWindow(this)); }
        finally { _confirmation = null; }
    }
    private void Lock_Click(object sender, RoutedEventArgs args)
    {
        args.Handled = true; if (IsInteracting || _suppressClick || _disposed) return;
        SetBusy(true);
        try
        {
            var accepted = ConfirmLock is { } confirm ? confirm() :
                Confirm("Lock device?", "Lock Windows now? You'll need to sign in again to continue.", "Lock device");
            if (accepted && IsVisible) Status.Text = ActionsService.LockDevice().Message;
        }
        finally { SetBusy(false); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _active = false;
        _confirmation?.Close(); StopMicrophoneMonitoring(); StopPageAnimation(); _awake.Dispose();
        _pageHover?.Stop(); _radios.Dispose(); StopBrightness();
    }
}
