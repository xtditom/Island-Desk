using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using IslandDesk.Monitoring;
using IslandDesk.Interaction;
using IslandDesk.Audio;
using IslandDesk.Media;
using IslandDesk.Presentation;
using IslandDesk.Views;
using IslandDesk.Windowing;
using IslandDesk.Settings;
using System.Globalization;
using IslandDesk.Themes;

namespace IslandDesk;

public partial class MainWindow : Window
{
    private const double CollapsedWidth = 106;
    private const double MusicWidth = 260;
    private const double CollapsedHeight = 36;
    private const double ExpandedWidth = DashboardLayout.Width;
    private const int TopMarginPixels = 8;
    private const uint MonitorDefaultToNearest = 2;
    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int WmDpiChanged = 0x02E0;
    private const int WmMouseActivate = 0x0021;
    private const int WmMouseWheel = 0x020A;
    private const int WmTimeChange = 0x001E;
    private const int MaNoActivate = 3;
    private const int GwlExStyle = -20;
    private const uint GwOwner = 4;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly nint HwndTopmost = new(-1);

    private readonly IslandInteractionController _interaction;
    private readonly SettingsController _settings;
    private readonly bool _ownsSettings, _enableFullscreenDetection;
    private readonly SystemUsageViewModel _usageViewModel = new();
    private readonly MediaViewModel _mediaViewModel;
    private readonly VolumeViewModel _volumeViewModel;
    private readonly ClockViewModel _clock;
    private readonly IslandViewModel _viewModel;
    private readonly IslandServices _services;
    private readonly IslandTransitions _transitions = new();
    private readonly IslandPalette _palette = new();
    private readonly ThemeService _theme;
    internal ThemeService Theme => _theme;
    private readonly IslandMorph _morph;
    private readonly IslandContentTransition _contentTransition;
    private readonly AnimationPreset _preset = AnimationPreset.Default;
    private SettingsWindow? _settingsWindow;
    private RenameWindow? _renameWindow;
    private HwndSource? _source;
    private FullscreenObserver? _fullscreenObserver;
    private bool _hiddenForFullscreen, _restoreAfterFullscreen, _restoreRenameAfterFullscreen;
    private bool _isClosed;
    private double _horizontalCenter;
    private double _topPosition = 8;
    private double _expandedScale = 1;
    private double _workLeft, _workRight = SystemParameters.PrimaryScreenWidth, _edgeMargin = 8;
    private double _monitorLeft, _monitorRight = SystemParameters.PrimaryScreenWidth, _offsetX;
    private nint _activeMonitor;
    private bool _placementPending;
    private bool _changingHostBounds;
    private Rect _visualBounds = new(0, 8, CollapsedWidth, CollapsedHeight);
    private double _clockPillWidth = CollapsedWidth;
    private AppSettings? _appliedSettings;
    private bool _isExpanded;
    private bool _suppressMusicMorphHover;
    private NativePoint _musicMorphPointer;
    private (bool Expanded, bool Music, Dashboard Dashboard, bool Visible, double Scale)? _presentationState;

    public MainWindow() : this(enableFullscreenDetection: true) { }

