using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using IslandDesk.Media;
using IslandDesk.Settings;

namespace IslandDesk.Controls;

public partial class PlaybackTimeline : UserControl
{
    private MediaTimelineViewModel? _model;
    private readonly DispatcherTimer _labels;
    private bool _dragging, _committing, _seekingEnabled = true, _attached;
    private double _preview;
    private int? _fps;
    private long _dragGeneration;
    public PlaybackTimeline()
    {
        InitializeComponent();
        _labels = new(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _labels.Tick += (_, _) => UpdateLabels();
        DataContextChanged += (_, _) => Connect();
        Loaded += (_, _) => { Connect(); Refresh(); };
        Unloaded += (_, _) => { Detach(); _labels.Stop(); Bar.SetProgress(0); };
        IsVisibleChanged += (_, _) => Refresh();
        Bar.SizeChanged += (_, _) => Refresh();
    }
    private void Connect()
    {
        _dragging = false; if (SeekArea.IsMouseCaptured) SeekArea.ReleaseMouseCapture();
        Detach(); _model = DataContext as MediaTimelineViewModel;
        if (_model is not null) { _model.PropertyChanged += Changed; _attached = true; }
        Refresh();
    }
    private void Detach() { if (_attached && _model is not null) _model.PropertyChanged -= Changed; _attached = false; }
    private void Changed(object? sender, PropertyChangedEventArgs e) => Refresh();
    internal void Configure(AppSettings settings, int? fps)
    {
        _seekingEnabled = settings.TimelineSeeking; _fps = fps;
        Bar.Thickness = settings.TimelineThickness; Bar.RemainingOpacity = settings.TimelineRemainingOpacity;
        Timestamps.Visibility = settings.TimelineTimestamps ? Visibility.Visible : Visibility.Collapsed;
        Bar.RefreshStyle(); Refresh();
    }
    internal void SetFrameRate(int? fps) { if (_fps == fps) return; _fps = fps; Refresh(); }
    private void Refresh()
    {
        _labels.Stop();
        if (_dragging && (!IsVisible || !_seekingEnabled || _model?.Generation != _dragGeneration))
        { _dragging = false; SeekArea.ReleaseMouseCapture(); }
        SeekArea.Cursor = _seekingEnabled && _model?.CanSeek == true ? Cursors.Hand : Cursors.Arrow;
        SeekArea.ToolTip = _model?.Status ?? "Timeline unavailable";
        if (_dragging || _committing) return;
        var valid = _model?.Available == true;
        var fraction = valid ? _model!.Position / _model.Duration : 0;
        if (IsVisible && IsLoaded && valid && _model!.Rate > 0 && fraction < 1)
        {
            Bar.SetProgress(fraction, 1, (_model.Duration - _model.Position) / _model.Rate, _fps);
            if (Timestamps.Visibility == Visibility.Visible) _labels.Start();
        }
        else Bar.SetProgress(fraction);
        Bar.Opacity = valid ? 1 : .35;
        UpdateLabels();
    }
    private void UpdateLabels()
    {
        var valid = _model?.Available == true;
        Elapsed.Text = valid ? MediaTimelineViewModel.FormatTime((_dragging || _committing) ? _preview * _model!.Duration : _model!.Position) : "—";
        Total.Text = valid ? MediaTimelineViewModel.FormatTime(_model!.Duration) : "—";
        if (valid && _model!.Position >= _model.Duration) _labels.Stop();
    }
    private void Preview(MouseEventArgs e)
    {
        _preview = Math.Clamp(e.GetPosition(SeekArea).X / Math.Max(1, SeekArea.ActualWidth), 0, 1);
        Bar.SetProgress(_preview); UpdateLabels();
    }
    private void Seek_Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_seekingEnabled || _model?.CanSeek != true || _committing) return;
        _dragGeneration = _model.Generation;
        _dragging = SeekArea.CaptureMouse(); _labels.Stop(); Preview(e);
    }
    private void Seek_Move(object sender, MouseEventArgs e) { if (_dragging) { Preview(e); e.Handled = true; } }
    private async void Seek_Up(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; if (!_dragging) return;
        Preview(e); _dragging = false; _committing = true; SeekArea.ReleaseMouseCapture();
        try { if (_model is not null) await _model.SeekAsync(_preview); }
        finally { _committing = false; Refresh(); }
    }
    private void Seek_LostCapture(object sender, MouseEventArgs e) { if (_dragging) { _dragging = false; Refresh(); } }
}
