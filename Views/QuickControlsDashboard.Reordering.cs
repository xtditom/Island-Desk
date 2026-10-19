using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IslandDesk.Settings;

namespace IslandDesk.Views;

public partial class QuickControlsDashboard
{
    private const string ControlDragFormat = "IslandDesk.QuickControlOrder.v1";
    public static readonly DependencyProperty IsDragSourceProperty = DependencyProperty.RegisterAttached(
        "IsDragSource", typeof(bool), typeof(QuickControlsDashboard), new PropertyMetadata(false));
    public static bool GetIsDragSource(DependencyObject target) => (bool)target.GetValue(IsDragSourceProperty);
    public static void SetIsDragSource(DependencyObject target, bool value) => target.SetValue(IsDragSourceProperty, value);
    public static readonly DependencyProperty IsDropTargetProperty = DependencyProperty.RegisterAttached(
        "IsDropTarget", typeof(bool), typeof(QuickControlsDashboard), new PropertyMetadata(false));
    public static bool GetIsDropTarget(DependencyObject target) => (bool)target.GetValue(IsDropTargetProperty);
    public static void SetIsDropTarget(DependencyObject target, bool value) => target.SetValue(IsDropTargetProperty, value);
    private readonly Dictionary<string, FrameworkElement> _cards = new(StringComparer.Ordinal);
    private DispatcherTimer? _pageHover;
    private FrameworkElement? _pressedCard, _dropTarget;
    private Point _pressPoint;
    private bool _dragging, _dropCommitted, _suppressClick;
    private int _hoverDirection, _dragStartPage;
    private string? _dragToken;
    private string[]? _pendingOrder;
    internal event Action<string[]>? OrderChanged;
    internal static bool IsControlDrag(IDataObject data) => data.GetDataPresent(ControlDragFormat, false);
    private string[] CurrentOrder => Actions.OrderedChildren.OfType<FrameworkElement>().Select(card => (string)card.Tag).ToArray();

