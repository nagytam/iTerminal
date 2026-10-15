using iTerminal.Core.Audio;
using iTerminal.Core.Audio.Decoders;
using iTerminal.Core.Data;
using iTerminal.Core.Data.Music;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// SQLite-backed music library index. The table is the durable copy and an in-memory list is the
/// one searches actually run against — the same shape <see cref="SettingsService"/> uses, and for
/// the same reason: reads happen from key handlers and must never wait on a query.
///
/// <para>The table is also what the catalog panel draws on its first paint: a run that has never
/// scanned anything still lists whatever the last run left behind, because the panel reads the
/// in-memory copy and that copy is filled from the table before any scan starts.</para>
///
/// <para>A rescan is deliberately cheap when nothing changed. The walk compares each file's size
/// and last-write time against the cached row and only re-reads tags when they differ, so the
/// steady-state cost of the periodic scan is a directory enumeration.</para>
///
/// <para>A scan that <em>does</em> find work publishes it as it goes rather than at the end: every
/// batch of freshly read files is written to the table and folded into the in-memory copy, so the
/// panel fills in while a first-ever scan of a large folder is still running, and an app closed
/// mid-scan keeps what had been indexed by then.</para>
/// </summary>
public sealed class MusicLibraryService : IMusicLibraryService
{
    private const int MaxScanDepth = 12;

    private readonly IDbContextFactory<iTerminalDbContext> _dbContextFactory;
    private readonly ISettingsService _settingsService;
    private readonly Serilog.ILogger? _logger;

    private readonly Lock _lock = new();

    /// <summary>
    /// Held for the whole of the initial table read, so a second caller waits for the rows instead
    /// of walking past an empty list — which is what a flag flipped before the query would give it.
    /// </summary>
    private readonly Lock _loadLock = new();

    private readonly SemaphoreSlim _scanGate = new(1, 1);

    private List<MusicLibraryEntry> _entries = [];
    private bool _isLoaded;
    private DateTimeOffset _lastLoadAttemptUtc = DateTimeOffset.MinValue;

    /// <summary>Reference assignment is atomic and the snapshot is immutable, so readers need no lock.</summary>
    private volatile MusicIndexingProgress _progress = MusicIndexingProgress.Idle;

    private DateTimeOffset _lastProgressUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// Both dependencies are optional so a test — or a run with no database at all — gets a
    /// throwaway SQLite file and no persisted settings instead of having to fake them.
    /// </summary>
    public MusicLibraryService(
        IDbContextFactory<iTerminalDbContext>? dbContextFactory,
        ISettingsService? settingsService,
        Serilog.ILogger? logger = null)
    {
        _dbContextFactory = dbContextFactory ?? new TemporaryDbContextFactory("MusicLibrary");
        _settingsService = settingsService ?? NullSettingsService.Instance;
        _logger = logger;
    }

    public event EventHandler? LibraryChanged;

    public event EventHandler<MusicIndexingProgress>? IndexingProgressChanged;

    /// <summary>
    /// How often progress is published while a stage runs. A scan touches thousands of files and
    /// every event repaints a panel, so the counts are throttled to something an eye can follow;
    /// stage changes and the end of the scan are always published regardless.
    /// </summary>
    public TimeSpan ProgressReportInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How long a running scan may hold freshly read files before writing them to the table and
    /// folding them into the in-memory copy. This is what makes a first scan of a large folder fill
    /// the panel as it runs rather than all at once at the end.
    /// </summary>
    public TimeSpan ScanFlushInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How many freshly read files force a flush regardless of <see cref="ScanFlushInterval"/>, so a
    /// fast scan of small files still lands in batches the database can swallow in one transaction.
    /// </summary>
    public int ScanFlushBatchSize { get; init; } = 250;

    /// <summary>
    /// How long a failed table read is left alone before another attempt. Without a pause, a
    /// database that cannot be opened would be re-queried on every repaint of the catalog panel.
    /// </summary>
    public TimeSpan LoadRetryInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Derived from <see cref="Progress"/> so there is one piece of state, not two.</summary>
    public bool IsIndexing => _progress.IsRunning;

    public MusicIndexingProgress Progress => _progress;

