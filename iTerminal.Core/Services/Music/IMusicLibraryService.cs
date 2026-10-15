using iTerminal.Core.Data.Music;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// The indexed music library: a SQLite-cached snapshot of the metadata of every audio file under
/// the configured music folder, plus the search that the music player's search pane runs against it.
///
/// <para>Indexing is the slow half and always happens on a background job
/// (<see cref="Jobs.MusicLibraryIndexJob"/>); searching is the fast half and runs against an
/// in-memory copy of the table, so a key handler can call <see cref="Search"/> directly and stay
/// well inside the 200 ms budget.</para>
/// </summary>
public interface IMusicLibraryService
{
    /// <summary>
    /// Folder currently being indexed: the <c>MusicFolder</c> setting, or the OS "Music" folder
    /// when that setting is empty. Never null, but may point at a folder that does not exist.
    /// </summary>
    string MusicFolder { get; }

    /// <summary>True while a scan is running, so the UI can say so instead of looking stalled.</summary>
    bool IsIndexing { get; }

    /// <summary>
    /// How far the running scan has got: which stage it is in, how many files discovery has found,
    /// and how many of those have been indexed. <see cref="MusicIndexingProgress.Idle"/> between
    /// scans.
    /// </summary>
    MusicIndexingProgress Progress { get; }

    /// <summary>Number of indexed files.</summary>
    int TrackCount { get; }

    /// <summary>When the last successful scan finished, or null when nothing has been indexed yet.</summary>
    DateTimeOffset? LastIndexedUtc { get; }

    /// <summary>Human-readable outcome of the last scan, for the player's status line.</summary>
    string StatusMessage { get; }

    /// <summary>Raised after a scan changes the library. May fire on a background thread.</summary>
    event EventHandler? LibraryChanged;

    /// <summary>
    /// Raised as a scan advances, and always on a stage change and when it ends.
    /// <see cref="LibraryChanged"/> only fires once the whole scan has landed, so this is the event a
    /// panel needs to repaint a progress indicator while the work is still running. Rate-limited by
    /// the implementation and fired on a background thread.
    /// </summary>
    event EventHandler<MusicIndexingProgress>? IndexingProgressChanged;

    /// <summary>Every indexed entry, in artist/album/title order. A snapshot, safe to enumerate.</summary>
    IReadOnlyList<MusicLibraryEntry> Entries { get; }

    /// <summary>
    /// Reads the cached table into memory if that has not happened yet. Every other member does this
    /// lazily on first use, which would put a full table read on whichever thread got there first —
    /// usually a key handler opening the catalog panel. Calling this from a background job at
    /// start-up means the panel only ever meets an already-loaded index.
    /// </summary>
    void Preload();

    /// <summary>
    /// Walks the music folder and brings the cache in line with it: unchanged files (same size and
    /// timestamp) are left alone, changed and new files have their tags re-read, and rows whose file
    /// is gone are deleted. Returns the number of files in the library afterwards.
    ///
    /// <para>Runs in two reported stages — discovery (the folder walk) then indexing (the per-file
    /// pass) — publishing <see cref="Progress"/> through <see cref="IndexingProgressChanged"/> as it
    /// goes.</para>
    /// </summary>
    Task<int> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Matches <paramref name="query"/> against artists, albums and track titles. Artist and album
    /// hits come first and are collapsed to one row each; track hits follow. An empty query yields
    /// no results.
    /// </summary>
    IReadOnlyList<MusicSearchResult> Search(string query, int limit = 200);

    /// <summary>
    /// Turns a search hit into the tracks it stands for: every track of an artist, every track of an
    /// album, or the single track itself.
    /// </summary>
    IReadOnlyList<MusicTrack> ExpandResult(MusicSearchResult result);
}
