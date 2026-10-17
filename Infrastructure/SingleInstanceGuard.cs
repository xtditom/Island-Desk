namespace IslandDesk.Infrastructure;

// Shared by development and installed copies. Setup checks the same session-local
// mutex before installing/uninstalling; keep this identity stable across updates.
internal sealed class SingleInstanceGuard : IDisposable
{
    internal const string MutexName = @"Local\IslandDesk.9BBE998F-0584-4868-AFE8-2ED902A95A75";
    private readonly Mutex _mutex;

    private SingleInstanceGuard(Mutex mutex) => _mutex = mutex;

    internal static SingleInstanceGuard? TryAcquire(string name = MutexName)
    {
        // Existence is the guard; no thread ownership, waits, polling, or focus changes.
        var mutex = new Mutex(initiallyOwned: false, name, out var createdNew);
        if (createdNew) return new(mutex);
        mutex.Dispose();
        return null;
    }

    public void Dispose() => _mutex.Dispose();
}