    public DateTimeOffset? LastIndexedUtc { get; private set; }

    public string StatusMessage { get; private set; } = string.Empty;

    public string MusicFolder => ResolveMusicFolder(_settingsService);

    /// <summary>
    /// The folder the indexer walks: the <c>MusicFolder</c> setting when it is set, otherwise the
    /// platform's own Music folder. Shared with the settings page so both show the same default.
    /// </summary>
    public static string ResolveMusicFolder(ISettingsService? settingsService)
    {
        string? configured = settingsService?.Get(Settings.MusicFolder);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        if (OperatingSystem.IsLinux())
        {
            return "/home/tamas/nata/music";
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
    }

    public int TrackCount
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

    public IReadOnlyList<MusicLibraryEntry> Entries
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

    public async Task<int> RefreshAsync(CancellationToken cancellationToken = default)
    {
        // One scan at a time: the periodic job and a user-triggered rescan can arrive together.
        if (!await _scanGate.WaitAsync(0, cancellationToken))
        {
            return TrackCount;
        }

        ReportProgress(MusicIndexingPhase.Discovering, 0, 0, force: true);
        try
        {
            EnsureLoaded();

            string folder = MusicFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                StatusMessage = string.IsNullOrWhiteSpace(folder)
                    ? "No music folder is configured."
                    : $"Music folder not found: {folder}";
                return TrackCount;
            }

            // Stage one: find out what there is. Until this returns the only honest thing to show is
            // a count that keeps rising, which is why it is reported as its own phase.
            List<string> files = [];
            CollectAudioFiles(
                folder,
                MaxScanDepth,
                files,
                cancellationToken,
                () => ReportProgress(MusicIndexingPhase.Discovering, files.Count, 0, force: false));

            // Stage two: discovery is done, so the total is final and the UI can switch from a
            // spinner to "n of m". Always published — this transition is the point of the split.
            ReportProgress(MusicIndexingPhase.Indexing, files.Count, 0, force: true);

            Dictionary<string, MusicLibraryEntry> known;
            lock (_lock)
            {
                known = _entries.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
            }

            // What readers see while the scan runs. It starts as the cached library and gains each
            // file as that file is read, so the panel grows during a first scan and never loses a
            // row during a rescan — rows whose file is gone are only dropped once the walk is over
            // and the scan actually knows they are gone.
            Dictionary<string, MusicLibraryEntry> published = new(known, StringComparer.OrdinalIgnoreCase);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<MusicLibraryEntry> scanned = new(files.Count);

            // Read but not yet written to the table. Emptied by each flush; whatever is left at the
            // end goes out with the removals.
            List<MusicLibraryEntry> pending = [];
            int changedCount = 0;
            DateTimeOffset lastFlushUtc = DateTimeOffset.UtcNow;

            int processed = 0;
            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReportProgress(MusicIndexingPhase.Indexing, files.Count, processed++, force: false);

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

                if (known.TryGetValue(file, out MusicLibraryEntry? cached)
                    && cached.Size == info.Length
                    && cached.ModifiedUtc == new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero))
                {
                    scanned.Add(cached);
                    continue;
                }

                MusicLibraryEntry entry = ReadEntry(file, info, now);
                scanned.Add(entry);
                pending.Add(entry);
                published[entry.Path] = entry;
                changedCount++;

                if (pending.Count >= ScanFlushBatchSize
                    || DateTimeOffset.UtcNow - lastFlushUtc >= ScanFlushInterval)
                {
                    FlushScan(pending, published);
                    lastFlushUtc = DateTimeOffset.UtcNow;
                }
            }

            // Every file is accounted for; what is left is the database write, which the counts
            // cannot describe. Reported so a watcher sees the run reach its total rather than stop
            // one short of it.
            ReportProgress(MusicIndexingPhase.Indexing, files.Count, files.Count, force: true);

            HashSet<string> live = new(scanned.Select(entry => entry.Path), StringComparer.OrdinalIgnoreCase);
            List<string> removed = [.. known.Keys.Where(path => !live.Contains(path))];

            scanned.Sort(CompareEntries);

            lock (_lock)
            {
                _entries = scanned;
            }

