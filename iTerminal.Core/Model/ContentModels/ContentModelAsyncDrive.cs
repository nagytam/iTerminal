using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using Serilog;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// Shared behaviour of the drives whose listing comes from somewhere slow — a <c>docker exec</c>, a
/// <c>git ls-tree</c>, a file-system walk that has already finished but is not this model's to own.
///
/// <para>Listing runs on a background task, never in <see cref="SetUrl"/>: a process spawn alone
/// can eat the whole 200 ms key-handler budget, and a stopped Docker daemon takes seconds to say
/// so. <see cref="SetUrl"/> puts the <c>..</c> row up at once so the panel is never empty and the
/// way out is always there, then the listing replaces it when it lands. Every result is checked
/// against the URL it was asked for and a generation counter before it is written, the same
/// stale-result rule <c>FilePanelRefreshTask</c> follows, so a slow listing for a folder the user
/// has already left cannot repaint the panel with it.</para>
///
/// <para>The way out of the drive's root is the folder the user came from: the panel records it
/// with <see cref="RememberReturnUrl"/> whenever it crosses into one of these drives, and the root's
/// <c>..</c> row leads there. Without it the only way back to the disk would be the folder selector.</para>
/// </summary>
public abstract class ContentModelAsyncDrive : ContentModelBase, IFileSystemProvider
{
    private readonly ISettingsService _settingsService;
    private readonly FolderHistoryService? _folderHistoryService;
    private readonly ILogger? _logger;
    private readonly Lock _lock = new();

    private int _generation;
    private CancellationTokenSource? _listingCancellation;
    private volatile bool _isLoading;

    protected ContentModelAsyncDrive(
        string name,
        ISettingsService? settingsService,
        FolderHistoryService? folderHistoryService,
        IFrequentExecutionGuardService frequentExecutionGuardService,
        ILogger? logger)
        : base(name, frequentExecutionGuardService)
    {
        _settingsService = settingsService ?? NullSettingsService.Instance;
        _folderHistoryService = folderHistoryService;
        _logger = logger;
    }

    /// <summary>The scheme this drive lives under, e.g. <c>docker://</c>.</summary>
    protected abstract string Scheme { get; }

    public abstract ContainerType ContainerType { get; }

    /// <summary>The service behind every file operation on this drive.</summary>
    public abstract IFileSystem FileSystem { get; }