    private void InitializeReordering()
    {
        foreach (var card in Actions.Children.OfType<FrameworkElement>())
        {
            var id = (string)card.Tag;
            _cards.Add(id, card);
            var menu = new ContextMenu();
            menu.SetResourceReference(Control.BackgroundProperty, "PopupSurface");
            menu.SetResourceReference(Control.ForegroundProperty, "Primary");
            menu.SetResourceReference(Control.BorderBrushProperty, "PopupBorder");
            AddMoveItem(menu, "Move earlier", () => MoveCard(card, -1));
            AddMoveItem(menu, "Move later", () => MoveCard(card, 1));
            AddMoveItem(menu, "Move to other page", () => MoveCardToOtherPage(card));
            if (id is "Wifi" or "Bluetooth" or "Brightness")
            {
                menu.Items.Add(new Separator());
                AddMoveItem(menu, "Open Windows Settings", () => OpenHardwareSettings(id));
            }
            card.ContextMenu = menu;
            ContextMenuService.SetShowOnDisabled(card, true);
            menu.Opened += (_, _) => { _contextMenuOpen = true; NotifyInteraction(); };
            menu.Closed += (_, _) => { _contextMenuOpen = false; NotifyInteraction(); };
        }
        _pageHover = new(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(450) };
        _pageHover.Tick += (_, _) =>
        {
            _pageHover?.Stop();
            if (_dragging && _active && _hoverDirection != 0) Actions.Navigate(Actions.PageIndex + _hoverDirection);
        };
        AllowDrop = true;
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Reorder_MouseDown), true);
        PreviewMouseMove += Reorder_MouseMove;
        PreviewMouseLeftButtonUp += (_, _) => _pressedCard = null;
        DragOver += Reorder_DragOver;
        Drop += Reorder_Drop;
        DragLeave += (_, args) =>
        {
            if (!IsControlDrag(args.Data)) return;
            _pageHover?.Stop(); _hoverDirection = 0; ClearDropTarget(); args.Handled = true;
        };
        QueryContinueDrag += (_, args) =>
        {
            if (!_dragging) return;
            args.Action = !_active || _disposed || args.EscapePressed ? DragAction.Cancel :
                (args.KeyStates & DragDropKeyStates.LeftMouseButton) == 0 ? DragAction.Drop : DragAction.Continue;
            args.Handled = true;
        };
    }
    private static void AddMoveItem(ContextMenu menu, string label, Action action)
    {
        var item = new MenuItem { Header = label };
        item.SetResourceReference(Control.ForegroundProperty, "Primary");
        item.Click += (_, _) => action(); menu.Items.Add(item);
    }
    internal void ApplyLayoutOrder(IEnumerable<string> saved)
    {
        var order = QuickControlLayout.Normalize(saved);
        if (CurrentOrder.SequenceEqual(order)) return;
        if (_dragging) { _pendingOrder = order; return; }
        Actions.SetOrder(order.Select(id => _cards[id]));
        UpdatePagination(); UpdateMicrophoneMonitoring(); UpdateHardwareMonitoring();
    }
    private void SaveOrder(IEnumerable<FrameworkElement> cards)
    {
        var order = cards.Select(card => (string)card.Tag).ToArray();
        if (CurrentOrder.SequenceEqual(order)) return;
        Actions.SetOrder(order.Select(id => _cards[id]));
        OrderChanged?.Invoke(order);
        UpdateMicrophoneMonitoring(); UpdateHardwareMonitoring();
    }
    private void MoveCard(FrameworkElement card, int direction)
    {
        if (_dragging || _busy) return;
        var cards = Actions.OrderedChildren.Cast<FrameworkElement>().ToList();
        var from = cards.IndexOf(card); var to = Math.Clamp(from + direction, 0, cards.Count - 1);
        if (from == to) return;
        cards.RemoveAt(from); cards.Insert(to, card); SaveOrder(cards); Actions.Navigate(to / 9);
    }
    private void MoveCardToOtherPage(FrameworkElement card)
    {
        if (_dragging || _busy) return;
        var cards = Actions.OrderedChildren.Cast<FrameworkElement>().ToList();
        var from = cards.IndexOf(card); var page = (from / 9 + 1) % Actions.PageCount;
        var to = Math.Min(page * 9 + from % 9, cards.Count - 1);
        cards.RemoveAt(from); cards.Insert(to, card); SaveOrder(cards); Actions.Navigate(page);
    }
    private void Reorder_MouseDown(object sender, MouseButtonEventArgs args)
    {
        _pressedCard = null; _suppressClick = false;
        if (args.ChangedButton != MouseButton.Left || IsInteracting || !_active) return;
        // Slider and Thumb retain their ordinary drag behavior.
        for (var node = args.OriginalSource as DependencyObject; node is not null && node != this;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is Slider or Thumb) return;
        _pressedCard = Actions.CardAt(args.GetPosition(Actions));
        _pressPoint = args.GetPosition(this);
    }
    private void Reorder_MouseMove(object sender, MouseEventArgs args)
    {
        if (_pressedCard is not { } source || args.LeftButton != MouseButtonState.Pressed || _dragging || _busy || !_active) return;
        var point = args.GetPosition(this);
        if (Math.Abs(point.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressedCard = null; args.Handled = true;
        _dragging = true; _dropCommitted = false; _dragStartPage = Actions.PageIndex;
        _dragToken = Guid.NewGuid().ToString("N");
        SetIsDragSource(source, true);
        DragPreviewLabel.Text = "Move · " + QuickControlLayout.Controls.First(control => control.Id == (string)source.Tag).Name;
        DragPreview.Visibility = Visibility.Visible;
        StopPageAnimation(); NotifyInteraction();
        try
        {
            var data = new DataObject(); data.SetData(ControlDragFormat, _dragToken, false);
            DragDrop.DoDragDrop(source, data, DragDropEffects.Move);
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        { Status.Text = "Windows could not complete this drag. The layout was kept."; }
        finally
        {
            _dragging = false; _dragToken = null; _pageHover?.Stop(); _hoverDirection = 0;
            SetIsDragSource(source, false); ClearDropTarget(); DragPreview.Visibility = Visibility.Collapsed;
            if (!_dropCommitted) Actions.Navigate(_dragStartPage);
            if (_pendingOrder is { } order) { _pendingOrder = null; ApplyLayoutOrder(order); }
            _suppressClick = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _suppressClick = false));
            NotifyInteraction();
        }
    }
    private bool AcceptReorder(DragEventArgs args) => _dragging && _active && !_disposed && IsControlDrag(args.Data) &&
        string.Equals(args.Data.GetData(ControlDragFormat, false) as string, _dragToken, StringComparison.Ordinal);
    private void Reorder_DragOver(object sender, DragEventArgs args)
    {
        if (!IsControlDrag(args.Data)) return;
        args.Handled = true; args.Effects = DragDropEffects.None;
        if (!AcceptReorder(args)) return;
        var point = args.GetPosition(this);
        var direction = Contains(PreviousPage, point) && Actions.PageIndex > 0 ? -1 :
            Contains(NextPage, point) && Actions.PageIndex + 1 < Actions.PageCount ? 1 : 0;
        if (_hoverDirection != direction)
        {
            _pageHover!.Stop(); _hoverDirection = direction;
            if (direction != 0) _pageHover?.Start();
        }
        var target = Actions.CardAt(args.GetPosition(Actions));
        if (_dropTarget != target)
        {
            ClearDropTarget(); _dropTarget = target;
            if (target is not null) SetIsDropTarget(target, true);
        }
        if (target is not null)
        {
            var origin = target.TranslatePoint(new Point(), Actions);
            var after = args.GetPosition(target).X >= target.ActualWidth / 2;
            Canvas.SetLeft(InsertionMarker, origin.X + (after ? target.ActualWidth + 2 : -4));
            Canvas.SetTop(InsertionMarker, origin.Y + 3);
            InsertionMarker.Visibility = Visibility.Visible;
        }
        if (target is not null || direction != 0) args.Effects = DragDropEffects.Move;
    }
    private bool Contains(FrameworkElement element, Point point) => new Rect(element.TranslatePoint(new Point(), this), element.RenderSize).Contains(point);
    private void Reorder_Drop(object sender, DragEventArgs args)
    {
        if (!IsControlDrag(args.Data)) return;
        args.Handled = true; args.Effects = DragDropEffects.None;
        if (!AcceptReorder(args) || Actions.CardAt(args.GetPosition(Actions)) is not { } target) return;
        var source = _cards.Values.First(card => GetIsDragSource(card));
        var cards = Actions.OrderedChildren.Cast<FrameworkElement>().ToList();
        var from = cards.IndexOf(source); var insert = cards.IndexOf(target);
        if (args.GetPosition(target).X >= target.ActualWidth / 2) insert++;
        cards.RemoveAt(from); if (from < insert) insert--;
        cards.Insert(Math.Clamp(insert, 0, cards.Count), source);
        SaveOrder(cards); _dropCommitted = true; args.Effects = DragDropEffects.Move;
    }
    private void ClearDropTarget()
    {
        if (_dropTarget is not null) SetIsDropTarget(_dropTarget, false);
        _dropTarget = null;
        InsertionMarker.Visibility = Visibility.Collapsed;
    }
}