            // The last batch and the deletions in one write: a row is only dropped once the whole
            // folder has been walked, so an interrupted scan can never take live tracks with it.
            Persist(pending, removed);

            LastIndexedUtc = now;
            StatusMessage = $"Indexed {scanned.Count} track{(scanned.Count == 1 ? string.Empty : "s")} from {folder}.";

            if (changedCount > 0 || removed.Count > 0)
            {
                LibraryChanged?.Invoke(this, EventArgs.Empty);
            }

            return scanned.Count;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Music indexing failed: {exception.Message}";
            _logger?.Warning(exception, "Music library indexing failed.");
            return TrackCount;
        }
        finally
        {
            // Idle first, then the gate: releasing it earlier would let the next scan announce its
            // own first stage only to have this line stamp Idle over it.
            ReportProgress(MusicIndexingPhase.Idle, 0, 0, force: true);
            _scanGate.Release();
        }
    }

    /// <summary>
    /// Writes what the scan has read so far to the table and makes it the snapshot every reader
    /// sees. Both halves of the request in one place: the database gets the content, and the panel —
    /// which rebuilds from <see cref="Entries"/> on each progress report — draws it on its next
    /// repaint without needing a scan to finish first.
    /// </summary>
    private void FlushScan(List<MusicLibraryEntry> pending, Dictionary<string, MusicLibraryEntry> published)
    {
        if (pending.Count == 0)
        {
            return;
        }

        Persist(pending, []);
        pending.Clear();

        List<MusicLibraryEntry> snapshot = [.. published.Values];
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
    private void ReportProgress(MusicIndexingPhase phase, int discovered, int indexed, bool force)
    {
        MusicIndexingProgress progress = new(phase, discovered, indexed);
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
            _logger?.Warning(exception, "A music indexing progress handler failed.");
        }
    }

    public IReadOnlyList<MusicSearchResult> Search(string query, int limit = 200)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0)
        {
            return [];
        }

        EnsureLoaded();

        string[] tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return [];
        }

        List<MusicLibraryEntry> entries;
        lock (_lock)
        {
            entries = [.. _entries];
        }

        List<MusicSearchResult> results = [];

        // Artists first, then albums, then individual tracks: the collapsed rows are what makes
        // "add this whole artist" a single keystroke, so they must not be pushed off by the limit.
        foreach (IGrouping<string, MusicLibraryEntry> group in entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Artist))
            .GroupBy(entry => entry.Artist, StringComparer.OrdinalIgnoreCase)
            .Where(group => MatchesAll(group.Key, tokens))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(new MusicSearchResult(
                MusicSearchResultKind.Artist,
                group.Key,
                string.Empty,
                null,
                group.Count())
            {
                TotalDuration = SumDuration(group),
            });
        }

        foreach (IGrouping<(string Artist, string Album), MusicLibraryEntry> group in entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Album))
            .GroupBy(entry => (entry.Artist, entry.Album), AlbumKeyComparer.Instance)
            .Where(group => MatchesAll($"{group.Key.Artist} {group.Key.Album}", tokens))
            .OrderBy(group => group.Key.Album, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(new MusicSearchResult(
                MusicSearchResultKind.Album,
                group.Key.Artist,
                group.Key.Album,
                null,
                group.Count())
            {
                TotalDuration = SumDuration(group),
            });
        }

        foreach (MusicLibraryEntry entry in entries.Where(entry => MatchesAll(Haystack(entry), tokens)))
        {
            if (results.Count >= limit)
            {
                break;
            }

            MusicTrack track = entry.ToTrack();
            results.Add(new MusicSearchResult(
                MusicSearchResultKind.Track,
                entry.Artist,
                entry.Album,
                track,
                1)
            {
                TotalDuration = track.Duration,
            });
        }

        return results.Count > limit ? results[..limit] : results;
    }

    public IReadOnlyList<MusicTrack> ExpandResult(MusicSearchResult result)
    {
        if (result is null)
        {
            return [];
        }

        if (result.Kind == MusicSearchResultKind.Track)
        {
            return result.Track is null ? [] : [result.Track];
        }

        EnsureLoaded();

        List<MusicLibraryEntry> entries;
        lock (_lock)
        {
            entries = [.. _entries];
        }

        IEnumerable<MusicLibraryEntry> matches = result.Kind == MusicSearchResultKind.Artist
            ? entries.Where(entry => string.Equals(entry.Artist, result.Artist, StringComparison.OrdinalIgnoreCase))
            : entries.Where(entry =>
                string.Equals(entry.Album, result.Album, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.Artist, result.Artist, StringComparison.OrdinalIgnoreCase));

        return [.. matches.Select(entry => entry.ToTrack())];
    }

    private static string Haystack(MusicLibraryEntry entry) =>
        $"{entry.Artist} {entry.Album} {entry.Title} {System.IO.Path.GetFileNameWithoutExtension(entry.Path)}";

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

    private static TimeSpan SumDuration(IEnumerable<MusicLibraryEntry> entries) =>
        TimeSpan.FromTicks(entries.Sum(entry => entry.DurationTicks));

    private static int CompareEntries(MusicLibraryEntry left, MusicLibraryEntry right)
    {
        int byArtist = string.Compare(left.Artist, right.Artist, StringComparison.OrdinalIgnoreCase);
        if (byArtist != 0)
        {
            return byArtist;
        }

        int byAlbum = string.Compare(left.Album, right.Album, StringComparison.OrdinalIgnoreCase);
        if (byAlbum != 0)
        {
            return byAlbum;
        }

        return string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
    }

    private static MusicLibraryEntry ReadEntry(string path, FileInfo info, DateTimeOffset now)
    {
        MusicTagReader.MusicTags tags = MusicTagReader.Read(path);

        return new MusicLibraryEntry
        {
            Path = path,
            Title = string.IsNullOrWhiteSpace(tags.Title)
                ? System.IO.Path.GetFileNameWithoutExtension(path)
                : tags.Title,
            Artist = tags.Artist,
            Album = tags.Album,
            DurationTicks = ProbeDuration(path).Ticks,
            Size = info.Length,
            ModifiedUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            IndexedUtc = now,
        };
    }

    /// <summary>
    /// Playing time for formats that decode in managed code. The decoders are constructed directly
    /// rather than through <see cref="AudioDecoderFactory"/> precisely to avoid its ffmpeg
    /// fallbacks: probing a library through ffmpeg would start one process per file. Everything
    /// else stays at zero and gets its duration when the player first opens it.
    /// </summary>
    private static TimeSpan ProbeDuration(string path)
    {
        string extension = System.IO.Path.GetExtension(path);

        try
        {
            if ((extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
                 || extension.Equals(".wave", StringComparison.OrdinalIgnoreCase))
                && WaveAudioDecoder.CanDecode(path))
            {
                using WaveAudioDecoder wave = new(path);
                return wave.Duration;
            }

            if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mp2", StringComparison.OrdinalIgnoreCase))
            {
                using Mp3AudioDecoder mp3 = new(path);
                return mp3.Duration;
            }
        }
        catch
        {
            // A file that cannot be opened still belongs in the index with its tags; only its
            // duration is unknown.
        }

        return TimeSpan.Zero;
    }

    /// <summary>
    /// Enumeration that does not give up on a folder because one entry in it is unreadable. The
    /// default options a bare <c>Directory.EnumerateFiles(path)</c> uses are
    /// <see cref="EnumerationOptions.Compatible"/>, which has <c>IgnoreInaccessible = false</c> — a
    /// single protected entry then throws mid-iteration and takes the rest of the folder with it.
    /// A discovery count nobody can trust is worse than no count at all.
    /// </summary>
    private static readonly EnumerationOptions DiscoveryOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
    };

    /// <summary>
    /// The discovery stage. <paramref name="onFolderScanned"/> is called once per folder rather than
    /// once per file: it is only there to keep the running count moving, and a deep library has
    /// enough folders for that without paying for a callback per file.
    ///
    /// <para>The two listings are guarded separately and each sub-folder is guarded on its own, so
    /// what a failure costs is that one folder — not, as with a single try around the lot, every
    /// sub-tree underneath a folder whose <em>file</em> listing happened to fail.</para>
    /// </summary>
    private static void CollectAudioFiles(
        string folder,
        int depthBudget,
        List<string> files,
        CancellationToken cancellationToken,
        Action? onFolderScanned = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (string file in Enumerate(() => Directory.EnumerateFiles(folder, "*", DiscoveryOptions)))
        {
            if (AudioDecoderFactory.IsAudioFile(file))
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
            CollectAudioFiles(subFolder, depthBudget - 1, files, cancellationToken, onFolderScanned);
        }
    }

    /// <summary>
    /// Materialises a lazy listing behind a catch. Enumerating is what throws, not calling it, so the
    /// guard has to wrap the iteration — and materialising is also what stops one bad entry from
    /// discarding the entries already read.
    /// </summary>
    internal static IReadOnlyList<string> Enumerate(Func<IEnumerable<string>> listing)
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

    /// <summary>
    /// Fills the in-memory copy from the table, once. This is the whole of "the catalog is shown
    /// from the database": nothing else puts rows there until a scan runs, so a panel opened before
    /// the first scan of the session draws the previous run's index.
    ///
    /// <para>The read is marked done only after it succeeds. A database that is momentarily
    /// unavailable — being recreated at start-up, locked by a second instance — would otherwise
    /// leave the catalog empty for the rest of the session, since nothing would ever look again.
    /// Failures are retried, but no more often than <see cref="LoadRetryInterval"/>, because the
    /// catalog panel reads this on every repaint.</para>
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

                List<MusicLibraryEntry> loaded = [.. context.MusicLibraryEntries.AsNoTracking()];
                loaded.Sort(CompareEntries);

                lock (_lock)
                {
                    _entries = loaded;
                }

                LastIndexedUtc = loaded.Count == 0 ? null : loaded.Max(entry => entry.IndexedUtc);
                _isLoaded = true;
            }
            catch (Exception exception)
            {
                _logger?.Warning(exception, "Could not load the cached music library.");
            }
        }
    }

    private void Persist(IReadOnlyList<MusicLibraryEntry> changed, IReadOnlyList<string> removed)
    {
        if (changed.Count == 0 && removed.Count == 0)
        {
            return;
        }

        try
        {
            using iTerminalDbContext context = _dbContextFactory.CreateDbContext();
            context.Database.EnsureCreated();

            foreach (string path in removed)
            {
                MusicLibraryEntry? existing = context.MusicLibraryEntries.Find(path);
                if (existing is not null)
                {
                    context.MusicLibraryEntries.Remove(existing);
                }
            }

            foreach (MusicLibraryEntry entry in changed)
            {
                MusicLibraryEntry? existing = context.MusicLibraryEntries.Find(entry.Path);
                if (existing is null)
                {
                    context.MusicLibraryEntries.Add(new MusicLibraryEntry
                    {
                        Path = entry.Path,
                        Title = entry.Title,
                        Artist = entry.Artist,
                        Album = entry.Album,
                        DurationTicks = entry.DurationTicks,
                        Size = entry.Size,
                        ModifiedUtc = entry.ModifiedUtc,
                        IndexedUtc = entry.IndexedUtc,
                    });
                }
                else
                {
                    existing.Title = entry.Title;
                    existing.Artist = entry.Artist;
                    existing.Album = entry.Album;
                    existing.DurationTicks = entry.DurationTicks;
                    existing.Size = entry.Size;
                    existing.ModifiedUtc = entry.ModifiedUtc;
                    existing.IndexedUtc = entry.IndexedUtc;
                }
            }

            context.SaveChanges();
        }
        catch (Exception exception)
        {
            // A library that cannot be cached still works for this session; it just re-indexes next
            // time, which is not worth failing the scan over.
            _logger?.Warning(exception, "Could not persist the music library index.");
        }
    }

    private sealed class AlbumKeyComparer : IEqualityComparer<(string Artist, string Album)>
    {
        public static readonly AlbumKeyComparer Instance = new();

        public bool Equals((string Artist, string Album) left, (string Artist, string Album) right) =>
            string.Equals(left.Artist, right.Artist, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Album, right.Album, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Artist, string Album) value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Artist),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Album));
    }

}
