using iTerminal.Core.Services.BackgroundStart;
using iConsole.Localization;
using iTerminal.Core.Data;
using iTerminal.Core.Data.Documents;
using iTerminal.Core.Documents;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Services.Documents;

/// <summary>
/// SQLite-backed document index. Three stores, deliberately: the metadata table
/// (<see cref="DocumentEntry"/>) which is mirrored in memory and is what lists and counts;
/// the content table (<see cref="DocumentContentEntry"/>) which is read one row at a time for the
/// preview; and the FTS5 index (<see cref="DocumentSearchIndex"/>) which is what search actually
/// runs against.
///
/// <para>The music library keeps everything in memory and searches there. That cannot work here and
/// the difference is worth naming: a music tag is tens of bytes and a document's text is tens of
/// kilobytes, so the same design over a real documents folder would hold hundreds of megabytes of
/// prose in the heap to answer a query SQLite answers from an index. What is kept in memory is
/// exactly what the UI enumerates — the metadata — and nothing else.</para>
///
/// <para>A rescan is cheap when nothing changed: each file's size and last-write time are compared
/// against the cached row, and only a file that moved is re-extracted. That matters far more than
/// it does for music. Re-reading every tag in a music folder is a second; re-extracting every PDF
/// and re-OCR'ing every scan in a documents folder is minutes, and it would happen every quarter of
/// an hour.</para>
/// </summary>
public sealed class DocumentLibraryService : IDocumentLibraryService
{
    private readonly IBackgroundStartService? _backgroundStart;

    /// <summary>Asks for the scan: it starts now, or as the document library is configured to (BackgroundStart.DocumentLibrary).</summary>
    public void RequestIndexing() => _backgroundStart?.Request(BackgroundServices.DocumentLibrary);

    /// <summary>
    /// How deep the folder walk goes. A documents folder is nested more deeply than a music folder —
    /// project trees, mail exports, years of scans — so the budget is larger, but it is still a
    /// budget: a symlink loop is otherwise an unbounded walk.
    /// </summary>
    private const int MaxScanDepth = 16;

    /// <summary>
    /// The default when no folder is configured. Kept as the literal the user asked for; on Windows
    /// it is anchored to a drive by <see cref="ResolveDocumentsFolder"/>.
    /// </summary>
    public const string DefaultDocumentsFolder = @"\users\tamas\nata\documents";

    private readonly IDbContextFactory<iTerminalDbContext> _dbContextFactory;
    private readonly ISettingsService _settingsService;
    private readonly DocumentTextExtractorFactory _extractorFactory;
    private readonly DocumentSearchIndex _searchIndex = new();
    private readonly Serilog.ILogger? _logger;
    private readonly IOcrEngine? _ocrEngine;

    private readonly Lock _lock = new();

    /// <summary>
    /// Held for the whole of the initial table read, so a second caller waits for the rows instead
    /// of walking past an empty list — which is what a flag flipped before the query would give it.
    /// </summary>
    private readonly Lock _loadLock = new();

    private readonly SemaphoreSlim _scanGate = new(1, 1);

    private readonly IDocumentEmbedder _embedder;
    private readonly DocumentVectorIndex _vectorIndex = new();

    /// <summary>One embedding run at a time. A run that finds it taken simply does not start.</summary>
    private readonly SemaphoreSlim _embedGate = new(1, 1);

    // What the meaning index reports for SemanticMessage. Written by the embedding run, read from the render path.
    private volatile bool _semanticRunning;
    private volatile int _semanticDone;
    private volatile int _semanticTotal;
    private volatile int _semanticIndexed;
    private volatile string? _semanticFailure;

    /// <summary>
    /// Serializes the database writes of a scan and of an embedding save. SQLite answers a second writer that is already
    /// in a transaction with a busy error at once, so a save that collided with a scan would end the embedding run.
    /// </summary>
    private readonly Lock _writeLock = new();

    private readonly Lock _queryCacheLock = new();
    private QueryVector? _lastQuery;

    private sealed record QueryVector(string Query, string Model, float[] Vector);

    /// <summary>A document's change stamp: the size and modified time its text was extracted from.</summary>
    private sealed record DocumentStamp(long Size, DateTimeOffset ModifiedUtc);

    private sealed record DocumentSnapshot(DocumentStamp Stamp, string Text);

    private List<DocumentEntry> _entries = [];
    private bool _isLoaded;
    private DateTimeOffset _lastLoadAttemptUtc = DateTimeOffset.MinValue;

    /// <summary>Reference assignment is atomic and the snapshot is immutable, so readers need no lock.</summary>
    private volatile DocumentIndexingProgress _progress = DocumentIndexingProgress.Idle;

