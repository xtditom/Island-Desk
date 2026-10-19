using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using IslandDesk.Windowing;
using Windows.Devices.Radios;

namespace IslandDesk.Views;

public partial class QuickControlsDashboard
{
    private RadioControlService _radios = null!;
    private ScreenBrightnessService? _brightness;
    private DispatcherTimer _brightnessDebounce = null!;
    private nint _brightnessMonitor;
    private bool _brightnessAvailable, _brightnessUpdating, _brightnessWriting, _brightnessDragging;
    private double _pendingBrightness = double.NaN;
    private void InitializeHardware()
    {
        _radios = new(Dispatcher);
        _radios.Changed += (_, _) => UpdateRadioCards();
        _brightnessDebounce = new(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        _brightnessDebounce.Tick += async (_, _) => { _brightnessDebounce.Stop(); await WriteBrightnessAsync(); };
        BrightnessSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => { _brightnessDragging = true; NotifyInteraction(); }), true);
        BrightnessSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { _brightnessDragging = false; NotifyInteraction(); }), true);
    }
    private void UpdateRadioCards()
    {
        if (_disposed) return;
        var wifi = _radios.Wifi; var bluetooth = _radios.Bluetooth;
        WifiLabel.Text = "Wi-Fi · " + wifi.Text; BluetoothLabel.Text = "Bluetooth · " + bluetooth.Text;
        SetIsActive(WifiButton, wifi.IsOn); SetIsActive(BluetoothButton, bluetooth.IsOn);
        WifiButton.ToolTip = wifi.Detail; BluetoothButton.ToolTip = bluetooth.Detail;
    }
    private async Task ToggleRadioAsync(RadioKind kind)
    {
        var state = kind == RadioKind.WiFi ? _radios.Wifi : _radios.Bluetooth;
        var id = kind == RadioKind.WiFi ? "Wifi" : "Bluetooth";
        if (!state.CanToggle)
        { Status.Text = state.Detail; OpenHardwareSettings(id); return; }
        var result = await _radios.ToggleAsync(kind);
        if (_disposed) return;
        Status.Text = result.Message;
        UpdateRadioCards();
        state = kind == RadioKind.WiFi ? _radios.Wifi : _radios.Bluetooth;
        if (!result.Success || !state.CanToggle) OfferHardwareSettings(id);
    }
    private string? _settingsFallback;
    private void OfferHardwareSettings(string id)
    {
        _settingsFallback = id;
        HardwareSettingsLink.Content = id == "Wifi" ? "Wi-Fi Settings" : id == "Bluetooth" ? "Bluetooth Settings" : "Display Settings";
        HardwareSettingsLink.Visibility = Visibility.Visible;
    }
    private void HardwareSettings_Click(object sender, RoutedEventArgs args)
    {
        args.Handled = true;
        if (_settingsFallback is { } id) OpenHardwareSettings(id);
    }
    private void OpenHardwareSettings(string id)
    {
        var page = id == "Wifi" ? "ms-settings:network-wifi" : id == "Bluetooth" ? "ms-settings:bluetooth" : "ms-settings:display";
        var result = AdvancedWindowsUtilityActions.OpenSettings(page);
        OfferHardwareSettings(id);
        if (!result.Success) Status.Text = result.Message;
    }
    internal void MonitorChanged() => UpdateHardwareMonitoring();
    private async void UpdateHardwareMonitoring()
    {
        var visible = !_disposed && _active && IsVisible;
        _radios.SetActive(visible && (WifiButton.Visibility == Visibility.Visible || BluetoothButton.Visibility == Visibility.Visible));
        if (!visible || BrightnessButton.Visibility != Visibility.Visible) { StopBrightness(); return; }
        var window = Window.GetWindow(this);
        if (window is null) return;
        var monitor = ScreenBrightnessService.MonitorForWindow(new WindowInteropHelper(window).Handle);
        if (_brightness is not null && _brightnessMonitor == monitor) return;
        StopBrightness();
        _brightnessMonitor = monitor;
        var service = _brightness = new ScreenBrightnessService();
        BrightnessLabel.Text = "Brightness · Checking";
        var status = await service.ProbeAsync(monitor);
        if (!_disposed && ReferenceEquals(_brightness, service)) ApplyBrightnessStatus(status);
    }
    private void ApplyBrightnessStatus(BrightnessStatus status)
    {
        _brightnessAvailable = status.IsAvailable;
        BrightnessSlider.Visibility = status.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
        BrightnessSlider.IsEnabled = status.IsAvailable;
        BrightnessButton.ToolTip = status.Detail;
        _brightnessUpdating = true;
        try
        {
            BrightnessLabel.Text = status.IsAvailable ? $"Brightness · {status.Percent:0}%" : "Brightness · Unavailable";
            if (status.IsAvailable) BrightnessSlider.Value = status.Percent;
        }
        finally { _brightnessUpdating = false; }
    }
    private void Brightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        if (_brightnessUpdating || !_brightnessAvailable || _brightness is null || _disposed) return;
        BrightnessLabel.Text = $"Brightness · {args.NewValue:0}%";
        _pendingBrightness = args.NewValue;
        _brightnessDebounce.Stop(); _brightnessDebounce.Start();
    }
    private async Task WriteBrightnessAsync()
    {
        if (_brightnessWriting || _brightness is not { } service || double.IsNaN(_pendingBrightness)) return;
        var value = _pendingBrightness; _pendingBrightness = double.NaN; _brightnessWriting = true;
        var status = await service.SetAsync(value);
        if (_disposed || !ReferenceEquals(service, _brightness)) return;
        _brightnessWriting = false;
        if (double.IsNaN(_pendingBrightness) || !status.IsAvailable) ApplyBrightnessStatus(status);
        if (!status.IsAvailable) { Status.Text = status.Detail; _pendingBrightness = double.NaN; OfferHardwareSettings("Brightness"); }
        else if (!double.IsNaN(_pendingBrightness)) { _brightnessDebounce.Stop(); _brightnessDebounce.Start(); }
    }
    private void StopBrightness()
    {
        _brightnessDebounce.Stop(); _pendingBrightness = double.NaN;
        var service = _brightness; _brightness = null; _brightnessMonitor = 0;
        _brightnessAvailable = false; _brightnessWriting = false;
        service?.Dispose();
        BrightnessSlider.IsEnabled = false;
    }
}
