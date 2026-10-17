using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using IslandDesk.Settings;
namespace IslandDesk.Controls;
public partial class Equalizer : UserControl
{
    public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(nameof(IsRunning), typeof(bool), typeof(Equalizer),
        new PropertyMetadata(false, (d, _) => ((Equalizer)d).UpdateAnimation()));
    public static readonly DependencyProperty DesiredFrameRateProperty = DependencyProperty.Register(nameof(DesiredFrameRate), typeof(int?), typeof(Equalizer),
        new PropertyMetadata(null, (d, _) => { var view = (Equalizer)d; view.Stop(); view.UpdateAnimation(); }));
    private readonly List<ScaleTransform> _bars = new();
    private readonly List<AnimationClock> _clocks = new();
    private ScaleTransform Bar1 = new();
    private bool _animating;
    private (bool Enabled, EqualizerStyle Style, double Width, double Space, int Count, double Height, double Speed)? _configuration;
    private double _speed = 1;
    public bool IsRunning { get => (bool)GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }
    public int? DesiredFrameRate { get => (int?)GetValue(DesiredFrameRateProperty); set => SetValue(DesiredFrameRateProperty, value); }
    internal bool IsAnimating => _animating;
    public Equalizer()
    {
        InitializeComponent(); Configure(new());
        Loaded += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }
    internal void Configure(AppSettings settings)
    {
        var configuration = (settings.EqualizerEnabled, settings.EqualizerStyle, settings.EqualizerBarWidth,
            settings.EqualizerBarSpacing, settings.EqualizerBarCount, settings.EqualizerBarHeight, settings.EqualizerSpeed);
        if (_configuration == configuration) return;
        _configuration = configuration; Stop(); Bars.Children.Clear(); _bars.Clear(); _speed = settings.EqualizerSpeed;
        Visibility = settings.EqualizerEnabled ? Visibility.Visible : Visibility.Collapsed;
        Width = Math.Max(20, settings.EqualizerBarCount * settings.EqualizerBarWidth + (settings.EqualizerBarCount - 1) * settings.EqualizerBarSpacing);
        for (var i = 0; i < settings.EqualizerBarCount; i++)
        {
            var transform = new ScaleTransform(1, .3 + i % 4 * .15); _bars.Add(transform);
            var bar = new Border { Width = settings.EqualizerBarWidth, Height = settings.EqualizerBarHeight,
                CornerRadius = new(settings.EqualizerBarWidth / 2), RenderTransform = transform,
                RenderTransformOrigin = new(.5, settings.EqualizerStyle switch { EqualizerStyle.Top => 0, EqualizerStyle.Middle => .5, _ => 1 }),
                Margin = new(0, 0, i == settings.EqualizerBarCount - 1 ? 0 : settings.EqualizerBarSpacing, 0) };
            bar.SetResourceReference(Border.BackgroundProperty, "MusicEqualizerBrush"); Bars.Children.Add(bar);
        }
        Bar1 = _bars[0]; UpdateAnimation();
    }
    private void UpdateAnimation()
    {
        if (!IsRunning || !IsVisible || !IsLoaded || _configuration?.Enabled != true) { Stop(); return; }
        if (_animating) return;
        _animating = true;
        for (var i = 0; i < _bars.Count; i++)
        {
            var animation = new DoubleAnimation(.22 + i % 4 * .06, .96 - i % 4 * .06,
                TimeSpan.FromMilliseconds((360 + i % 7 * 95) / _speed))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Timeline.SetDesiredFrameRate(animation, DesiredFrameRate);
            var clock = (AnimationClock)animation.CreateClock(true);
            _clocks.Add(clock);
            _bars[i].ApplyAnimationClock(ScaleTransform.ScaleYProperty, clock);
        }
    }
    private void Stop()
    {
        if (!_animating) return;
        _animating = false;
        foreach (var clock in _clocks) clock.Controller?.Remove();
        _clocks.Clear();
        foreach (var bar in _bars) bar.ApplyAnimationClock(ScaleTransform.ScaleYProperty, null);
    }
}
