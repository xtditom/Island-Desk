using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IslandDesk.Controls;

// All cards are created once. Additional children automatically make more pages;
// each page has at most nine cards and uses the same measured area.
public sealed class PagedActionGrid : Grid
{
    private const int PageSize = 9;
    private int _page, _wheel;
    private long _lastWheel, _lastNavigation;
    private UIElement[] _order = [];
    public IReadOnlyList<UIElement> OrderedChildren => _order;
    public int PageIndex => _page;
    public int PageCount => Math.Max(1, (Children.Count + PageSize - 1) / PageSize);
    public event EventHandler? PageChanged;
    public PagedActionGrid()
    {
        Background = Brushes.Transparent;
        for (var i = 0; i < 5; i++)
        {
            ColumnDefinitions.Add(new() { Width = i % 2 == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(8) });
            RowDefinitions.Add(new() { Height = new GridLength(i % 2 == 0 ? 70 : 8) });
        }
        Loaded += (_, _) => ArrangePage();
        MouseLeave += (_, _) => ResetWheel();
        PreviewMouseWheel += (_, args) => { ConsumeWheel(args.Delta); args.Handled = true; };
    }
    public bool Navigate(int page)
    {
        page = Math.Clamp(page, 0, PageCount - 1);
        if (_page == page) return false;
        _page = page;
        ArrangePage();
        PageChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
    public void Reset() { ResetWheel(); Navigate(0); }
    public void ConsumeWheel(int delta)
    {
        if (delta == 0 || !IsEnabled) return;
        var now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_lastWheel, now).TotalMilliseconds > 250 || Math.Sign(_wheel) != Math.Sign(delta)) _wheel = 0;
        _lastWheel = now;
        // One page per gesture/detent; discard momentum and excess delta rather
        // than queuing multiple jumps from high-resolution wheels.
        if (_lastNavigation != 0 && Stopwatch.GetElapsedTime(_lastNavigation, now).TotalMilliseconds < 250) return;
        _wheel += delta;
        if (Math.Abs(_wheel) < 120) return;
        Navigate(_page + (_wheel < 0 ? 1 : -1));
        _wheel = 0; _lastNavigation = now;
    }
    private void ResetWheel() { _wheel = 0; _lastWheel = 0; _lastNavigation = 0; }
    protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        _order = Children.Cast<UIElement>().ToArray();
        _page = Math.Min(_page, PageCount - 1);
        ArrangePage();
    }
    private void ArrangePage()
    {
        var start = _page * PageSize;
        var count = Math.Min(PageSize, Children.Count - start);
        for (var i = 0; i < _order.Length; i++)
        {
            var card = _order[i];
            var visible = i >= start && i < start + count;
            card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible) continue;
            var slot = i - start;
            SetColumn(card, slot % 3 * 2);
            SetRow(card, count <= 3 ? 2 : slot / 3 * 2);
        }
    }
    public void SetOrder(IEnumerable<UIElement> order)
    {
        var next = order.ToArray();
        if (next.Length != Children.Count || next.Distinct().Count() != Children.Count || next.Any(item => !Children.Contains(item)))
            throw new ArgumentException("The order must contain each card exactly once.", nameof(order));
        _order = next;
        ArrangePage();
    }
    public FrameworkElement? CardAt(Point point) => _order.OfType<FrameworkElement>().FirstOrDefault(card =>
        card.Visibility == Visibility.Visible && new Rect(card.TranslatePoint(new Point(), this), card.RenderSize).Contains(point));
}
