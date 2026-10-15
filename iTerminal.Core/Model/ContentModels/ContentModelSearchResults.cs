using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Search;
using Serilog;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// The <c>search://</c> drive: the hits of the last F7 search as a panel listing.
///
/// <para>Every row is the real <c>file://</c> URL of the file it stands for, named by its path
/// relative to where the search started. That is what makes the panel more than a list: F3 views a
/// hit, F4 edits it, F5 copies it to the other panel and F8 deletes it — each through the local
/// drive, chosen by the row's own URL — and Enter goes to the folder it is in with the cursor on
/// it. The <c>..</c> row goes back to the folder the search started in.</para>
///
/// <para>The panel follows a search that is still running: it relists whenever the service reports
/// new hits (throttled there to a few times a second). A listing also drops hits that no longer
/// exist, so a file deleted from here leaves the list without a new search.</para>
/// </summary>
public sealed class ContentModelSearchResults : ContentModelAsyncDrive, IDisposable
{
    private readonly IFileSearchService _searchService;
    private readonly IFileSystem _fileSystem;
    private bool _isDisposed;

    public ContentModelSearchResults(
        string name,
        ISettingsService? settingsService,
        IFileSearchService searchService,
        IFileSystem fileSystem,
        FolderHistoryService? folderHistoryService,
        IFrequentExecutionGuardService frequentExecutionGuardService,
        ILogger? logger = null)
        : base(name, settingsService, folderHistoryService, frequentExecutionGuardService, logger)
    {
        _searchService = searchService;
        _fileSystem = fileSystem;
        _searchService.ResultsChanged += OnResultsChanged;
    }

    protected override string Scheme => Container.SEARCH_RESULTS;

    public override ContainerType ContainerType => ContainerType.SearchResults;

    public override IFileSystem FileSystem => _fileSystem;

    /// <summary>Nothing can be created in a list of results; the files themselves are on the disk.</summary>
    public override bool IsReadOnly => true;

    private void OnResultsChanged(object? sender, EventArgs e)
    {
        if (!_isDisposed)
        {
            Refresh();
        }
    }

    protected override IEntry ParentEntry(Url url)
    {
        var start = _searchService.Query?.StartFolder;
        return string.IsNullOrEmpty(start)
            ? base.ParentEntry(url)
            : new VirtualDriveEntry(new Url(start.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar), "..", "Up");
    }

    protected override Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        var start = _searchService.Query?.StartFolder ?? string.Empty;
        List<IEntry> entries = [];
        foreach (var hit in _searchService.Hits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (hit.IsFolder ? !Directory.Exists(hit.Path) : !File.Exists(hit.Path))
            {
                continue;
            }

            var relative = start.Length > 0 ? Path.GetRelativePath(start, hit.Path) : hit.Path;
            var hitUrl = new Url(hit.IsFolder ? hit.Path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar : hit.Path);
            entries.Add(new VirtualDriveEntry(hitUrl, relative, hit.IsFolder ? Url.Folder : hit.Size.ToFileSize(), hit.Modified)
            {
                Size = hit.Size,
                Detail = hit.LineNumber > 0 ? $"Line {hit.LineNumber}: {hit.LineText}" : string.Empty,
            });
        }

        return Task.FromResult<IReadOnlyList<IEntry>>(entries);
    }

    protected override string DescribeLocation()
    {
        var query = _searchService.Query;
        if (query is null)
        {
            return "search: nothing searched yet — F7";
        }

        var what = query.NamePatterns.Trim().Length > 0 ? query.NamePatterns.Trim() : "*";
        var containing = query.SearchesContent ? $" containing \"{query.ContainingText}\"" : string.Empty;
        var running = _searchService.IsRunning ? $" — searching… {_searchService.Progress.FilesScanned:N0} files" : string.Empty;
        return $"search: {what}{containing} in {query.StartFolder}{running}";
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _searchService.ResultsChanged -= OnResultsChanged;
    }
}