    private DateTimeOffset _lastProgressUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// Every dependency is optional so a test — or a run with no database at all — gets a throwaway
    /// SQLite file and the default extractors instead of having to fake them, the same bargain
    /// <see cref="Music.MusicLibraryService"/> makes.
    /// </summary>
    public DocumentLibraryService(
        IDbContextFactory<iTerminalDbContext>? dbContextFactory,
        ISettingsService? settingsService,
        IOcrEngine? ocrEngine = null,
        DocumentTextExtractorFactory? extractorFactory = null,
        Serilog.ILogger? logger = null,
        IBackgroundStartService? backgroundStart = null,
        IDocumentEmbedder? documentEmbedder = null)
    {
        _backgroundStart = backgroundStart;
        _dbContextFactory = dbContextFactory ?? new TemporaryDbContextFactory("DocumentLibrary");
        _settingsService = settingsService ?? NullSettingsService.Instance;
        _ocrEngine = ocrEngine;
        _extractorFactory = extractorFactory ?? new DocumentTextExtractorFactory(ocrEngine);
        _logger = logger;
        _embedder = documentEmbedder ?? NullDocumentEmbedder.Instance;
    }

    public bool SemanticSearchAvailable => _embedder.IsConfigured;

    public string SemanticMessage
    {
        get
        {
            if (!_embedder.IsConfigured)
            {
                return UiText.Get("Docs.SemanticOff", "Meaning search is off: set AI:EmbeddingModelId to an embedding model.");
            }

            if (_semanticRunning)
            {
                return string.Format(UiText.Get("Docs.SemanticProgress", "Building the meaning index: {0} of {1} documents."), _semanticDone, _semanticTotal);
            }

            string? failure = _semanticFailure;
            if (failure is not null)
            {
                return string.Format(UiText.Get("Docs.SemanticFailed", "Meaning index failed: {0}"), failure);
            }

            int indexed = _semanticIndexed;
            return indexed == 0
                ? UiText.Get("Docs.SemanticNotBuilt", "Meaning index not built yet: it is built in the background after a scan.")
                : string.Format(UiText.Get("Docs.SemanticReady", "Meaning index: {0} documents, embedded with {1}."), indexed, _embedder.ModelId);
        }
    }

    public event EventHandler? LibraryChanged;

    public event EventHandler<DocumentIndexingProgress>? IndexingProgressChanged;

    /// <summary>
    /// How often progress is published while a stage runs. Faster than the music library's, because
    /// a document scan spends whole seconds on single files and the current-file line is the only
    /// sign it is still moving.
    /// </summary>
    public TimeSpan ProgressReportInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How long a running scan may hold freshly extracted documents before writing them. Shorter
    /// than the music library's for the same reason the report interval is: fewer, slower files, so
    /// a batch takes longer to fill and would otherwise sit unwritten.
    /// </summary>
    public TimeSpan ScanFlushInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How many freshly extracted documents force a write regardless of
    /// <see cref="ScanFlushInterval"/>. Smaller than the music library's batch because each row
    /// carries a document's whole text rather than three short tag strings.
    /// </summary>
    public int ScanFlushBatchSize { get; init; } = 50;

    /// <summary>
    /// How long a failed table read is left alone before another attempt. Without a pause, a
    /// database that cannot be opened would be re-queried on every repaint of the dialog.
    /// </summary>
    public TimeSpan LoadRetryInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Derived from <see cref="Progress"/> so there is one piece of state, not two.</summary>
    public bool IsIndexing => _progress.IsRunning;

    public DocumentIndexingProgress Progress => _progress;

    public DateTimeOffset? LastIndexedUtc { get; private set; }

    public string StatusMessage { get; private set; } = string.Empty;

    public string DocumentsFolder => ResolveDocumentsFolder(_settingsService);

    public string CapabilityMessage
    {
        get
        {
            string search = _searchIndex.IsAvailable ? iConsole.Localization.UiText.Get("Documents.SearchFts", "full-text index") : iConsole.Localization.UiText.Get("Documents.SearchScan", "scan (no FTS5)");

            // The engine names its own state — "off", "not installed", or its version — because
            // those are three different things to a user and only one of them is a problem.
            string ocr = _ocrEngine?.DisplayName ?? iConsole.Localization.UiText.Get("Documents.NoOcr", "no OCR engine");
            return string.Format(iConsole.Localization.UiText.Get("Documents.Capability", "Search: {0} · Images: {1}"), search, ocr);
        }
    }

    /// <summary>
    /// The folder the indexer walks: the <c>DocumentsFolder</c> setting when it is set, otherwise
    /// the built-in default. Shared with the settings page so both show the same default.
    ///
    /// <para>On Windows the default is anchored to the system drive. A path that starts with a
    /// separator but names no drive is resolved against whichever drive the process happens to be
    /// running on, so left as written it would name a different folder depending on where the app
    /// was launched from.</para>
    /// </summary>
    public static string ResolveDocumentsFolder(ISettingsService? settingsService)
    {
        string? configured = settingsService?.Get(Settings.DocumentsFolder);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        if (OperatingSystem.IsLinux())
        {
            return "/home/tamas/nata/documents";
        }

        string root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? @"C:\";
        return Path.Combine(root, DefaultDocumentsFolder.TrimStart('\\', '/'));
    }

