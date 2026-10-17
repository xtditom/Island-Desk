using System.Windows.Controls;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using IslandDesk.Media;
using IslandDesk.Settings;
namespace IslandDesk.Controls;
public partial class MediaPlayerCard : UserControl
{
    public MediaPlayerCard() => InitializeComponent();
    internal void ApplySettings(AppSettings settings, int? fps = null)
    {
        AlbumArtwork.Visibility = settings.MediaArtwork ? Visibility.Visible : Visibility.Collapsed;
        ArtworkColumn.Width = new(settings.MediaArtwork ? 56 : 0);
        Metadata.Margin = new(settings.MediaArtwork ? 10 : 0, 0, 0, 0);
        ArtistText.Visibility = settings.MediaArtist ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Visibility = settings.MediaStatus ? Visibility.Visible : Visibility.Collapsed;
        TransportCard.Visibility = settings.MediaControls ? Visibility.Visible : Visibility.Collapsed;
        ControlsColumn.Width = new(settings.MediaControls ? 108 : 0);
        Timeline.Visibility = settings.MediaTimeline ? Visibility.Visible : Visibility.Collapsed;
        TimelineDivider.Visibility = Timeline.Visibility;
        TimelineRow.Height = new(settings.MediaTimeline ? (settings.TimelineTimestamps ? 40 : 24) : 0);
        Timeline.Configure(settings, fps);
    }
    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Buttons own their gestures, including clicks on their nested icon/text.
        for (var source = e.OriginalSource as DependencyObject; source is not null && source != this;
             source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
            if (source is ButtonBase) return;

        // Consume unavailable media clicks too; never toggle the enclosing island.
        e.Handled = true;
        if (e.ClickCount == 1 && DataContext is MediaViewModel media && media.ToggleCommand.CanExecute(null))
            media.ToggleCommand.Execute(null);
    }
}