    /// <summary>Lists <paramref name="url"/> without its <c>..</c> row; the base adds that.</summary>
    protected abstract Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken);

    /// <summary>The panel title for the current location, before it is shrunk to the border.</summary>
    protected abstract string DescribeLocation();

    public override bool IsReadOnly => FileSystem.IsReadOnly;

    /// <summary>Why the last listing is not what the user asked for, or empty.</summary>
    public string StatusMessage { get; protected set; } = string.Empty;

    /// <summary>True from a navigation until its listing has landed.</summary>
    public bool IsLoading => _isLoading;

    /// <summary>The listing started last. Nothing waits on it; it exists so a test can.</summary>
    public Task ListingTask { get; private set; } = Task.CompletedTask;

    #region Return URL

    private static string ReturnUrlKey(string panelName) => panelName + ".ReturnUrl";

    /// <summary>Whether <paramref name="containerType"/> is one of the drives built on this class.</summary>
    public static bool IsAsyncDrive(ContainerType containerType) =>
        containerType is ContainerType.Docker or ContainerType.Git
            or ContainerType.SearchResults or ContainerType.Preview or ContainerType.Archive
            or ContainerType.Recycle or ContainerType.Mtp
            or ContainerType.S3 or ContainerType.OneDrive or ContainerType.GoogleDrive;

    /// <summary>
    /// Records where <paramref name="panelName"/> was when it crossed into one of these drives, so
    /// the drive's root can offer the way back — whatever took it there: a hot key, the folder
    /// selector, a typed URL. <c>ContentModelService</c> calls this on every change of drive kind.
    /// Only a location outside these drives is worth remembering: going from the git drive to the
    /// search results should still lead back to the disk, not to a commit.
    /// </summary>
    public static void RememberReturnUrl(ISettingsService settingsService, string panelName, Url? current, Url destination)
    {
        if (current is null || !IsAsyncDrive(destination.ContainerType) || IsAsyncDrive(current.ContainerType))
        {
            return;
        }

        settingsService.Set(ReturnUrlKey(panelName), current.FullUrl);
    }

    /// <summary>Where <paramref name="panelName"/> goes back to from one of these drives: the remembered folder, or the user's home.</summary>
    public static Url GetReturnUrl(ISettingsService settingsService, string panelName)
    {
        var stored = settingsService.Get(ReturnUrlKey(panelName));
        return string.IsNullOrWhiteSpace(stored)
            ? new Url(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + Path.DirectorySeparatorChar)
            : new Url(stored);
    }

    /// <summary>Where the root's <c>..</c> row leads. See <see cref="GetReturnUrl"/>.</summary>
    public Url ReturnUrl => GetReturnUrl(_settingsService, Name);

    #endregion

    #region Navigation

    public override void ReadUrl()
    {
        var stored = _settingsService.Get(Name);
        SetUrl(new Url(string.IsNullOrWhiteSpace(stored) || !stored.StartsWith(Scheme, StringComparison.Ordinal) ? Scheme : stored));
    }

    public override void SetUrl(Url url)
    {
        _settingsService.Set(Name, url);
        _folderHistoryService?.Add(url);
        StatusMessage = string.Empty;
        Filter = null;

        Url = url;
        ResetNavigation();
        Entries.ReplaceAll([ParentEntry(url)]);
        Refresh();
    }

    /// <summary>
    /// The <c>..</c> row: the parent inside the drive, or — at the drive's root, where there is no
    /// parent — the folder the drive was opened from.
    /// </summary>
    protected virtual IEntry ParentEntry(Url url) =>
        new VirtualDriveEntry(IsDriveRoot(url) ? ReturnUrl : url.ParentUrl, "..", "Up");

    /// <summary>Whether <paramref name="url"/> is the top of this drive, where <c>..</c> leaves it.</summary>
    protected virtual bool IsDriveRoot(Url url) => url.Path.Trim('/').Length == 0;

    public override void Refresh(bool forceRefresh = false)
    {
        var url = Url;
        if (url is null)
        {
            return;
        }

        int generation;
        CancellationTokenSource cancellation = new();
        lock (_lock)
        {
            generation = ++_generation;
            _listingCancellation?.Cancel();
            _listingCancellation = cancellation;
        }

        _isLoading = true;
        Update();

        ListingTask = Task.Run(async () =>
        {
            try
            {
                var entries = await ListAsync(url, cancellation.Token);
                if (!IsCurrent(generation, url))
                {
                    return;
                }

                // A refresh of the folder already on screen keeps the cursor on its row; a new
                // folder starts on ".." (or on whatever RestoreNavigation is holding for it).
                StatusMessage = string.Empty;
                ReplaceEntriesAndRestoreFocus([ParentEntry(url), .. entries], isComplete: true);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer navigation, which owns the panel now.
            }
            catch (Exception exception)
            {
                if (IsCurrent(generation, url))
                {
                    StatusMessage = exception.Message;
                }

                _logger?.Warning(exception, "Listing {Url} failed", url.FullUrl);
            }
            finally
            {
                if (IsCurrent(generation, url))
                {
                    _isLoading = false;
                    Update();
                }
            }
        });
    }

    private bool IsCurrent(int generation, Url url) =>
        generation == Volatile.Read(ref _generation)
        && string.Equals(Url?.FullUrl, url.FullUrl, StringComparison.Ordinal);

    #endregion

    #region Chrome

    public override string GetHeader(int width)
    {
        var header = DescribeLocation() + (_isLoading ? " (reading…)" : string.Empty);
        return header.Shrink(width) ?? string.Empty;
    }

    public override string GetFooter()
    {
        var footer = EntryCountFooter();
        return StatusMessage.Length > 0 ? $"{footer}│ {StatusMessage} " : footer;
    }

    #endregion

    #region File system: answered from the listing, done by the service

    /// <summary>
    /// Answered from what is listed, which is what every caller is asking about — the F3, F5 and F8
    /// gates check the row under the cursor. Asking the tool instead would put a process spawn on
    /// the key handler.
    /// </summary>
    public override bool ExistsFile(Url url) => FindListed(url) is { PathType: PathType.File };

    public override bool ExistsFolder(Url url) =>
        IsSameLocation(url.FullUrl, Url?.FullUrl) || FindListed(url) is { PathType: PathType.Folder };

    private Url? FindListed(Url url)
    {
        foreach (var entry in Entries.AsSnapshot())
        {
            if (entry.DisplayName != ".." && IsSameLocation(entry.Url.FullUrl, url.FullUrl))
            {
                return entry.Url;
            }
        }

        return null;
    }

    public override Task<IMemoryFile?> ReadFileAsync(Url url) => FileSystem.ReadFileAsync(url);

    public override Task WriteFileAsync(Url url, MemoryStream inputStream) => FileSystem.WriteFileAsync(url, inputStream);

    public override Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken) =>
        FileSystem.StreamToAsync(url, destination, progress, cancellationToken);

    public override Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken) =>
        FileSystem.StreamFromAsync(url, source, progress, cancellationToken);

    public override Task CreateFolderAsync(Url url) => FileSystem.CreateFolderAsync(url);

    public override bool DeleteFile(Url url, bool moveToRecycleBin) => FileSystem.DeleteFile(url, moveToRecycleBin);

    public override bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => FileSystem.DeleteFolder(url, recursive, moveToRecycleBin);

    public override void Delete(Url url, bool recursive, bool moveToRecycleBin)
    {
        if (url.PathType == PathType.Folder)
        {
            FileSystem.DeleteFolder(url, recursive, moveToRecycleBin);
        }
        else
        {
            FileSystem.DeleteFile(url, moveToRecycleBin);
        }

        Refresh(forceRefresh: true);
    }

    public override Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) => FileSystem.MoveAsync(sourceUrl, targetUrl, overwrite);

    public override Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) => FileSystem.CopyAsync(sourceUrl, targetUrl, overwrite);

    public override string GetAvailableDiskSpace() => string.Empty;

    public override Task RunTerminalCommandAsync(ITerminalModel terminalModel) => Task.CompletedTask;

    #endregion
}