    public int DocumentCount
    {
        get
        {
            EnsureLoaded();
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    public int IndexedTextCount
    {
        get
        {
            EnsureLoaded();
            lock (_lock)
            {
                return _entries.Count(entry => entry.TextLength > 0);
            }
        }
    }

    public IReadOnlyList<DocumentEntry> Entries
    {
        get
        {
            EnsureLoaded();
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public void Preload() => EnsureLoaded();

    #region Scanning

    public async Task<int> RefreshAsync(CancellationToken cancellationToken = default)
    {
        int count = await ScanAsync(cancellationToken);

        // The meaning index follows the text. It runs after the scan has let go of its gate, so a slow model never holds up
        // the next scan, and it stops with the same token.
        await EmbedPendingAsync(cancellationToken);
        return count;
    }

    private async Task<int> ScanAsync(CancellationToken cancellationToken)
    {
        // One scan at a time: the periodic job and a user-triggered rescan can arrive together.
        if (!await _scanGate.WaitAsync(0, cancellationToken))
        {
            return DocumentCount;
        }

        ReportProgress(DocumentIndexingPhase.Discovering, 0, 0, string.Empty, force: true);

        try
        {
            EnsureLoaded();

            string folder = DocumentsFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                StatusMessage = string.IsNullOrWhiteSpace(folder)
                    ? UiText.Get("Docs.NoFolder", "No documents folder is configured.")
                    : string.Format(UiText.Get("Docs.FolderMissing", "Documents folder not found: {0}"), folder);
                return DocumentCount;
            }

            // Stage one: find out what there is. Until this returns the only honest thing to show is
            // a count that keeps rising, which is why it is reported as its own phase.
            List<string> files = [];
            CollectFiles(
                folder,
                MaxScanDepth,
                files,
                cancellationToken,
                () => ReportProgress(DocumentIndexingPhase.Discovering, files.Count, 0, string.Empty, force: false));

            // Stage two: discovery is done, so the total is final and the UI can switch from a
            // spinner to "n of m". Always published — this transition is the point of the split.
            ReportProgress(DocumentIndexingPhase.Indexing, files.Count, 0, string.Empty, force: true);

            return await Task.Run(() => Index(folder, files, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            StatusMessage = string.Format(UiText.Get("Msg.DocIndexFailed", "Document indexing failed: {0}"), exception.Message);
            _logger?.Warning(exception, "Document library indexing failed.");
            return DocumentCount;
        }
        finally
        {
            // Idle first, then the gate: releasing it earlier would let the next scan announce its
            // own first stage only to have this line stamp Idle over it.
            ReportProgress(DocumentIndexingPhase.Idle, 0, 0, string.Empty, force: true);
            _scanGate.Release();
        }
    }

    /// <summary>
    /// The indexing stage: extract what changed, keep what did not, and drop what is gone.
    /// Synchronous, and run on a pool thread by <see cref="RefreshAsync"/> — extraction is CPU and
    /// disk work with nothing to await, so an async signature here would only add ceremony.
    /// </summary>
    private int Index(string folder, List<string> files, CancellationToken cancellationToken)
    {
        Dictionary<string, DocumentEntry> known;
        lock (_lock)
        {
            known = _entries.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        }

        // What readers see while the scan runs. It starts as the cached library and gains each file
        // as that file is read, so the list grows during a first scan and never loses a row during a
        // rescan — rows whose file is gone are only dropped once the walk is over and the scan
        // actually knows they are gone.
        Dictionary<string, DocumentEntry> published = new(known, StringComparer.OrdinalIgnoreCase);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<DocumentEntry> scanned = new(files.Count);

        // Extracted but not yet written. Emptied by each flush; whatever is left at the end goes out
        // with the removals.
        List<(DocumentEntry Entry, string Text)> pending = [];
        int changedCount = 0;
        DateTimeOffset lastFlushUtc = DateTimeOffset.UtcNow;

        int processed = 0;
        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReportProgress(
                DocumentIndexingPhase.Indexing,
                files.Count,
                processed++,
                Path.GetFileName(file),
                force: false);

            FileInfo info;
            try
            {
                info = new FileInfo(file);
                if (!info.Exists)
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            if (known.TryGetValue(file, out DocumentEntry? cached)
                && cached.Size == info.Length
                && cached.ModifiedUtc == new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero))
            {
                scanned.Add(cached);
                continue;
            }

            (DocumentEntry entry, string text) = ReadEntry(folder, file, info, now, cancellationToken);
            scanned.Add(entry);
            pending.Add((entry, text));
            published[entry.Path] = entry;
            changedCount++;

            if (pending.Count >= ScanFlushBatchSize
                || DateTimeOffset.UtcNow - lastFlushUtc >= ScanFlushInterval)
            {
                FlushScan(pending, published);
                lastFlushUtc = DateTimeOffset.UtcNow;
            }
        }

        // Every file is accounted for; what is left is the database write, which the counts cannot
        // describe. Reported so a watcher sees the run reach its total rather than stop one short.
        ReportProgress(DocumentIndexingPhase.Indexing, files.Count, files.Count, string.Empty, force: true);

        HashSet<string> live = new(scanned.Select(entry => entry.Path), StringComparer.OrdinalIgnoreCase);
        List<string> removed = [.. known.Keys.Where(path => !live.Contains(path))];

        scanned.Sort(CompareEntries);

        lock (_lock)
        {
            _entries = scanned;
        }

        // The last batch and the deletions in one write: a row is only dropped once the whole folder
        // has been walked, so an interrupted scan can never take live documents with it.
        Persist(pending, removed);

        LastIndexedUtc = now;
        int withText = scanned.Count(entry => entry.TextLength > 0);
        StatusMessage =
            scanned.Count == 1
            ? string.Format(UiText.Get("Docs.IndexedOne", "Indexed 1 document ({0} with text) from {1}."), withText, folder)
            : string.Format(UiText.Get("Docs.IndexedMany", "Indexed {0} documents ({1} with text) from {2}."), scanned.Count, withText, folder);

        if (changedCount > 0 || removed.Count > 0)
        {
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }

        return scanned.Count;
    }

    /// <summary>
    /// Indexes one file: its metadata always, its text when an extractor claims it and the file is
    /// small enough to be worth opening.
    /// </summary>
    private (DocumentEntry Entry, string Text) ReadEntry(
        string root,
        string path,
        FileInfo info,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        DocumentEntry entry = new()
        {
            Path = path,
            FileName = Path.GetFileName(path),
            RelativeFolder = GetRelativeFolder(root, path),
            Extension = Path.GetExtension(path).ToLowerInvariant(),
            Kind = DocumentTextExtractorFactory.GetKind(path),
            Size = info.Length,
            ModifiedUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            IndexedUtc = now,
        };

        IDocumentTextExtractor? extractor = _extractorFactory.GetExtractor(path);
        if (extractor is null)
        {
            entry.ExtractionStatus = DocumentExtractionStatus.Unsupported;
            return (entry, string.Empty);
        }

        if (info.Length > DocumentTextLimits.MaxFileSize)
        {
            entry.ExtractionStatus = DocumentExtractionStatus.TooLarge;
            entry.ExtractionNote = $"{info.Length / (1024 * 1024)} MB is over the extraction limit";
            return (entry, string.Empty);
        }

        DocumentTextResult result;
        try
        {
            result = extractor.Extract(path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The extractors promise not to throw, but a scan of ten thousand files is not the
            // place to find out one of them broke that promise.
            _logger?.Warning(exception, "Extracting text from {Path} failed.", path);
            result = DocumentTextResult.None(DocumentExtractionStatus.Failed, exception.Message);
        }

        entry.ExtractionStatus = result.Status;
        entry.ExtractionNote = result.Note;
        entry.Title = result.Title;
        entry.Author = result.Author;
        entry.PageCount = result.PageCount;
        entry.TextLength = result.Text.Length;
        entry.WordCount = DocumentTextLimits.CountWords(result.Text);

        return (entry, result.Text);
    }

    /// <summary>
    /// The file's folder relative to the indexed root, which is what the results list shows: an
    /// absolute path is mostly the same prefix repeated on every row.
    /// </summary>
    internal static string GetRelativeFolder(string root, string path)
    {
        try
        {
            string? folder = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(folder))
            {
                return string.Empty;
            }

            string relative = Path.GetRelativePath(root, folder);
            return relative == "." ? string.Empty : relative;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Writes what the scan has extracted so far and makes it the snapshot every reader sees. Both
    /// halves in one place: the database gets the content, and the dialog — which rebuilds from
    /// <see cref="Entries"/> on each progress report — draws it on its next repaint without needing
    /// the scan to finish first.
    /// </summary>
    private void FlushScan(
        List<(DocumentEntry Entry, string Text)> pending,
        Dictionary<string, DocumentEntry> published)
    {
        if (pending.Count == 0)
        {
            return;
        }

        Persist(pending, []);
        pending.Clear();

        List<DocumentEntry> snapshot = [.. published.Values];
        snapshot.Sort(CompareEntries);

        lock (_lock)
        {
            _entries = snapshot;
        }
    }

    /// <summary>
    /// Records the current stage and counts, and publishes them unless the last publication was too
    /// recent. Updating the snapshot is unconditional — only the event is rate-limited — so a reader
    /// that polls <see cref="Progress"/> always sees the latest counts.
    /// </summary>
    private void ReportProgress(
        DocumentIndexingPhase phase,
        int discovered,
        int indexed,
        string currentFile,
        bool force)
    {
        DocumentIndexingProgress progress = new(phase, discovered, indexed, currentFile);
        _progress = progress;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (!force && now - _lastProgressUtc < ProgressReportInterval)
        {
            return;
        }

        _lastProgressUtc = now;

        try
        {
            IndexingProgressChanged?.Invoke(this, progress);
        }
        catch (Exception exception)
        {
            // A subscriber that throws is its own problem; it must not abort the scan.
            _logger?.Warning(exception, "A document indexing progress handler failed.");
        }
    }

    /// <summary>
    /// Enumeration that does not give up on a folder because one entry in it is unreadable — the
    /// same reasoning as the music indexer's, and more pressing here: a documents folder routinely
    /// contains folders the user cannot read.
    /// </summary>
    private static readonly EnumerationOptions DiscoveryOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
    };

    /// <summary>
    /// Folder names that are never worth walking: build output and version-control internals. A
    /// documents folder that happens to contain a checked-out repository would otherwise spend the
    /// scan indexing object files.
    /// </summary>
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "bin", "obj", ".vs", "__pycache__", ".venv",
    };

    /// <summary>
    /// The discovery stage. Unlike the music scan this counts every file, not only the ones an
    /// extractor claims: an unsupported file is still listed, still searchable by name, and still
    /// part of what the user sees in the folder, so hiding it from the denominator would make the
    /// count disagree with the folder.
    /// </summary>
    private static void CollectFiles(
        string folder,
        int depthBudget,
        List<string> files,
        CancellationToken cancellationToken,
        Action? onFolderScanned = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (string file in Enumerate(() => Directory.EnumerateFiles(folder, "*", DiscoveryOptions)))
        {
            if (!IsHidden(file))
            {
                files.Add(file);
            }
        }

        onFolderScanned?.Invoke();

        if (depthBudget <= 0)
        {
            return;
        }

        foreach (string subFolder in Enumerate(() => Directory.EnumerateDirectories(folder, "*", DiscoveryOptions)))
        {
            if (SkippedFolders.Contains(Path.GetFileName(subFolder)))
            {
                continue;
            }

            CollectFiles(subFolder, depthBudget - 1, files, cancellationToken, onFolderScanned);
        }
    }

    /// <summary>
    /// Whether a file is one the user would not think of as a document: dot-files, and the
    /// lock/temporary siblings Office leaves next to an open document.
    /// </summary>
    private static bool IsHidden(string path)
    {
        string name = Path.GetFileName(path);
        return name.StartsWith('.') || name.StartsWith("~$", StringComparison.Ordinal);
    }

    /// <summary>
    /// Materialises a lazy listing behind a catch. Enumerating is what throws, not calling it, so the
    /// guard has to wrap the iteration — and materialising is also what stops one bad entry from
    /// discarding the entries already read.
    /// </summary>
    private static IReadOnlyList<string> Enumerate(Func<IEnumerable<string>> listing)
    {
        List<string> results = [];

        try
        {
            foreach (string item in listing())
            {
                results.Add(item);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Whatever was read before the failure still counts; the rest of this folder does not.
        }

        return results;
    }

    /// <summary>Most recently modified first: a documents list is read newest-down, not A-to-Z.</summary>
    private static int CompareEntries(DocumentEntry left, DocumentEntry right)
    {
        int byModified = right.ModifiedUtc.CompareTo(left.ModifiedUtc);
        return byModified != 0
            ? byModified
            : string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Search

    public async Task<IReadOnlyList<DocumentSearchHit>> SearchAsync(
        string query,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        string[] tokens = DocumentSearchIndex.Tokenize(query);
        if (tokens.Length == 0 || limit <= 0)
        {
            return [];
        }

        EnsureLoaded();

        List<DocumentEntry> entries;
        lock (_lock)
        {
            entries = [.. _entries];
        }

        // Name and metadata hits come out of the in-memory snapshot: no query can beat a list that
        // is already in the heap, and these are the hits that must never be crowded out.
        List<DocumentSearchHit> hits = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (DocumentEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (MatchesAll($"{entry.FileName} {entry.RelativeFolder}", tokens))
            {
                hits.Add(new DocumentSearchHit(entry, DocumentSearchMatch.Name, string.Empty, -2000 + hits.Count));
                seen.Add(entry.Path);
            }
            else if (MatchesAll($"{entry.Title} {entry.Author}", tokens))
            {
                hits.Add(new DocumentSearchHit(entry, DocumentSearchMatch.Metadata, entry.Title, -1000 + hits.Count));
                seen.Add(entry.Path);
            }

            if (hits.Count >= limit)
            {
                return hits;
            }
        }

        Dictionary<string, DocumentEntry> byPath = entries.ToDictionary(
            entry => entry.Path,
            StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<DocumentSearchIndex.ContentHit> contentHits =
            await Task.Run(() => SearchContent(query, limit), cancellationToken);

        foreach (DocumentSearchIndex.ContentHit hit in contentHits)
        {
            if (hits.Count >= limit)
            {
                break;
            }

            // A content row whose metadata row is gone is a leftover from an interrupted delete;
            // there is nothing to show for it, so it is skipped rather than shown pathless.
            if (!byPath.TryGetValue(hit.Path, out DocumentEntry? entry) || !seen.Add(hit.Path))
            {
                continue;
            }

            hits.Add(new DocumentSearchHit(entry, DocumentSearchMatch.Content, hit.Snippet, hit.Rank));
        }

        return hits;
    }

    public async Task<IReadOnlyList<DocumentSearchHit>> SearchSemanticAsync(
        string query,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!_embedder.IsConfigured || query.Trim().Length == 0 || limit <= 0)
        {
            return [];
        }

        EnsureLoaded();

        // The query is embedded over the network and compared on a worker thread; neither runs on the key handler. The dialog
        // re-asks on every keystroke and every index change, so the vector of the last query is kept while it still matches.
        string model = _embedder.ModelId;
        float[] queryVector = await QueryVectorAsync(query, model, cancellationToken);

        return await Task.Run(() =>
        {
            using iTerminalDbContext context = _dbContextFactory.CreateDbContext();
            context.Database.EnsureCreated();

            IReadOnlyList<DocumentVectorIndex.Match> matches = _vectorIndex.Search(context, model, queryVector, limit);

            Dictionary<string, DocumentEntry> byPath;
            lock (_lock)
            {
                byPath = _entries.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
            }

            // One connection for every row: the passages were stored with their vectors, so this reads a few hundred
            // characters per hit rather than each document's text.
            List<DocumentSearchHit> hits = new(matches.Count);
            foreach (DocumentVectorIndex.Match match in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // A vector whose document is no longer listed is a leftover from an interrupted delete: nothing to show for it.
                if (!byPath.TryGetValue(match.Path, out DocumentEntry? entry))
                {
                    continue;
                }

                string passage = _vectorIndex.ReadPassage(context, match.Path, match.ChunkStart);

                // Rank is lower-first, so the best score gets the lowest rank.
                hits.Add(new DocumentSearchHit(entry, DocumentSearchMatch.Semantic, Excerpt(passage), -match.Score));
            }

            return (IReadOnlyList<DocumentSearchHit>)hits;
        }, cancellationToken);
    }

    private async Task<float[]> QueryVectorAsync(string query, string model, CancellationToken cancellationToken)
    {
        lock (_queryCacheLock)
        {
            if (_lastQuery is { } last && last.Query == query && last.Model == model)
            {
                return last.Vector;
            }
        }

        float[] vector = (await _embedder.EmbedAsync([query], cancellationToken))[0];
        lock (_queryCacheLock)
        {
            _lastQuery = new QueryVector(query, model, vector);
        }

        return vector;
    }

    /// <summary>The start of a passage as one line for a results row: whitespace collapsed, cut to <paramref name="maxLength"/>.</summary>
    internal static string Excerpt(string passage, int maxLength = 160)
    {
        string collapsed = string.Join(' ', passage.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= maxLength ? collapsed : DocumentChunker.TrimToCharacters(collapsed[..maxLength]) + "…";
    }

    /// <summary>
    /// Embeds every document whose passages are missing or were made by another model. One document at a time, each
    /// saved as soon as its vectors arrive, so an interrupted run resumes where it stopped. A model that fails stops the
    /// run and is reported in <see cref="SemanticMessage"/>; it does not fail the scan, and it is not retried per document.
    /// </summary>
    private async Task EmbedPendingAsync(CancellationToken cancellationToken)
    {
        if (!_embedder.IsConfigured || !await _embedGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        bool hadWork = false;
        try
        {
            string model = _embedder.ModelId;
            _semanticFailure = null;

            List<string> pending;
            using (iTerminalDbContext context = _dbContextFactory.CreateDbContext())
            {
                context.Database.EnsureCreated();
                pending = _vectorIndex.Pending(context, model);

                // Read now, not at the end of the run, so a library that is already embedded reads as built right after a restart.
                _semanticIndexed = _vectorIndex.Count(context, model);
            }

            hadWork = pending.Count > 0;
            _semanticDone = 0;
            _semanticTotal = pending.Count;
            _semanticRunning = hadWork;

            // One refused document (a passage the model rejects, say) must not stop the rest. It is left pending and tried again
            // on the next run; the others are embedded now.
            int failed = 0;
            string lastFailure = string.Empty;
            foreach (string path in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await EmbedDocumentAsync(path, model, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failed++;
                    lastFailure = exception.Message;
                    _logger?.Warning(exception, "Could not embed {Path} for the meaning index.", path);
                }

                _semanticDone++;
            }

            if (failed > 0)
            {
                _semanticFailure = $"{failed} document(s) could not be embedded: {lastFailure}";
            }

            using iTerminalDbContext counted = _dbContextFactory.CreateDbContext();
            counted.Database.EnsureCreated();
            _semanticIndexed = _vectorIndex.Count(counted, model);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _semanticFailure = exception.Message;
            _logger?.Warning(exception, "Building the document meaning index failed.");
        }
        finally
        {
            _semanticRunning = false;
            _embedGate.Release();

            // The meaning index changed, so a listing built on it is stale: the dialog re-runs its query.
            if (hadWork)
            {
                LibraryChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private async Task EmbedDocumentAsync(string path, string model, CancellationToken cancellationToken)
    {
        DocumentSnapshot? read;
        using (iTerminalDbContext context = _dbContextFactory.CreateDbContext())
        {
            read = ReadSnapshot(context, path);
        }

        // No row: the file was removed since the pending list was made. Nothing to embed or record.
        if (read is null)
        {
            return;
        }

        DocumentStamp stamp = read.Stamp;
        string text = read.Text;

        IReadOnlyList<int> starts = DocumentChunker.ChunkStarts(text);
        List<(int ChunkStart, string Passage, float[] Vector)> passages = new(starts.Count);
        if (starts.Count > 0)
        {
            List<string> passageTexts = [.. starts.Select(start => DocumentChunker.ChunkAt(text, start))];
            IReadOnlyList<float[]> vectors = await _embedder.EmbedAsync(passageTexts, cancellationToken);
            for (int index = 0; index < starts.Count; index++)
            {
                passages.Add((starts[index], passageTexts[index], vectors[index]));
            }
        }

        // The file may have been re-extracted or removed while its passages were being embedded. Its stamp then differs, or
        // is gone, so these vectors describe text that is no longer there and are dropped; a changed file is pending next run.
        // The stamp is compared rather than the text, so a large document is not read a second time.
        lock (_writeLock)
        {
            using iTerminalDbContext save = _dbContextFactory.CreateDbContext();
            save.Database.EnsureCreated();
            using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = save.Database.BeginTransaction();

            if (stamp.Equals(ReadStamp(save, path)))
            {
                _vectorIndex.Replace(save, path, model, passages);
                transaction.Commit();

                // The commit is what makes the new vectors visible to a search that loads rows; see DocumentVectorIndex.Invalidate.
                _vectorIndex.Invalidate();
            }
        }
    }

    /// <summary>
    /// The stamp alone, for the save-time check. Only the metadata is read: the text is not needed to see whether the file
    /// has changed. Safe without a join because the save runs under <see cref="_writeLock"/>, which the scan's writes share.
    /// </summary>
    private static DocumentStamp? ReadStamp(iTerminalDbContext context, string path)
    {
        var row = context.DocumentEntries
            .AsNoTracking()
            .Where(entry => entry.Path == path)
            .Select(entry => new { entry.Size, entry.ModifiedUtc })
            .FirstOrDefault();

        return row is null ? null : new DocumentStamp(row.Size, row.ModifiedUtc);
    }

    /// <summary>
    /// A document's stamp and text in one statement, so the two always describe the same extraction: read apart, a scan
    /// committing between them would pair the new stamp with the old text. Null when the document is not listed or has no
    /// text row (removed, or never extracted).
    /// </summary>
    private static DocumentSnapshot? ReadSnapshot(iTerminalDbContext context, string path)
    {
        var row = (from entry in context.DocumentEntries.AsNoTracking()
                   join content in context.DocumentContentEntries.AsNoTracking() on entry.Path equals content.Path
                   where entry.Path == path
                   select new { entry.Size, entry.ModifiedUtc, content.Text })
            .FirstOrDefault();

        return row is null ? null : new DocumentSnapshot(new DocumentStamp(row.Size, row.ModifiedUtc), row.Text);
    }

    private IReadOnlyList<DocumentSearchIndex.ContentHit> SearchContent(string query, int limit)
    {
        try
        {
            using iTerminalDbContext context = _dbContextFactory.CreateDbContext();
            context.Database.EnsureCreated();
            return _searchIndex.Search(context, query, limit);
        }
        catch (Exception exception)
        {
            _logger?.Warning(exception, "Searching the document index failed.");
            return [];
        }
    }

    private static bool MatchesAll(string haystack, string[] tokens)
    {
        foreach (string token in tokens)
        {
            if (!haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public async Task<string> GetTextAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            await using iTerminalDbContext context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            DocumentContentEntry? content = await context.DocumentContentEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(entry => entry.Path == path, cancellationToken);

            return content?.Text ?? string.Empty;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.Warning(exception, "Could not read the cached text of {Path}.", path);
            return string.Empty;
        }
    }

    #endregion

    #region Persistence

    /// <summary>
    /// Fills the in-memory copy from the metadata table, once — and only the metadata table. The
    /// text is deliberately left on disk; see the class remarks.
    ///
    /// <para>The read is marked done only after it succeeds. A database that is momentarily
    /// unavailable — being recreated at start-up, locked by a second instance — would otherwise
    /// leave the dialog empty for the rest of the session, since nothing would ever look again.</para>
    /// </summary>
    private void EnsureLoaded()
    {
        lock (_loadLock)
        {
            if (_isLoaded)
            {
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now - _lastLoadAttemptUtc < LoadRetryInterval)
            {
                return;
            }

            _lastLoadAttemptUtc = now;

            try
            {
                using iTerminalDbContext context = _dbContextFactory.CreateDbContext();
                context.Database.EnsureCreated();

                List<DocumentEntry> loaded = [.. context.DocumentEntries.AsNoTracking()];
                loaded.Sort(CompareEntries);

                lock (_lock)
                {
                    _entries = loaded;
                }

                LastIndexedUtc = loaded.Count == 0 ? null : loaded.Max(entry => entry.IndexedUtc);

                // Creating the full-text table here rather than on the first search means the very
                // first query the user types meets a table that already exists, and that a rebuild
                // from a surviving content table happens off the search path.
                _searchIndex.EnsureCreated(context);

                _isLoaded = true;
            }
            catch (Exception exception)
            {
                _logger?.Warning(exception, "Could not load the cached document index.");
            }
        }
    }

    /// <summary>
    /// Writes a batch of extracted documents and a set of deletions. All three stores move together
    /// and in one transaction: a metadata row without its content row would preview as blank, and a
    /// content row without its FTS row would be invisible to search — both of which look exactly
    /// like a failed extraction and neither of which a rescan would fix, since the metadata row's
    /// stamp says the file is up to date.
    /// </summary>
    private void Persist(IReadOnlyList<(DocumentEntry Entry, string Text)> changed, IReadOnlyList<string> removed)
    {
        // Under the same lock as an embedding save: see _writeLock.
        lock (_writeLock)
        {
            PersistLocked(changed, removed);
        }
    }

    private void PersistLocked(IReadOnlyList<(DocumentEntry Entry, string Text)> changed, IReadOnlyList<string> removed)
    {
        if (changed.Count == 0 && removed.Count == 0)
        {
            return;
        }

        try
        {
            using iTerminalDbContext context = _dbContextFactory.CreateDbContext();
            context.Database.EnsureCreated();
            _searchIndex.EnsureCreated(context);

            using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
                context.Database.BeginTransaction();

            foreach (string path in removed)
            {
                DocumentEntry? existing = context.DocumentEntries.Find(path);
                if (existing is not null)
                {
                    context.DocumentEntries.Remove(existing);
                }

                DocumentContentEntry? content = context.DocumentContentEntries.Find(path);
                if (content is not null)
                {
                    context.DocumentContentEntries.Remove(content);
                }
            }

            foreach ((DocumentEntry entry, string _) in changed)
            {
                DocumentEntry? existing = context.DocumentEntries.Find(entry.Path);
                if (existing is null)
                {
                    context.DocumentEntries.Add(Clone(entry));
                }
                else
                {
                    CopyInto(entry, existing);
                }
            }

            foreach ((DocumentEntry entry, string text) in changed)
            {
                DocumentContentEntry? existing = context.DocumentContentEntries.Find(entry.Path);

                if (text.Length == 0)
                {
                    if (existing is not null)
                    {
                        context.DocumentContentEntries.Remove(existing);
                    }

                    continue;
                }

                if (existing is null)
                {
                    context.DocumentContentEntries.Add(new DocumentContentEntry { Path = entry.Path, Text = text });
                }
                else
                {
                    existing.Text = text;
                }
            }

            context.SaveChanges();

            _searchIndex.Remove(context, removed);
            foreach ((DocumentEntry entry, string text) in changed)
            {
                _searchIndex.Upsert(context, entry.Path, text);
            }

            // Passages of a changed or removed file describe text that is gone. They go in this transaction; the next
            // embedding run finds the changed ones pending and embeds the new text.
            List<string> stale = [.. removed];
            stale.AddRange(changed.Select(change => change.Entry.Path));
            _vectorIndex.Remove(context, stale);

            transaction.Commit();

            // See DocumentVectorIndex.Invalidate: the passages removed above must not survive in a snapshot cached mid-transaction.
            _vectorIndex.Invalidate();
        }
        catch (Exception exception)
        {
            // A library that cannot be cached still works for this session; it just re-indexes next
            // time, which is not worth failing the scan over.
            _logger?.Warning(exception, "Could not persist the document index.");
        }
    }

    private static DocumentEntry Clone(DocumentEntry source)
    {
        DocumentEntry copy = new() { Path = source.Path };
        CopyInto(source, copy);
        return copy;
    }

    private static void CopyInto(DocumentEntry source, DocumentEntry target)
    {
        target.FileName = source.FileName;
        target.RelativeFolder = source.RelativeFolder;
        target.Extension = source.Extension;
        target.Kind = source.Kind;
        target.ExtractionStatus = source.ExtractionStatus;
        target.ExtractionNote = source.ExtractionNote;
        target.Title = source.Title;
        target.Author = source.Author;
        target.PageCount = source.PageCount;
        target.WordCount = source.WordCount;
        target.TextLength = source.TextLength;
        target.Size = source.Size;
        target.ModifiedUtc = source.ModifiedUtc;
        target.IndexedUtc = source.IndexedUtc;
    }

    #endregion
}
