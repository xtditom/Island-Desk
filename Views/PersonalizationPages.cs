using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace IslandDesk.Views;

// Shares the existing Settings styles, data context and persistence controller.
internal sealed class PersonalizationPages(FrameworkElement owner)
{
    private static Binding Bind(string property) => new(property)
    { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
    private TextBlock Text(string value, string? style = null) => new() { Text = value, TextWrapping = TextWrapping.Wrap,
        Style = style is null ? null : (Style)owner.FindResource(style) };
    private CheckBox Toggle(string label, string property)
    { var toggle = new CheckBox { Content = label }; toggle.SetBinding(CheckBox.IsCheckedProperty, Bind(property)); return toggle; }
    private Grid Choice(string label, string options, string property)
    {
        var grid = Row(label); var combo = new ComboBox { MinWidth = 150, ItemTemplate = EnumTemplate(), Margin = new(8, 0, 0, 0) };
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(options)); combo.SetBinding(ComboBox.SelectedItemProperty, Bind(property));
        AutomationProperties.SetName(combo, label); Grid.SetColumn(combo, 1); grid.Children.Add(combo); return grid;
    }
    private static DataTemplate EnumTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding { Converter = new EnumLabelConverter() });
        return new() { VisualTree = text };
    }
    private Grid Row(string label)
    {
        var grid = new Grid { Margin = new(0, 8, 0, 8) };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = Text(label); text.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(text); return grid;
    }
    private StackPanel Slider(string label, string property, double min, double max, double step, string suffix = "")
    {
        var stack = new StackPanel { Margin = new(0, 9, 0, 9) }; var row = Row(label);
        var value = Text(""); value.SetBinding(TextBlock.TextProperty, new Binding(property) { StringFormat = "{0:0.##}" + suffix });
        value.SetResourceReference(TextBlock.ForegroundProperty, "Secondary"); Grid.SetColumn(value, 1); row.Children.Add(value);
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = step, SmallChange = step, IsSnapToTickEnabled = true };
        slider.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty, Bind(property)); AutomationProperties.SetName(slider, label);
        stack.Children.Add(row); stack.Children.Add(slider); return stack;
    }
    private StackPanel Color(string label, string property)
    {
        var stack = new StackPanel { Margin = new(0, 8, 0, 8) }; var row = Row(label);
        var input = new TextBox { Width = 110, MaxLength = 7, ToolTip = "#RRGGBB · applied when you leave this field" };
        input.SetBinding(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus, ValidatesOnExceptions = true });
        AutomationProperties.SetName(input, label); Grid.SetColumn(input, 1); row.Children.Add(input); stack.Children.Add(row); return stack;
    }
    private Border Card(params UIElement[] children)
    { var stack = new StackPanel(); foreach (var child in children) stack.Children.Add(child); return new() { Style = (Style)owner.FindResource("SettingsCard"), Child = stack }; }
    private ScrollViewer Page(string title, string description, params UIElement[] cards)
    {
        var stack = new StackPanel(); stack.Children.Add(Text(title, "SectionTitle")); stack.Children.Add(Text(description, "Description"));
        foreach (var card in cards) stack.Children.Add(card);
        return new() { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 8, 0) };
    }
    internal Border Appearance() => Card(Choice("Background style", "BackgroundStyles", "BackgroundStyle"),
        Slider("Gradient intensity", "GradientIntensity", 0, 100, 5, "%"),
        Slider("Island corner radius", "SurfaceCornerRadius", 12, 22, 1), Slider("Card corner radius", "CardCornerRadius", 6, 16, 1));
    internal ScrollViewer Media() => Page("Media player", "Separate information, transport and timeline cards. Wheel input always changes system volume.",
        Card(Toggle("Show album artwork", "MediaArtwork"), Toggle("Show artist", "MediaArtist"), Toggle("Show playback status", "MediaStatus"), Toggle("Show transport controls", "MediaControls")),
        Card(Toggle("Show playback timeline", "MediaTimeline"), Toggle("Show timeline timestamps", "TimelineTimestamps"), Toggle("Allow seeking when supported", "TimelineSeeking"),
            Slider("Timeline thickness", "TimelineThickness", 2, 6, .5), Slider("Remaining-track opacity", "TimelineRemainingOpacity", .08, .5, .02),
            Choice("Timeline color", "TimelineColors", "TimelineColor"), Color("Custom timeline color", "TimelineCustomColor")));
    internal ScrollViewer Dynamic() => Page("Dynamic music colors", "Artwork accents are cached and kept legible. The charcoal surface remains subtle.",
        Card(Toggle("Use artwork-based accents", "DynamicAccent"), Toggle("Tint the collapsed music pill", "PillTint"), Slider("Pill tint strength", "TintStrength", 0, 10, .5, "%")),
        Card(Toggle("Dynamic timeline color", "DynamicTimeline"), Toggle("Dynamic frequency bar color", "DynamicEqualizer"),
            Toggle("Dynamic volume slider color", "DynamicVolume"), Color("Fallback accent color", "FallbackAccent")),
        BackgroundOptions());
    private Border BackgroundOptions()
    {
        var toggle = Toggle("Dynamic background color", "DynamicBackground");
        var strength = Slider("Background tint strength", "BackgroundTintStrength", 0, 30, 1, "%");
        toggle.SetBinding(UIElement.IsEnabledProperty, new Binding("CanUseDynamicBackground"));
        strength.SetBinding(UIElement.IsEnabledProperty, new Binding("CanUseDynamicBackground"));
        var explanation = Text("Requires Gradient background.");
        explanation.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 11.0));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8)));
        var trigger = new DataTrigger { Binding = new Binding("CanUseDynamicBackground"), Value = false };
        trigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible)); style.Triggers.Add(trigger);
        explanation.Style = style;
        return Card(toggle, strength, explanation);
    }
    internal ScrollViewer Equalizer() => Page("Frequency Bar", "A visual playback indicator, not an audio spectrum. Motion stops when hidden or inactive.",
        Card(Toggle("Enable frequency bar", "EqualizerEnabled"), Choice("Animation origin", "EqualizerStyles", "EqualizerStyle")),
        Card(Slider("Bar count", "EqualizerBarCount", 2, 12, 1), Slider("Bar width", "EqualizerBarWidth", 1, 4, .5),
            Slider("Bar spacing", "EqualizerBarSpacing", 1, 4, .5), Slider("Bar height / amplitude", "EqualizerBarHeight", 6, 18, 1), Slider("Motion speed", "EqualizerSpeed", .5, 2, .1, "×")),
        Card(Choice("Color source", "EqualizerColors", "EqualizerColor"), Color("Custom frequency bar color", "EqualizerCustomColor")));
    private sealed class EnumLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is IslandDesk.Settings.IslandBackgroundStyle background
            ? background == IslandDesk.Settings.IslandBackgroundStyle.CharcoalGradient ? "Gradient" : "Solid"
            : Regex.Replace(value?.ToString() ?? "", "(?<=[a-z])(?=[A-Z])", " ");
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
