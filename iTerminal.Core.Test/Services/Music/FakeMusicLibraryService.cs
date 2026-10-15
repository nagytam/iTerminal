using iTerminal.Core.Data.Music;
using iTerminal.Core.Services.Music;

namespace iTerminal.Core.Test.Services.Music;

/// <summary>
/// An in-memory stand-in for the indexed library. Search and expansion mirror
/// <see cref="MusicLibraryService"/> closely enough for the panels to be exercised, without a SQLite
/// file or a folder full of audio to index.
/// </summary>
internal sealed class FakeMusicLibraryService : IMusicLibraryService
{
    private readonly List<MusicLibraryEntry> _entries = [];

    public string MusicFolder { get; set; } = @"C:\Music";

    /// <summary>Mirrors the real service: the flag is the progress snapshot, not a second state.</summary>
    public bool IsIndexing => Progress.IsRunning;

    public MusicIndexingProgress Progress { get; private set; } = MusicIndexingProgress.Idle;

    public int TrackCount => _entries.Count;

    public DateTimeOffset? LastIndexedUtc { get; set; }

    public string StatusMessage { get; set; } = string.Empty;

    public int RefreshCount { get; private set; }

    public int PreloadCount { get; private set; }

    /// <summary>Nothing to read from: the fake's entries are already in memory.</summary>
    public void Preload() => PreloadCount++;

    public event EventHandler? LibraryChanged;

    public event EventHandler<MusicIndexingProgress>? IndexingProgressChanged;

    /// <summary>Puts the fake into a scan stage and publishes it, the way a real scan would.</summary>
    public void SetProgress(MusicIndexingPhase phase, int discovered = 0, int indexed = 0)
    {
        Progress = new MusicIndexingProgress(phase, discovered, indexed);
        IndexingProgressChanged?.Invoke(this, Progress);
    }

    public IReadOnlyList<MusicLibraryEntry> Entries => [.. _entries];

    public FakeMusicLibraryService Add(string artist, string album, string title, string? path = null)
    {
        _entries.Add(new MusicLibraryEntry
        {
            Path = path ?? System.IO.Path.Combine(MusicFolder, artist, album, title + ".mp3"),
            Artist = artist,
            Album = album,
            Title = title,
            DurationTicks = TimeSpan.FromMinutes(3).Ticks,
        });
        return this;
    }

    public Task<int> RefreshAsync(CancellationToken cancellationToken = default)
    {
        RefreshCount++;

        // Both stages are walked through so anything watching progress sees the same sequence a real
        // scan produces, ending back at idle.
        SetProgress(MusicIndexingPhase.Discovering, _entries.Count);
        SetProgress(MusicIndexingPhase.Indexing, _entries.Count, _entries.Count);
        SetProgress(MusicIndexingPhase.Idle);

        LastIndexedUtc = DateTimeOffset.UtcNow;
        LibraryChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(_entries.Count);
    }

    public IReadOnlyList<MusicSearchResult> Search(string query, int limit = 200)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        List<MusicSearchResult> results = [];

        foreach (var group in _entries
            .GroupBy(entry => entry.Artist, StringComparer.OrdinalIgnoreCase)
            .Where(group => Matches(group.Key, query)))
        {
            results.Add(new MusicSearchResult(MusicSearchResultKind.Artist, group.Key, string.Empty, null, group.Count()));
        }

        foreach (var group in _entries
            .GroupBy(entry => (entry.Artist, entry.Album))
            .Where(group => Matches($"{group.Key.Artist} {group.Key.Album}", query)))
        {
            results.Add(new MusicSearchResult(MusicSearchResultKind.Album, group.Key.Artist, group.Key.Album, null, group.Count()));
        }

        foreach (var entry in _entries.Where(entry => Matches($"{entry.Artist} {entry.Album} {entry.Title}", query)))
        {
            results.Add(new MusicSearchResult(MusicSearchResultKind.Track, entry.Artist, entry.Album, entry.ToTrack(), 1));
        }

        return results.Count > limit ? results[..limit] : results;
    }

    public IReadOnlyList<MusicTrack> ExpandResult(MusicSearchResult result) => result.Kind switch
    {
        MusicSearchResultKind.Track => result.Track is null ? [] : [result.Track],
        MusicSearchResultKind.Artist =>
            [.. _entries.Where(entry => string.Equals(entry.Artist, result.Artist, StringComparison.OrdinalIgnoreCase)).Select(entry => entry.ToTrack())],
        _ =>
            [.. _entries
                .Where(entry => string.Equals(entry.Artist, result.Artist, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(entry.Album, result.Album, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.ToTrack())],
    };

    public void RaiseLibraryChanged() => LibraryChanged?.Invoke(this, EventArgs.Empty);

    private static bool Matches(string haystack, string query) =>
        haystack.Contains(query, StringComparison.OrdinalIgnoreCase);
}
