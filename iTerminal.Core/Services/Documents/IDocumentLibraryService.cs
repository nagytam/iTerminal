using iTerminal.Core.Data.Documents;

namespace iTerminal.Core.Services.Documents;

/// <summary>
/// The indexed document library: a SQLite-cached record of every file under the configured
/// documents folder, the text extracted from the ones an extractor understands, and the full-text
/// search that runs over it.
///
/// <para>The split of work mirrors <see cref="Music.IMusicLibraryService"/> — a background job does
/// the slow half, the UI does the fast half — but the halves are further apart here. Extracting a
/// PDF or OCR'ing a scan is seconds per file rather than microseconds, and the extracted text is
/// far too large to hold in memory, so unlike the music library <em>searching is asynchronous too</em>:
/// it queries the database. Dialogs run it on a background task, never from a key handler.</para>
/// </summary>
public interface IDocumentLibraryService
{
    /// <summary>
    /// Folder currently being indexed: the <c>DocumentsFolder</c> setting, or the built-in default
    /// when that setting is empty. Never null, but may point at a folder that does not exist.
    /// </summary>
    string DocumentsFolder { get; }

    /// <summary>True while a scan is running, so the UI can say so instead of looking stalled.</summary>
    bool IsIndexing { get; }

    /// <summary>
    /// How far the running scan has got. <see cref="DocumentIndexingProgress.Idle"/> between scans.
    /// </summary>
    DocumentIndexingProgress Progress { get; }

    /// <summary>Number of indexed files, whether or not any text came out of them.</summary>
    int DocumentCount { get; }

    /// <summary>Number of indexed files that have searchable text.</summary>
    int IndexedTextCount { get; }

    /// <summary>When the last successful scan finished, or null when nothing has been indexed yet.</summary>
    DateTimeOffset? LastIndexedUtc { get; }

    /// <summary>Human-readable outcome of the last scan, for the dialog's status line.</summary>
    string StatusMessage { get; }

    /// <summary>
    /// Whether full-text search is backed by the FTS5 index or by the fallback scan, and whether an
    /// OCR engine was found — the two facts that change what a search can find, which is why the
    /// dialog shows them rather than leaving the user to wonder why an image is not matching.
    /// </summary>
    string CapabilityMessage { get; }

    /// <summary>Raised after a scan changes the library. May fire on a background thread.</summary>
    event EventHandler? LibraryChanged;

    /// <summary>
    /// Raised as a scan advances, and always on a stage change and when it ends. Rate-limited by
    /// the implementation and fired on a background thread.
    /// </summary>
    event EventHandler<DocumentIndexingProgress>? IndexingProgressChanged;

    /// <summary>Every indexed document, newest first. A snapshot, safe to enumerate.</summary>
    IReadOnlyList<DocumentEntry> Entries { get; }

    /// <summary>
    /// Reads the cached metadata table into memory if that has not happened yet, so the dialog only
    /// ever meets an already-loaded index. Called from the indexing job at start-up; every other
    /// member does it lazily.
    /// </summary>
    void Preload();

    /// <summary>
    /// Walks the documents folder and brings the index in line with it: unchanged files (same size
    /// and timestamp) are left alone, changed and new files have their text re-extracted, and rows
    /// whose file is gone are deleted. Returns the number of documents afterwards.
    /// </summary>
    Task<int> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks for the scan to run. Nothing is walked until something needs the library — the document manager calls this
    /// when it opens — so a scan is not started at start-up for a feature the user may never use. Idempotent.
    /// </summary>
    void RequestIndexing();

    /// <summary>Completes once <see cref="RequestIndexing"/> has been called.</summary>
    Task IndexingRequested { get; }

    /// <summary>
    /// Finds documents matching <paramref name="query"/>: every word must match, and each is
    /// treated as a prefix so the results narrow as the query is typed. Name and metadata hits sort
    /// ahead of body-text hits — someone who types a file name wants that file, not the twenty
    /// documents that mention it. An empty query yields no results.
    /// </summary>
    Task<IReadOnlyList<DocumentSearchHit>> SearchAsync(
        string query,
        int limit = 200,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The extracted text of one document, for the preview pane. Empty when nothing was extracted.
    /// Reads a single row, so it is safe to call each time the cursor moves — but on a background
    /// task, never from the key handler that moved it.
    /// </summary>
    Task<string> GetTextAsync(string path, CancellationToken cancellationToken = default);
}
