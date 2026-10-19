using System.Collections.ObjectModel;
using System.IO;
using IslandDesk.Infrastructure;

namespace IslandDesk.Presentation;

internal sealed record ShelfFile(string Path, string Name, string Extension, long Bytes)
{
    public string SizeText => FormatSize(Bytes);
    public string Details => $"{(Extension.Length == 0 ? "FILE" : Extension.TrimStart('.').ToUpperInvariant())} · {FormatSize(Bytes)}";
    private static string FormatSize(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1048576 ? $"{bytes / 1024d:0.#} KB" : bytes < 1073741824 ? $"{bytes / 1048576d:0.#} MB" : $"{bytes / 1073741824d:0.#} GB";
}
internal enum ShelfViewStyle { Grid, Line, TwoColumn }

// Session-only references. File contents are never read, copied, watched or persisted.
internal sealed class FileShelfViewModel : ObservableObject, IDisposable
{
    internal const int MaximumEntries = 50;
    private ShelfViewStyle _viewStyle;
    public ShelfViewStyle ViewStyle => _viewStyle;
    public string ViewDescription => ViewStyle switch
    {
        ShelfViewStyle.Grid => "Grid view · Switch to Line view",
        ShelfViewStyle.Line => "Line view · Switch to Two-Column view",
        _ => "Two-Column view · Switch to Grid view"
    };
    public int PageSize => ViewStyle == ShelfViewStyle.Grid ? 6 : 4;
    private readonly List<ShelfFile> _files = [];
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _addition;
    private int _page;
    private bool _disposed, _busy;
    private string _status = "Drop files here or choose Add Files. Originals stay in place.";
    public ObservableCollection<ShelfFile> PageEntries { get; } = [];
    public int Count => _files.Count;
    public int PageCount => Math.Max(1, (Count + PageSize - 1) / PageSize);
    public string PageLabel => Count == 0 ? "Empty shelf" : $"{_page + 1} / {PageCount} · {Count} files";
    public bool HasFiles => Count > 0;
    public bool CanPrevious => _page > 0;
    public bool CanNext => _page + 1 < PageCount;
    public bool IsBusy { get => _busy; private set { if (_busy == value) return; _busy = value; Changed(); Changed(nameof(CanAdd)); } }
    public bool CanAdd => !IsBusy && Count < MaximumEntries;
    public string Status { get => _status; set { if (_status == value) return; _status = value; Changed(); } }

    public async Task AddAsync(IReadOnlyList<string> paths)
    {
        if (_disposed) return;
        if (IsBusy) { Status = "Please wait for the current files to finish adding."; return; }
        if (Count >= MaximumEntries) { Status = $"The shelf holds up to {MaximumEntries} files. Remove a reference to add another."; return; }
        IsBusy = true; Status = "Adding files…";
        using var addition = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _addition = addition;
        var token = addition.Token;
        var known = _files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var available = MaximumEntries - Count;
        try
        {
            var result = await Task.Run(() => Inspect(paths, known, available, token), token);
            if (token.IsCancellationRequested || _disposed) return;
            _files.AddRange(result.Files); Refresh();
            Status = $"Added {result.Files.Count}." + (result.Duplicates > 0 ? $" {result.Duplicates} already on the shelf." : "") +
                (result.Invalid > 0 ? $" {result.Invalid} unavailable or unsupported." : "") +
                (result.Excess > 0 ? $" Shelf limit: {MaximumEntries} files." : "");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { _addition = null; if (!_disposed) IsBusy = false; }
    }
    private sealed record Added(List<ShelfFile> Files, int Duplicates, int Invalid, int Excess);
    internal static bool IsSupportedLocalFile(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && System.IO.Path.IsPathFullyQualified(path) &&
                !path.StartsWith(@"\\", StringComparison.Ordinal) &&
                new DriveInfo(System.IO.Path.GetPathRoot(path)!).DriveType != DriveType.Network && File.Exists(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { return false; }
    }
    private static Added Inspect(IReadOnlyList<string> paths, HashSet<string> known, int capacity, CancellationToken token)
    {
        var files = new List<ShelfFile>(); var duplicates = 0; var invalid = 0; var excess = 0;
        for (var i = 0; i < paths.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            if (files.Count >= capacity) { excess = paths.Count - i; break; }
            try
            {
                var path = paths[i];
                if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) { invalid++; continue; }
                path = System.IO.Path.GetFullPath(path);
                if (known.Contains(path)) { duplicates++; continue; }
                if (new DriveInfo(System.IO.Path.GetPathRoot(path)!).DriveType == DriveType.Network) { invalid++; continue; }
                var info = new FileInfo(path);
                if (!info.Exists || (info.Attributes & FileAttributes.Directory) != 0) { invalid++; continue; }
                // Metadata only: avoid thumbnails and file-content hydration.
                var entry = new ShelfFile(info.FullName, info.Name, info.Extension, info.Length);
                files.Add(entry); known.Add(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
            { invalid++; }
        }
        return new(files, duplicates, invalid, excess);
    }
    public void Previous() { if (CanPrevious) { _page--; Refresh(); } }
    public void Next() { if (CanNext) { _page++; Refresh(); } }
    public void CycleView()
    {
        var anchor = _page * PageSize;
        _viewStyle = (ShelfViewStyle)(((int)_viewStyle + 1) % 3);
        _page = anchor / PageSize; Refresh();
    }
    public void Remove(ShelfFile file) { if (_files.Remove(file)) { Refresh(); Status = "Reference removed. Original file kept."; } }
    public void Clear() { _addition?.Cancel(); _files.Clear(); _page = 0; Refresh(); Status = "Shelf cleared. Original files kept."; }
    public bool CheckAvailable(ShelfFile file)
    {
        if (_disposed || !_files.Contains(file)) return false;
        if (File.Exists(file.Path)) return true;
        Status = "This file is missing or inaccessible. Remove its reference or add its new location."; return false;
    }
    private void Refresh()
    {
        _page = Math.Clamp(_page, 0, PageCount - 1);
        var page = _files.Skip(_page * PageSize).Take(PageSize).ToArray();
        for (var i = 0; i < page.Length; i++)
        {
            if (i >= PageEntries.Count) PageEntries.Add(page[i]);
            else if (!ReferenceEquals(PageEntries[i], page[i])) PageEntries[i] = page[i];
        }
        while (PageEntries.Count > page.Length) PageEntries.RemoveAt(PageEntries.Count - 1);
        Changed(null);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); _files.Clear(); PageEntries.Clear();
    }
}
