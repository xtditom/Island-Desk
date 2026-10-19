using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using IslandDesk.Controls;

internal static class HomeGpuValidation
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var bar = new PlaybackBar { Width = 320, Height = 16 };
        var container = new Grid { Children = { bar } };
        var window = new Window { Content = container, Width = 700, Height = 120, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); await Task.Delay(100);
            var timer = Field<DispatcherTimer>(bar, "_progress");
            var fill = Field<RectangleGeometry>(bar, "_fill");
            foreach (var fps in new[] { 60, 90, 100 })
            {
                bar.SetProgress(.2, 1, 480, fps);
                check(timer.IsEnabled && Field<object?>(bar, "_clock") is null,
                    $"{fps} FPS: slow progress schedules subpixel changes without a continuous animation clock");
                var changes = 0; void Changed(object? sender, EventArgs args) => changes++;
                fill.Changed += Changed;
                var start = fill.Rect.Width; await Task.Delay(300);
                fill.Changed -= Changed;
                check(changes > 0 && changes < fps * .3,
                    $"{fps} FPS: slow progress changes geometry only {changes} times over 300ms");
                check(fill.Rect.Width > start && fill.Rect.Width - start < .3,
                    "Demand-driven progress follows actual playback time rather than incrementing by timer ticks");
                bar.SetProgress(.4);
                check(!timer.IsEnabled && !bar.IsAnimating && fill.Rect.Width == 128,
                    "Pause/seek replacement stops demand-driven work and presents the exact requested position");
                bar.SetProgress(.2, 1, 2, fps);
                check(!timer.IsEnabled && Field<AnimationClock>(bar, "_clock") is { } clock &&
                    Timeline.GetDesiredFrameRate(clock.Timeline) == fps,
                    $"{fps} FPS: faster motion retains the existing selected-rate WPF animation");
            }
            bar.SetProgress(.2, 1, 480, 60); var normalInterval = timer.Interval;
            container.LayoutTransform = new ScaleTransform(2, 2); window.UpdateLayout();
            bar.SetProgress(.2, 1, 480, 100);
            check(Math.Abs(timer.Interval.TotalSeconds / normalInterval.TotalSeconds - .5) < .01,
                "Ancestor scale is included in physical-pixel scheduling, preserving detail at higher display density");
            container.LayoutTransform = null; window.UpdateLayout();
            bar.SetProgress(.2, 1, 480, 60); bar.Visibility = Visibility.Hidden;
            var hidden = fill.Rect; await Task.Delay(150);
            check(!bar.IsAnimating && !timer.IsEnabled && fill.Rect == hidden, "Hidden bars stop all progress work immediately");
            bar.Visibility = Visibility.Visible;
            bar.SetProgress(.2, 1, 480, 60); window.Content = null; await Task.Delay(100);
            check(!bar.IsAnimating && !timer.IsEnabled, "Unloaded bars release scheduled progress without waiting for collection");
            window.Content = container; await Task.Delay(100);
            bar.SetProgress(.5, .5001, .1, 60); await Task.Delay(300);
            check(!bar.IsAnimating && !timer.IsEnabled && fill.Rect.Width == 320 * .5001,
                "Demand-driven completion commits the exact endpoint and stops its timer");
            bar.SetProgress(.2, 1, 480, 60); bar.SetProgress(.8, .8001, .1, 60); await Task.Delay(300);
            check(!bar.IsAnimating && fill.Rect.Width == 320 * .8001,
                "Rapid replacement cannot allow an old scheduled tick to overwrite the new endpoint");
            bar.SetProgress(.5, .5, 600, 100);
            check(!bar.IsAnimating && Field<object?>(bar, "_clock") is null && !timer.IsEnabled,
                "Identical endpoints perform no animation work");
        }
        finally { window.Close(); }
    }
    private static T Field<T>(object source, string name) => (T)source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
}
