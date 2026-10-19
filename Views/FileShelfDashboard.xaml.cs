using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using IslandDesk.Presentation;
using IslandDesk.Windowing;
namespace IslandDesk.Views;
public partial class FileShelfDashboard : UserControl, IDisposable
{
    internal FileShelfViewModel Model { get; } = new();
    internal IShelfFileActions FileActions { get; set; } = new ShelfFileActions();
    internal bool IsInteracting { get; private set; }
    internal event EventHandler? InteractionChanged;
    private Point _dragStart;
    private ShelfFile? _dragFile;
    private readonly DispatcherTimer _clearExpiry;
    private Window? _clearOwner;
    public static readonly DependencyProperty IsClearArmedProperty = DependencyProperty.Register(nameof(IsClearArmed), typeof(bool), typeof(FileShelfDashboard), new PropertyMetadata(false));
    public bool IsClearArmed => (bool)GetValue(IsClearArmedProperty);
    public FileShelfDashboard()
    {
        InitializeComponent(); DataContext = Model;
        _clearExpiry = new(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromSeconds(5) };
        _clearExpiry.Tick += (_, _) => CancelClear();
        IsVisibleChanged += (_, _) => { if (!IsVisible) { CancelClear(); CancelFilePress(); } };
        Unloaded += (_, _) => { CancelClear(); CancelFilePress(); };
        MouseLeave += (_, _) => CancelClear();
        PreviewMouseLeftButtonDown += (_, _) => CancelFilePress();
        Model.PropertyChanged += Model_Changed;
    }
    private void SetBusy(bool value) { IsInteracting = value; InteractionChanged?.Invoke(this, EventArgs.Empty); }
    private void Model_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (IsClearArmed && !Model.HasFiles) CancelClear(); }
    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; if (IsInteracting || Model.IsBusy) return;
        SetBusy(true);
        try
        {
            // No owner HWND: closing the activating system picker must not activate the widget.
            var picker = new OpenFileDialog { Title = "Add files to File Shelf", Multiselect = true,
                CheckFileExists = true, CheckPathExists = true, AddToRecent = false };
            if (picker.ShowDialog() == true) await Model.AddAsync(picker.FileNames);
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { Model.Status = "Windows could not open the file picker. You can also drop files here."; }
        finally { SetBusy(false); }
    }
    private void Files_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = CanReceiveFiles(e.Data, e.AllowedEffects) ? DragDropEffects.Copy : DragDropEffects.None;
    }
    internal void SetDropTarget(bool active)
    {
        if (active) ShelfCard.SetResourceReference(Border.BorderBrushProperty, "SurfaceHoverBorder");
        else ShelfCard.ClearValue(Border.BorderBrushProperty);
    }
    internal bool CanStageDrag(IDataObject data, DragDropEffects allowed)
    {
        if (!CanReceiveFiles(data, allowed)) return false;
        try
        {
            return data.GetData(DataFormats.FileDrop, false) is string[] paths &&
                paths.Any(FileShelfViewModel.IsSupportedLocalFile);
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException) { return false; }
    }
    private async void Files_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        // OLE consumes Effects synchronously; metadata collection may finish later.
        e.Effects = CanReceiveFiles(e.Data, e.AllowedEffects) ? DragDropEffects.Copy : DragDropEffects.None;
        if (e.Effects == DragDropEffects.Copy) await ReceiveFilesAsync(e.Data, e.AllowedEffects);
    }
    internal bool CanReceiveFiles(IDataObject data, DragDropEffects allowed)
    {
        try { return Model.CanAdd && !IsInteracting && (allowed & DragDropEffects.Copy) != 0 && data.GetDataPresent(DataFormats.FileDrop, false); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException) { return false; }
    }
    internal async Task<DragDropEffects> ReceiveFilesAsync(IDataObject data, DragDropEffects allowed)
    {
        if (!CanReceiveFiles(data, allowed)) return DragDropEffects.None;
        try
        {
            if (data.GetData(DataFormats.FileDrop, false) is string[] paths)
            {
                SetBusy(true);
                try { await Model.AddAsync(paths); return DragDropEffects.Copy; }
                finally { SetBusy(false); }
            }
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        { Model.Status = "These dropped files are unavailable. Try Add Files instead."; }
        return DragDropEffects.None;
    }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!Model.HasFiles || IsInteracting) return;
        if (IsClearArmed) { CancelClear(); Model.Clear(); return; }
        SetValue(IsClearArmedProperty, true); ClearLabel.Text = "Confirm Clear";
        _clearExpiry.Start();
        _clearOwner = Window.GetWindow(this);
        _clearOwner?.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Clear_OutsidePress), true);
    }
    internal void CancelClear()
    {
        _clearExpiry?.Stop();
        if (_clearOwner is not null) _clearOwner.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Clear_OutsidePress));
        _clearOwner = null;
        if (!IsClearArmed) return;
        SetValue(IsClearArmedProperty, false); ClearLabel.Text = "Clear";
    }
    private void Clear_OutsidePress(object sender, MouseButtonEventArgs e)
    { if (!IsWithin(e.OriginalSource as DependencyObject, ClearButton)) CancelClear(); }
    private static bool IsWithin(DependencyObject? child, DependencyObject parent)
    {
        for (; child is not null; child = child is Visual ? VisualTreeHelper.GetParent(child) : LogicalTreeHelper.GetParent(child))
            if (ReferenceEquals(child, parent)) return true;
        return false;
    }
    private void Previous_Click(object sender, RoutedEventArgs e) { e.Handled = true; Model.Previous(); }
    private void Next_Click(object sender, RoutedEventArgs e) { e.Handled = true; Model.Next(); }
    private void View_Click(object sender, RoutedEventArgs e) { e.Handled = true; CancelClear(); CancelFilePress(); Model.CycleView(); }
    private void Remove_Click(object sender, RoutedEventArgs e)
    { e.Handled = true; if (((FrameworkElement)sender).DataContext is ShelfFile file) Model.Remove(file); }
    private void Reveal_Click(object sender, RoutedEventArgs e) => Act(sender, e, FileActions.Reveal);
    private void Copy_Click(object sender, RoutedEventArgs e) => Act(sender, e, FileActions.CopyPath, false);
    private void Act(object sender, RoutedEventArgs e, Func<string, UtilityResult> action, bool requireExisting = true)
    {
        e.Handled = true;
        if (((FrameworkElement)sender).DataContext is ShelfFile file && (!requireExisting || Model.CheckAvailable(file))) Model.Status = action(file.Path).Message;
    }
    private void File_MouseDown(object sender, MouseButtonEventArgs e)
    {
        for (var source = e.OriginalSource as DependencyObject; source is not null && source != sender;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
            if (source is ButtonBase) return;
        e.Handled = true;
        if (((FrameworkElement)sender).DataContext is not ShelfFile file) return;
        if (e.ClickCount != 1) return;
        _dragStart = e.GetPosition(this); _dragFile = file;
        Mouse.Capture((IInputElement)sender);
    }
    private void File_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragFile is not { } file) return;
        var row = (FrameworkElement)sender; var point = e.GetPosition(row);
        var open = ReferenceEquals(row.DataContext, file) && new Rect(row.RenderSize).Contains(point);
        CancelFilePress(); e.Handled = true;
        if (open) OpenFile(file);
    }
    internal void OpenFile(ShelfFile file) { if (Model.CheckAvailable(file)) Model.Status = FileActions.Open(file.Path).Message; }
    private void File_LostCapture(object sender, MouseEventArgs e) => _dragFile = null;
    private void CancelFilePress()
    {
        _dragFile = null;
        if (Mouse.Captured is DependencyObject captured && IsWithin(captured, this)) Mouse.Capture(null);
    }
    private void File_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { _dragFile = null; return; }
        if (_dragFile is not { } file || IsInteracting) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        CancelFilePress(); e.Handled = true;
        if (!Model.CheckAvailable(file)) return;
        SetBusy(true);
        try { DragDrop.DoDragDrop(this, new DataObject(DataFormats.FileDrop, new[] { file.Path }), DragDropEffects.Copy); }
        catch (System.Runtime.InteropServices.COMException) { Model.Status = "Windows could not start this file drag."; }
        finally { SetBusy(false); }
    }
    public void Dispose() { CancelClear(); CancelFilePress(); Model.PropertyChanged -= Model_Changed; Model.Dispose(); }
}
