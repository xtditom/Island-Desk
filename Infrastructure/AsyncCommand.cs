using System.Windows.Input;

namespace IslandDesk.Infrastructure;

internal sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        Refresh();
        try { await execute(); }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Control action failed: {0}", error.Message); }
        finally { _running = false; Refresh(); }
    }
}
