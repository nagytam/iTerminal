using iConsole.Jobs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;

namespace iTerminal.Core.Jobs;

/// <summary>
/// Keeps the music library index in step with the music folder. Runs as a background job rather
/// than from the player dialog so the index is already warm the first time the user searches, and
/// so a large library is walked off both the render thread and the key handler.
///
/// <para>The folder setting is polled instead of subscribed to: <see cref="ISettingsService"/> is a
/// plain key/value store with no change notification, and a five-second poll is far cheaper than
/// giving it one.</para>
/// </summary>
public class MusicLibraryIndexJob(IMusicLibraryService musicLibraryService) : Job
{
    /// <summary>Let the app finish starting before walking what may be tens of thousands of files.</summary>
    public TimeSpan StartupDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How often the music-folder setting is re-read.</summary>
    public TimeSpan FolderPollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How often the folder is re-walked even when the setting has not moved.</summary>
    public TimeSpan RescanInterval { get; init; } = TimeSpan.FromMinutes(15);

    private readonly IMusicLibraryService _musicLibraryService = musicLibraryService;

    private string _indexedFolder = string.Empty;
    private DateTimeOffset _lastScanUtc = DateTimeOffset.MinValue;

    public override JobPriority Priority => JobPriority.Background;

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        // Before the delay, not after it: the point of the delay is to keep the *scan* out of
        // start-up, while reading the cached table is what makes the catalog panel usable at all.
        // Doing it here means the first Ctrl+F7 never waits on SQLite.
        try
        {
            _musicLibraryService.Preload();
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
                string folder = _musicLibraryService.MusicFolder;

                // Pointing the setting at a different folder invalidates the whole index, so that
                // rescan cannot wait for the periodic one.
                bool folderChanged = !string.Equals(folder, _indexedFolder, StringComparison.OrdinalIgnoreCase);
                bool due = DateTimeOffset.UtcNow - _lastScanUtc >= RescanInterval;

                if (folderChanged || due)
                {
                    await _musicLibraryService.RefreshAsync(cancellationToken);
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