    internal MainWindow(bool enableFullscreenDetection, SettingsController? settings = null, IWindowsThemeSource? themeSource = null)
    {
        _ownsSettings = settings is null;
        _settings = settings ?? new(Dispatcher);
        _theme = new(_settings, Dispatcher, themeSource);
        _enableFullscreenDetection = enableFullscreenDetection;
        _morph = new(ApplyVisualBounds);
        _contentTransition = new(ApplyContentOpacity);
        _mediaViewModel = new(action => _services?.ExecuteMediaAsync(action) ?? Task.FromResult(false),
            request => _services?.SeekMediaAsync(request) ?? Task.FromResult(false));
        _volumeViewModel = new(value => _services?.SetVolume(value), muted => _services?.SetMute(muted));
        _clock = new(Dispatcher);
        _viewModel = new(_usageViewModel, _mediaViewModel, _volumeViewModel, _clock);
        _services = new(Dispatcher, _usageViewModel, _mediaViewModel, _volumeViewModel, _viewModel.Battery);
        InitializeComponent();
        if (ApplicationIconAssets.WindowIcon is { } icon) Icon = icon;
        IslandSurface.CardBackground = _palette.CardBrush;
        _theme.Changed += Theme_Changed;
        Resources["MusicTimelineBrush"] = _palette.TimelineBrush;
        Resources["MusicEqualizerBrush"] = _palette.EqualizerBrush;
        Resources["VolumeFill"] = _palette.VolumeFillBrush;
        Resources["VolumeTrack"] = _palette.VolumeTrackBrush;
        Resources["VolumeThumb"] = _palette.VolumeThumbBrush;
        DataContext = _viewModel;
        MainDashboard.DataContext = null;
        SystemDashboard.DataContext = null;
        _interaction = new(Dispatcher, () => _isExpanded, SetExpanded, new IslandInteractionOptions());
        IslandSurface.MouseEnter += Surface_MouseEnter;
        IslandSurface.MouseMove += Surface_MouseMove;
        IslandSurface.MouseLeave += (_, _) => { _suppressMusicMorphHover = false; _interaction.PointerLeft(); };
        IslandSurface.GotMouseCapture += (_, _) => UpdateInteractionCapture();
        IslandSurface.LostMouseCapture += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input,
            new Action(UpdateInteractionCapture));
        Header.SystemRequested += (_, _) => SelectDashboard(Dashboard.System);
        Header.MainRequested += (_, _) => SelectDashboard(Dashboard.Main);
        Header.RenameRequested += (_, _) => RenameTitle();
        Header.SettingsRequested += (_, _) => OpenSettings();
        _settings.SettingsChanged += Settings_Changed;
        _mediaViewModel.PropertyChanged += Media_PropertyChanged;
        SourceInitialized += (_, _) => ConfigureNativeWindow();
        Loaded += (_, _) =>
        {
            UpdatePlacement();
            RaiseWithoutActivation();
            UpdateInteractionState();
            UpdatePresentation(false);
            UpdateServices();
            ApplySettings();
        };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && _hiddenForFullscreen && IsLoaded)
            {
                _restoreAfterFullscreen = true;
                Hide();
                return;
            }
            if (e.NewValue is true && IsLoaded)
            {
                UpdatePlacement();
                RaiseWithoutActivation();
            }
            UpdateInteractionState();
            if (IsLoaded) { UpdatePresentation(false); UpdateServices(); }
        };
        StateChanged += (_, _) =>
        {
            UpdateInteractionState();
            UpdatePresentation(false);
            UpdateServices();
        };
        Closed += (_, _) =>
        {
            _isClosed = true;
            _fullscreenObserver?.Dispose();
            _interaction.Dispose();
            _renameWindow?.Close();
            _settingsWindow?.CloseImmediately();
            _settings.SettingsChanged -= Settings_Changed;
            if (_ownsSettings) _settings.Dispose();
            _mediaViewModel.PropertyChanged -= Media_PropertyChanged;
            _services.Dispose();
            _mediaViewModel.Dispose();
            _clock.Dispose();
            _transitions.Dispose();
            _palette.Dispose();
            _theme.Changed -= Theme_Changed;
            _theme.Dispose();
            _source?.RemoveHook(WindowMessageHook);
        };
        Deactivated += (_, _) => { if (_settings.Current.AlwaysOnTop) RaiseWithoutActivation(); };
        _horizontalCenter = SystemParameters.PrimaryScreenWidth / 2;
        PositionAtTopCenter();
    }

    private void ConfigureNativeWindow()
    {
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowMessageHook);
        _services.AttachWindow(handle);
        ConfigureWidgetStyles(handle);
        RaiseWithoutActivation(SwpFrameChanged);
        UpdatePlacement();
    }

    private static void ConfigureWidgetStyles(nint handle)
    {
        var extendedStyle = GetWindowLong(handle, GwlExStyle);
        var widgetStyle = (extendedStyle | WsExToolWindow | WsExNoActivate) & ~WsExAppWindow;
        if (widgetStyle != extendedStyle)
            SetExtendedStyle(handle, widgetStyle);
    }

    private static void SetExtendedStyle(nint handle, int style)
    {
        SetLastError(0);
        var previousStyle = SetWindowLong(handle, GwlExStyle, style);
        if (previousStyle == 0 && Marshal.GetLastWin32Error() != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private void RaiseWithoutActivation(uint additionalFlags = 0, bool force = false)
    {
        if (!_settings.Current.AlwaysOnTop && !force && additionalFlags == 0) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0)
            return;

        // ShowInTaskbar=false gives WPF a hidden owner. A non-topmost owner can
        // keep its owned island below other windows despite the island's Topmost style.
        var owner = GetWindow(handle, GwOwner);
        if (owner != 0 && !IsWindowVisible(owner) &&
            GetWindowThreadProcessId(owner, out var processId) != 0 &&
            processId == Environment.ProcessId)
        {
            var ownerStyle = GetWindowLong(owner, GwlExStyle);
            if ((ownerStyle & WsExNoActivate) == 0)
                SetExtendedStyle(owner, ownerStyle | WsExNoActivate);
            SetTopmost(owner, 0, _settings.Current.AlwaysOnTop);
        }

        SetTopmost(handle, additionalFlags, _settings.Current.AlwaysOnTop);
    }

    private static void SetTopmost(nint handle, uint additionalFlags, bool enabled)
    {
        if (!SetWindowPos(handle, enabled ? HwndTopmost : new nint(-2), 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate | additionalFlags))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private void PillButton_Click(object sender, RoutedEventArgs e)
    {
        // The pill Button captures the mouse for its Click event. Permit this
        // intentional expansion while retaining capture protection elsewhere.
        _interaction.Click(allowWhileInteracting: !_isExpanded);
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Sibling controls must never toggle the island or request keyboard focus.
        for (var source = e.OriginalSource as DependencyObject; source is not null && source != IslandSurface;
             source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
            if (source is System.Windows.Controls.Primitives.ButtonBase or Slider or Thumb ||
                source is Controls.MediaPlayerCard or Controls.VolumeCard) return;
        _interaction.Click();
        e.Handled = true;
    }

    private void UpdateInteractionState()
    {
        var active = IsVisible && !_isClosed && WindowState != WindowState.Minimized;
        if (!active) _suppressMusicMorphHover = false;
        _interaction.SetActive(active);
    }

    private bool AcceptPointerMovement()
    {
        if (!_suppressMusicMorphHover) return true;
        if (GetCursorPos(out var position) && position.X == _musicMorphPointer.X && position.Y == _musicMorphPointer.Y)
            return false;
        _suppressMusicMorphHover = false;
        return true;
    }

    private void Surface_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _interaction.PointerEntered(allowExpansion: AcceptPointerMovement());
    }

    private void Surface_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // A widened pill appearing under a stationary cursor is not a hover gesture.
        // This is event-driven and reads the cursor only while that one guard is pending.
        if (_suppressMusicMorphHover && IslandSurface.IsMouseOver && AcceptPointerMovement())
            _interaction.PointerEntered();
    }

    private void SetExpanded(bool expanded)
    {
        if (_isClosed || _isExpanded == expanded)
            return;
        _isExpanded = expanded;
        // The selection resets immediately, but the outgoing header must not change mid-fade.
        Header.DataContext = expanded ? _viewModel : new { _viewModel.Title, _viewModel.IsSystem, _viewModel.Clock };
        _viewModel.Dashboard = Dashboard.Main;
        PillButton.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,
            _isExpanded ? "Collapse IslandDesk" : "Expand IslandDesk");

        UpdatePresentation();
        RaiseWithoutActivation();
        UpdateServices();
    }

    private bool WidgetVisible => IsLoaded && IsVisible && !_isClosed &&
        WindowState != WindowState.Minimized;

    private void UpdateServices()
    {
        _services.SetState(WidgetVisible, _isExpanded, _viewModel.IsSystem, _settings.Current);
        if (WidgetVisible && _isExpanded && _viewModel.IsSystem) _ = _viewModel.Device.EnsureLoadedAsync();
    }

    private void Media_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isClosed && (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(MediaViewModel.IsPlaying)))
            UpdatePresentation();
        if (!_isClosed && e.PropertyName == nameof(MediaViewModel.ArtworkAccent)) ApplyMusicPalette(true);
    }

    private void ApplyMusicPalette(bool animate)
    {
        _palette.Apply(_settings.Current, _mediaViewModel.ArtworkAccent, !_isExpanded && _mediaViewModel.IsPlaying,
            _settings.EffectiveFrameRate, animate && WidgetVisible,
            _isExpanded && !_viewModel.IsSystem && _settings.Current.HomeMedia && _settings.Current.MediaTimeline, _isExpanded,
            _isExpanded && !_viewModel.IsSystem && _settings.Current.HomeVolume);
        IslandSurface.Background = _palette.Surface;
    }
    private void Theme_Changed(object? sender, EventArgs e)
    {
        if (_isClosed) return;
        ApplyMusicPalette(false);
        ApplyCardSurface();
    }
    private void ApplyCardSurface()
    {
        // The palette owns the live card brush, including theme/opacity/tint updates.
        IslandSurface.CardBackground = _palette.CardBrush;
    }

    private void UpdatePresentation(bool animate = true)
    {
        var music = !_isExpanded && _mediaViewModel.IsPlaying;
        var state = (_isExpanded, music, _viewModel.Dashboard, WidgetVisible, _expandedScale);
        if (_presentationState == state) return;
        if (WidgetVisible && !_isExpanded && _presentationState is { Expanded: false } previous && previous.Music != music)
            _suppressMusicMorphHover = GetCursorPos(out _musicMorphPointer);
        _presentationState = state;
        ApplyMusicPalette(animate);
        var options = _settings.Current;
        var duration = animate && WidgetVisible && options.AnimationStyle == TransitionStyle.Smooth
            ? (int)Math.Round((_isExpanded ? _preset.ExpandMilliseconds : _preset.CollapseMilliseconds) / options.AnimationSpeed) : 0;
        var fade = duration == 0 ? 0 : (int)Math.Round(_preset.FadeMilliseconds / options.AnimationSpeed);
        var target = GetHostBounds(new Size(_isExpanded ? ExpandedWidth * _expandedScale : music ? MusicWidth : _clockPillWidth,
            _isExpanded ? (_viewModel.IsSystem ? options.SystemHeight : options.HomeHeight) * _expandedScale : CollapsedHeight));
        // Grow the transparent host once, animate only render geometry, then trim it.
        // Reversals retain the current visible size and the existing host envelope.
        var gutter = ShadowGutter;
        SetHostSize(duration == 0 ? target.Size : new Size(Math.Max(ActualWidth - 2 * gutter.Width, target.Width), Math.Max(ActualHeight - 2 * gutter.Height, target.Height)));
        UpdateVisualGeometry();
        _morph.Retarget(_transitions, _visualBounds, target, duration, () =>
        { SetHostSize(target.Size); UpdateVisualGeometry(); });
        // Keep the outgoing pill's identity while expanding. Prepare a hidden incoming
        // pill atomically; only a visible collapsed music/clock change needs a crossfade.
        if (!_isExpanded)
        {
            var pillFade = PillButton.Opacity > 0 ? fade : 0;
            Fade(ClockPill, !music, pillFade);
            Fade(MusicPill, music, pillFade);
        }
        PillButton.IsHitTestVisible = !_isExpanded;
        _transitions.Animate(_contentTransition, IslandContentTransition.BlendProperty, _isExpanded ? 1 : 0, fade, () =>
        {
            if (!_isExpanded || !WidgetVisible)
            {
                MainDashboard.DataContext = null;
                SystemDashboard.DataContext = null;
                Header.DataContext = _viewModel;
            }
        });
        ApplyContentOpacity(Math.Max(0, 1 - 2 * (double)_contentTransition.GetValue(IslandContentTransition.BlendProperty)),
            Math.Max(0, 2 * (double)_contentTransition.GetValue(IslandContentTransition.BlendProperty) - 1));
        _transitions.Animate(ContentTranslation, TranslateTransform.YProperty, _isExpanded ? 0 : -5, duration);
        ExpandedContent.IsHitTestVisible = _isExpanded;
        if (_isExpanded || !WidgetVisible)
        {
            MainDashboard.Visibility = _viewModel.IsSystem ? Visibility.Hidden : Visibility.Visible;
            SystemDashboard.Visibility = _viewModel.IsSystem ? Visibility.Visible : Visibility.Hidden;
        }
        // Keep the outgoing readings through the fade, then detach hidden bindings.
        if (_isExpanded || !WidgetVisible)
        {
            MainDashboard.DataContext = WidgetVisible && _isExpanded && !_viewModel.IsSystem ? _viewModel : null;
            SystemDashboard.DataContext = WidgetVisible && _isExpanded && _viewModel.IsSystem ? _viewModel : null;
        }
        _clock.SetVisible(WidgetVisible && (_isExpanded ? (_viewModel.IsSystem || options.HomeClock) : !music),
            dateVisible: WidgetVisible && _isExpanded && !_viewModel.IsSystem);
        MusicEqualizer.IsRunning = WidgetVisible && music && options.EqualizerEnabled;
        var needsAccent = options.DynamicAccent && (options.PillTint || options.DynamicTimeline || options.DynamicEqualizer);
        var volumeNeedsAccent = options.DynamicVolume && options.HomeVolume;
        var backgroundNeedsAccent = options.DynamicBackground && options.BackgroundTintStrength > 0 && options.BackgroundStyle == IslandBackgroundStyle.CharcoalGradient;
        _mediaViewModel.SetArtworkVisible(WidgetVisible && ((music && (options.MediaArtwork || needsAccent)) ||
            (_isExpanded && (!_viewModel.IsSystem && (options.HomeMedia && (options.MediaArtwork || needsAccent) || volumeNeedsAccent) || backgroundNeedsAccent))));
    }

    private void ApplyContentOpacity(double pill, double panel)
    {
        PillButton.Opacity = pill;
        ExpandedContent.Opacity = panel;
        PillButton.Visibility = pill > 0 ? Visibility.Visible : Visibility.Hidden;
        ExpandedContent.Visibility = panel > 0 ? Visibility.Visible : Visibility.Hidden;
    }

    private void Fade(UIElement element, bool visible, int duration, Action? completed = null)
    {
        if (visible) element.Visibility = Visibility.Visible;
        element.IsHitTestVisible = visible;
        _transitions.Animate(element, OpacityProperty, visible ? 1 : 0, duration,
            () => { if (!visible) element.Visibility = Visibility.Hidden; completed?.Invoke(); });
    }

    private void SelectDashboard(Dashboard dashboard)
    {
        if (!_isExpanded || _viewModel.Dashboard == dashboard || _isClosed) return;
        _viewModel.Dashboard = dashboard;
        UpdatePresentation();
        UpdateServices();
    }

    private void UpdateInteractionCapture() =>
        _interaction.SetInteractionInProgress(_renameWindow is not null || IslandSurface.IsMouseCaptureWithin);

    internal void OpenSettings(bool editTitle = false)
    {
        if (_isClosed) return;
        if (_settingsWindow is null)
        {
            _settingsWindow = new(_settings);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Show(); _settingsWindow.Activate();
        }
        if (editTitle) _settingsWindow.FocusTitle();
    }

    private void Settings_Changed(object? sender, EventArgs e) => ApplySettings();
    private void ApplySettings()
    {
        if (_isClosed) return;
        var options = _settings.Current;
        var previous = _appliedSettings;
        _appliedSettings = options;
        _viewModel.Title = PreferencesStore.NormalizeTitle(options.Title);
        if (previous?.ExpansionMode != options.ExpansionMode || previous?.CollapseMode != options.CollapseMode ||
            previous?.CollapseDelayMilliseconds != options.CollapseDelayMilliseconds)
            _interaction.Configure(new() { ExpandOnHover = options.ExpansionMode != ExpansionMode.Click,
                ExpandOnClick = options.ExpansionMode != ExpansionMode.Hover,
                CollapseOnHover = options.CollapseMode != ExpansionMode.Click,
                CollapseOnClick = options.CollapseMode != ExpansionMode.Hover,
                CollapseDelay = TimeSpan.FromMilliseconds(options.CollapseDelayMilliseconds) });
        _clock.Configure(new(options.Use24Hour, options.ShowSeconds));
        ApplyAnimationFrameRate();
        MainDashboard.MediaSection.ApplySettings(options, _settings.EffectiveFrameRate);
        MusicEqualizer.Configure(options);
        EqualizerColumn.Width = new(options.EqualizerEnabled ? MusicEqualizer.Width : 0);
        EqualizerGap.Width = new(options.EqualizerEnabled ? 12 : 0);
        CollapsedArtwork.Visibility = options.MediaArtwork ? Visibility.Visible : Visibility.Collapsed;
        CollapsedArtworkColumn.Width = new(options.MediaArtwork ? 24 : 0);
        CollapsedArtworkGap.Width = new(options.MediaArtwork ? 10 : 0);
        ApplyMusicPalette(false);
        IslandSurface.CornerRadius = new(options.SurfaceCornerRadius);
        if (previous?.CardCornerRadius != options.CardCornerRadius) Resources["IslandCardRadius"] = new CornerRadius(options.CardCornerRadius);
        UpdateVisualGeometry();
        if (previous?.SurfaceOpacity != options.SurfaceOpacity)
        {
            ApplyCardSurface();
        }
        if (previous?.AlwaysOnTop != options.AlwaysOnTop)
        { Topmost = options.AlwaysOnTop; RaiseWithoutActivation(force: true); }
        if (options.HideInFullscreen && _enableFullscreenDetection && IsLoaded)
            _fullscreenObserver ??= new(Dispatcher, new WindowInteropHelper(this).Handle, FullscreenChanged);
        else if (!options.HideInFullscreen)
        {
            _fullscreenObserver?.Dispose(); _fullscreenObserver = null;
            FullscreenChanged(false);
        }
        var layoutChanged = previous is null || previous.MediaHeight != options.MediaHeight || previous.HomeClock != options.HomeClock || previous.HomeMedia != options.HomeMedia ||
            previous.HomeVolume != options.HomeVolume || previous.Cpu != options.Cpu || previous.Gpu != options.Gpu ||
            previous.Ram != options.Ram || previous.Disk != options.Disk || previous.Network != options.Network || previous.Battery != options.Battery;
        var clockChanged = previous is null || previous.Use24Hour != options.Use24Hour || previous.ShowSeconds != options.ShowSeconds;
        if (layoutChanged)
        {
            MainDashboard.ApplySettings(options); SystemDashboard.ApplySettings(options);
            ExpandedContent.Height = 48 + Math.Max(options.HomeBodyHeight, options.SystemBodyHeight);
        }
        if (clockChanged)
        {
            var format = options.Use24Hour ? (options.ShowSeconds ? "HH:mm:ss" : "HH:mm") : (options.ShowSeconds ? "h:mm:ss tt" : "h:mm tt");
            var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
            var width = new[] { new DateTime(2000, 1, 1, 11, 58, 58), new DateTime(2000, 1, 1, 23, 58, 58) }
                .Max(time => new FormattedText(time.ToString(format, CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, typeface, 13, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace);
            _clockPillWidth = Math.Max(CollapsedWidth, Math.Ceiling(width + 19 + 9 + 28));
            // The collapsed layer keeps a fixed layout even inside the larger transition host.
            PillButton.Width = Math.Max(MusicWidth, _clockPillWidth) - 2;
        }
        if (layoutChanged || previous?.Alignment != options.Alignment || previous?.ManualTopOffset != options.ManualTopOffset ||
            previous?.UseWorkingArea != options.UseWorkingArea || previous?.XOffset != options.XOffset || previous?.YOffset != options.YOffset)
            UpdatePlacement();
        if (layoutChanged || clockChanged || previous?.AnimationStyle != options.AnimationStyle || previous?.AnimationSpeed != options.AnimationSpeed ||
            previous?.EqualizerEnabled != options.EqualizerEnabled || previous?.MediaArtwork != options.MediaArtwork || previous?.DynamicAccent != options.DynamicAccent ||
            previous?.PillTint != options.PillTint || previous?.DynamicTimeline != options.DynamicTimeline || previous?.DynamicEqualizer != options.DynamicEqualizer ||
            previous?.DynamicVolume != options.DynamicVolume || previous?.DynamicBackground != options.DynamicBackground ||
            previous?.BackgroundTintStrength != options.BackgroundTintStrength || previous?.BackgroundStyle != options.BackgroundStyle)
        { _presentationState = null; UpdatePresentation(false); }
        UpdateServices();
    }

    private double HorizontalCenter(double width)
    {
        var center = _settings.Current.Alignment switch
        {
            IslandAlignment.Left => _workLeft + _edgeMargin + width / 2,
            IslandAlignment.Right => _workRight - _edgeMargin - width / 2,
            _ => _horizontalCenter
        };
        var gutter = ShadowGutter.Width;
        return Math.Clamp(center + _offsetX, _monitorLeft + gutter + width / 2,
            Math.Max(_monitorLeft + gutter + width / 2, _monitorRight - gutter - width / 2));
    }

    private void ApplyAnimationFrameRate()
    {
        _transitions.ConfigureFrameRate(_settings.EffectiveFrameRate);
        MusicEqualizer.DesiredFrameRate = _settings.EffectiveFrameRate;
        MainDashboard.MediaSection.Timeline.SetFrameRate(_settings.EffectiveFrameRate);
    }

    private void RenameTitle()
    {
        if (_isClosed || _hiddenForFullscreen || _renameWindow is not null) return;
        _renameWindow = new(_viewModel.Title, async title =>
        {
            var normalized = PreferencesStore.NormalizeTitle(title);
            _settings.Title = normalized;
            return await _settings.FlushAsync();
        });
        _renameWindow.Closed += (_, _) => { _renameWindow = null; _restoreRenameAfterFullscreen = false; UpdateInteractionCapture(); };
        UpdateInteractionCapture();
        // Modeless and separate: WPF modal-owner activation never targets the island.
        _renameWindow.Show();
    }

    private void FullscreenChanged(bool fullscreen)
    {
        if (_isClosed || _hiddenForFullscreen == fullscreen) return;
        _hiddenForFullscreen = fullscreen;
        if (fullscreen)
        {
            // Only restore visibility that this observer changed. An independently hidden
            // or minimized widget must not be unexpectedly opened when fullscreen ends.
            _restoreAfterFullscreen = IsVisible && WindowState != WindowState.Minimized;
            _restoreRenameAfterFullscreen = _renameWindow?.IsVisible == true;
            if (_restoreRenameAfterFullscreen) _renameWindow?.Hide();
            if (_restoreAfterFullscreen) Hide();
        }
        else
        {
            if (_restoreAfterFullscreen)
            {
                _restoreAfterFullscreen = false;
                // Showing under a stationary cursor must not expand a previously collapsed pill.
                _suppressMusicMorphHover = GetCursorPos(out _musicMorphPointer);
                Show();
            }
            if (_restoreRenameAfterFullscreen)
            {
                _restoreRenameAfterFullscreen = false;
                _renameWindow?.RestoreWithoutActivation();
            }
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_changingHostBounds && !_morph.HasAnimatedProperties) PositionAtTopCenter();
    }

    private void ApplyVisualBounds(Rect bounds)
    {
        _visualBounds = bounds;
        UpdateVisualGeometry();
    }

    private void UpdateVisualGeometry()
    {
        if (_isClosed) return;
        var hostWidth = ActualWidth > 0 ? ActualWidth : Width;
        var bounds = new Rect(_visualBounds.X - Left, _visualBounds.Y - Top, _visualBounds.Width, _visualBounds.Height);
        IslandSurface.SetVisualBounds(bounds);
        IslandShadow.SetVisualBounds(bounds, IslandSurface.CornerRadius.TopLeft);
        PillTranslation.X = bounds.X + bounds.Width / 2 - hostWidth / 2;
        PillTranslation.Y = bounds.Y;
        var panel = GetHostBounds(new Size(ExpandedWidth * _expandedScale, bounds.Height));
        ContentTranslation.X = panel.X + panel.Width / 2 - Left - hostWidth / 2;
    }

    private Size ShadowGutter
    {
        get
        {
            var device = _source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            return new(Math.Ceiling(Controls.IslandShadow.MarginSize * device.M11) / device.M11,
                Math.Ceiling(Controls.IslandShadow.MarginSize * device.M22) / device.M22);
        }
    }

    private Rect GetHostBounds(Size size)
    {
        var device = _source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var width = Math.Max(1, Math.Round(size.Width * device.M11));
        var height = Math.Max(1, Math.Round(size.Height * device.M22));
        return new Rect(Math.Round(HorizontalCenter(size.Width) * device.M11 - width / 2) / device.M11,
            Math.Round(_topPosition * device.M22) / device.M22, width / device.M11, height / device.M22);
    }

    private void SetHostSize(Size size)
    {
        if (_isClosed) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0 || _source?.CompositionTarget is null) return;
        var transform = _source.CompositionTarget.TransformToDevice;
        var bounds = GetHostBounds(size);
        var gutter = ShadowGutter;
        bounds.Inflate(gutter.Width, gutter.Height);
        ExpandedContent.Margin = new Thickness(0, 14 * _expandedScale + gutter.Height, 0, 0);
        var width = (int)Math.Round(bounds.Width * transform.M11);
        var height = (int)Math.Round(bounds.Height * transform.M22);
        var left = (int)Math.Round(bounds.X * transform.M11);
        var top = (int)Math.Round(bounds.Y * transform.M22);
        if ((int)Math.Round(ActualWidth * transform.M11) == width && (int)Math.Round(ActualHeight * transform.M22) == height &&
            (int)Math.Round(Left * transform.M11) == left && (int)Math.Round(Top * transform.M22) == top) return;
        _changingHostBounds = true;
        try
        {
            if (!SetWindowPos(handle, 0, left, top, width, height, SwpNoActivate | SwpNoZOrder))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            // A single boundary layout commits full-size, sharp content before the next frame.
            UpdateLayout();
        }
        finally { _changingHostBounds = false; }
    }

    private void PositionAtTopCenter()
    {
        var gutter = ShadowGutter;
        var width = Math.Max(1, (ActualWidth > 0 ? ActualWidth : Width) - 2 * gutter.Width);
        var host = GetHostBounds(new Size(width, Math.Max(1, (ActualHeight > 0 ? ActualHeight : Height) - 2 * gutter.Height)));
        host.Inflate(gutter.Width, gutter.Height);
        Left = host.X;
        Top = host.Y;
        var retarget = _morph.HasAnimatedProperties;
        _visualBounds = GetHostBounds(_visualBounds.Size);
        UpdateVisualGeometry();
        if (retarget)
        {
            // A placement/DPI change is an explicit new anchor, not a reason to keep
            // animating toward the old monitor's screen coordinates.
            _presentationState = null;
            UpdatePresentation();
        }
    }

    private void UpdatePlacement()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        if (source?.CompositionTarget is null)
            return;

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _activeMonitor = monitor;

        if (_settings.Current.ManualTopOffset is { } legacy)
        {
            // Preserve the old absolute top override once, then save universal offsets.
            _settings.Update(_settings.Current with { ManualTopOffset = null,
                YOffset = Math.Max(0, legacy - (info.Work.Top - info.Monitor.Top)) });
            return;
        }

        // Win32 reports monitor/work rectangles in pixels; WPF positions in DIPs.
        var toDevice = source.CompositionTarget.TransformToDevice;
        var fromDevice = source.CompositionTarget.TransformFromDevice;
        var area = _settings.Current.UseWorkingArea ? info.Work : info.Monitor;
        var maxHeight = Math.Max(_settings.Current.HomeHeight, _settings.Current.SystemHeight);
        var shadow = ShadowGutter;
        var scale = DashboardLayout.ScaleToFit((area.Right - area.Left) / toDevice.M11 - 2 * Math.Max(_edgeMargin, shadow.Width),
            (area.Bottom - area.Top) / toDevice.M22 - 2 * Math.Max(TopMarginPixels / toDevice.M22, shadow.Height), maxHeight);
        var scaleChanged = SetDashboardScale(scale);
        var expandedHeightPixels = Math.Max(CollapsedHeight, maxHeight * scale) * toDevice.M22;
        var centerPixels = (area.Left + area.Right) / 2.0;
        var desiredTop = area.Top + TopMarginPixels + _settings.Current.YOffset;
        var earliestTop = info.Monitor.Top + shadow.Height * toDevice.M22;
        var latestTop = info.Monitor.Bottom - expandedHeightPixels - shadow.Height * toDevice.M22;
        var topPixels = Math.Clamp(desiredTop, earliestTop, Math.Max(earliestTop, latestTop));
        var position = fromDevice.Transform(new System.Windows.Point(centerPixels, topPixels));
        _horizontalCenter = position.X;
        _workLeft = area.Left / toDevice.M11;
        _workRight = area.Right / toDevice.M11;
        _monitorLeft = info.Monitor.Left / toDevice.M11;
        _monitorRight = info.Monitor.Right / toDevice.M11;
        _offsetX = _settings.Current.XOffset / toDevice.M11;
        _edgeMargin = TopMarginPixels / toDevice.M11;
        _topPosition = position.Y;
        PositionAtTopCenter();
        if (scaleChanged && IsLoaded) { _presentationState = null; UpdatePresentation(false); }
        _settings.UpdateDisplayRefreshRate(MonitorRefreshReader.Read(monitor));
        ApplyAnimationFrameRate();
        ApplyMusicPalette(true);
        _fullscreenObserver?.RequestCheck();
    }

    private bool SetDashboardScale(double scale)
    {
        if (Math.Abs(scale - _expandedScale) <= 0.000001) return false;
        _expandedScale = scale;
        var layout = new ScaleTransform(scale, scale); layout.Freeze();
        ExpandedContent.LayoutTransform = layout;
        ExpandedContent.Margin = new Thickness(0, 14 * scale + ShadowGutter.Height, 0, 0);
        return true;
    }

    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam,
        ref bool handled)
    {
        if (message == 0x0084 && Mouse.Captured is null) // WM_NCHITTEST: shadow is not an interactive area.
        {
            var point = new System.Windows.Point((short)(lParam.ToInt64() & 0xFFFF), (short)((lParam.ToInt64() >> 16) & 0xFFFF));
            if (!IsInsideSurface(PointFromScreen(point))) { handled = true; return -1; } // HTTRANSPARENT
        }
        if (message is WmSettingChange or 0x031A) _theme.RefreshSystem();
        if (message == 0x0218) _services.PowerChanged(wParam, lParam); // WM_POWERBROADCAST
        if (message == WmMouseWheel && WidgetVisible && _isExpanded)
        {
            var screen = new System.Windows.Point((short)(lParam.ToInt64() & 0xFFFF),
                (short)((lParam.ToInt64() >> 16) & 0xFFFF));
            if (IsInsideSurface(PointFromScreen(screen)))
            {
                var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                _volumeViewModel.AdjustFromWheel(delta);
                // Consume at our existing HWND hook before a Slider can also handle it.
                // No global input hook, activation, or duplicate Core Audio service.
                handled = true;
                return 0;
            }
        }
        if (message == WmMouseActivate)
        {
            // Keep the mouse click, but do not let WPF/native default processing activate us.
            handled = true;
            return MaNoActivate;
        }
        if (message is WmSettingChange or WmDisplayChange or WmDpiChanged ||
            (message == 0x0003 && MonitorFromWindow(hwnd, MonitorDefaultToNearest) != _activeMonitor))
            RequestPlacement();
        if (message is WmTimeChange or WmSettingChange) _clock.RefreshAndReschedule();
        return 0;
    }

    private void RequestPlacement()
    {
        if (_placementPending || _isClosed) return;
        _placementPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        { _placementPending = false; if (!_isClosed) UpdatePlacement(); }));
    }

    private bool IsInsideSurface(System.Windows.Point point)
    {
        var bounds = IslandSurface.VisualBounds;
        point = new(point.X - bounds.X, point.Y - bounds.Y);
        if (point.X < 0 || point.Y < 0 || point.X >= bounds.Width || point.Y >= bounds.Height) return false;
        var radius = Math.Min(IslandSurface.CornerRadius.TopLeft, Math.Min(bounds.Width, bounds.Height) / 2);
        if ((point.X >= radius && point.X <= bounds.Width - radius) ||
            (point.Y >= radius && point.Y <= bounds.Height - radius)) return true;
        var dx = point.X - (point.X < radius ? radius : bounds.Width - radius);
        var dy = point.Y - (point.Y < radius ? radius : bounds.Height - radius);
        return dx * dx + dy * dy <= radius * radius;
    }

    internal static int? ReadTopOffsetPixels()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "IslandDesk.settings.json");
        if (!File.Exists(path))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("TopOffsetPixels", out var value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt32(out var pixels) && pixels >= 0)
                return pixels;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable setting falls back to automatic work-area positioning.
        }

        return null;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(nint window, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter,
        int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("kernel32.dll", EntryPoint = "SetLastError")]
    private static extern void SetLastError(uint errorCode);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint position);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
