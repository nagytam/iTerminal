using iConsole.Jobs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Documents;

namespace iTerminal.Core.Jobs;

/// <summary>
/// Keeps the document index in step with the documents folder. A background job for the same reason
/// <see cref="MusicLibraryIndexJob"/> is one — the index should be warm before the first search —
/// but with the intervals turned right down, because the work is a different size: extracting text
/// from a folder of PDFs is minutes, not the second a music rescan costs, and a scan that ran every
/// quarter of an hour would leave a core busy most of the time for no benefit.
///
/// <para>The folder setting is polled rather than subscribed to: <see cref="ISettingsService"/> is a
/// plain key/value store with no change notification, and a poll is far cheaper than giving it one.</para>
/// </summary>
public class DocumentLibraryIndexJob(IDocumentLibraryService documentLibraryService) : Job
{
    /// <summary>
    /// Let the app finish starting before walking what may be tens of thousands of documents. Longer
    /// than the music job's delay so the two first scans do not land on the machine together — the
    /// document scan is the heavier of the two and should not be competing with anything.
    /// </summary>
    public TimeSpan StartupDelay { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>How often the documents-folder setting is re-read.</summary>
    public TimeSpan FolderPollInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How often the folder is re-walked even when the setting has not moved. An hour rather than
    /// the music library's quarter: a rescan whose files are all unchanged still costs a full
    /// directory walk, and documents change on the scale of a working day.
    /// </summary>
    public TimeSpan RescanInterval { get; init; } = TimeSpan.FromHours(1);

    private readonly IDocumentLibraryService _documentLibraryService = documentLibraryService;

    private string _indexedFolder = string.Empty;
    private DateTimeOffset _lastScanUtc = DateTimeOffset.MinValue;

    public override JobPriority Priority => JobPriority.Background;

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        // Before the delay, not after it: the point of the delay is to keep the *scan* out of
        // start-up, while reading the cached table is what makes the dialog usable at all. Doing it
        // here means the first time the dialog is opened it never waits on SQLite.
        try
        {
            _documentLibraryService.Preload();
        }
        catch
        {
            // A cache that cannot be read is the service's problem to report, not this job's.
        }

        try
        {
            await Task.Delay(StartupDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                string folder = _documentLibraryService.DocumentsFolder;

                // Pointing the setting at a different folder invalidates the whole index, so that
                // rescan cannot wait for the periodic one.
                bool folderChanged = !string.Equals(folder, _indexedFolder, StringComparison.OrdinalIgnoreCase);
                bool due = DateTimeOffset.UtcNow - _lastScanUtc >= RescanInterval;

                if (folderChanged || due)
                {
                    await _documentLibraryService.RefreshAsync(cancellationToken);
                    _indexedFolder = folder;
                    _lastScanUtc = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Indexing is a convenience; a failed pass must not take the job down. The service
                // has already recorded why in its status message.
                _lastScanUtc = DateTimeOffset.UtcNow;
            }

            try
            {
                await Task.Delay(FolderPollInterval, cancellationToken);

                // The folders are scanned again when the window has focus; a window in the background leaves them alone.
                await iConsole.Services.TerminalFocus.Current.WaitForFocusAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
