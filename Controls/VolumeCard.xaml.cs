using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using IslandDesk.Audio;
namespace IslandDesk.Controls;
public partial class VolumeCard : UserControl
{
    public VolumeCard()
    {
        InitializeComponent();
        MasterVolumeSlider.AddHandler(Thumb.DragStartedEvent,
            new DragStartedEventHandler((_, _) => (DataContext as VolumeViewModel)?.SetDragging(true)));
        MasterVolumeSlider.AddHandler(Thumb.DragCompletedEvent,
            new DragCompletedEventHandler((_, _) => (DataContext as VolumeViewModel)?.SetDragging(false)));
    }
}
