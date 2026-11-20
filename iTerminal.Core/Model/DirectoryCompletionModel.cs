using iConsole.Data;
using iConsole.Model;
using iTerminal.Core.Services;

namespace iTerminal.Core.Model;

/// <summary>What <see cref="DirectoryCompletionModel"/> offers from a listed folder.</summary>
public enum DirectoryCompletionMode
{
    /// <summary>Folders only: for a box that takes a destination folder.</summary>
    FoldersOnly,

    /// <summary>Folders and files: for a box that takes the path of a file that may not exist yet, or one that does.</summary>
    FilesAndFolders,
}

/// <summary>
/// Completes a typed path from what is actually on disk, one path segment at a time: the text up to the last
/// separator is the folder that is listed, and what follows it is the prefix a child's name must start with.
/// Typing <c>C:\Users\ta</c> offers <c>C:\Users\tamas\</c> without the user ever having been there.
///
/// <para>Every match is the whole text the box should then hold (Tab replaces the line), built from the text as
/// typed — its own separator and its own case up to the last segment — so the ghost text after the cursor lines
/// up. Folders come first and end in the separator, so the next segment can be typed straight away. An optional
/// history model (the paths typed or visited before) is offered after the live matches, and it is what
/// <see cref="InsertCompletion"/> and <see cref="Completions"/> belong to.</para>
///
/// <para>Only a plain local path, or <c>file://</c>, is listed: no other scheme has a listing seam
/// (<see cref="IDirectoryLister"/> is local), so for those only the history is offered.</para>
///
/// <para>Listing is I/O, hence <see cref="IBlockingCompletionModel"/>: <c>AutoCompletion</c> runs every update on a
/// background task. A folder is listed once and reused for two seconds, so typing within it costs one listing, and a
/// listing that takes longer than three quarters of a second (a dead share) counts as empty rather than holding the
/// completion. Updates can overlap — one per keystroke — so each builds its list on its own and the newest one
/// to have started is the only one that publishes.</para>
/// </summary>
public sealed class DirectoryCompletionModel : IBlockingCompletionModel
{
    /// <summary>The most matches one update offers.</summary>
    public const int MaxMatches = 200;

    /// <summary>The most children read from one folder.</summary>
    internal const int MaxListed = 5000;

    internal static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan ListTimeout = TimeSpan.FromMilliseconds(750);

    private readonly IDirectoryLister _lister;
    private readonly DirectoryCompletionMode _mode;
    private readonly ICompletionModel? _history;
    private readonly HashSet<string>? _extensions;
    private readonly Func<long> _clock;

    private readonly object _cacheGate = new();
    private string? _cachedPath;
    private IReadOnlyList<DirectoryChild> _cached = [];
    private long _cachedAt;
    private Task<IReadOnlyList<DirectoryChild>>? _inflight;
    private string? _inflightPath;

    private readonly object _publishGate = new();
    private long _sequence;
    private long _published;

    /// <param name="lister">Reads the folders.</param>
    /// <param name="mode">Folders only, or folders and files.</param>
    /// <param name="history">Paths to offer after the live matches, and where <see cref="InsertCompletion"/> remembers.</param>
    /// <param name="fileExtensions">With <see cref="DirectoryCompletionMode.FilesAndFolders"/>: the extensions (".iso") a file must have to be offered. Null offers every file.</param>
    /// <param name="clock">Milliseconds, for the cache. Tests move it by hand.</param>
    public DirectoryCompletionModel(
        IDirectoryLister lister,
        DirectoryCompletionMode mode = DirectoryCompletionMode.FoldersOnly,
        ICompletionModel? history = null,
        IEnumerable<string>? fileExtensions = null,
        Func<long>? clock = null)
    {
        _lister = lister;
        _mode = mode;
        _history = history;
        _extensions = fileExtensions is null ? null : new HashSet<string>(fileExtensions, StringComparer.OrdinalIgnoreCase);
        _clock = clock ?? (() => Environment.TickCount64);
    }

    public ConcurrentObservableCollection<string> MatchingValues { get; } = [];

    public Task? UpdateTask { get; set; }

    /// <summary>The history's completions: there is no fixed list to enumerate for a folder that is read on demand.</summary>
    public IEnumerable<string> Completions => _history?.Completions ?? [];

    public void InsertCompletion(string value) => _history?.InsertCompletion(value);

    public void UpdateMatchingValues(string value)
    {
        value ??= string.Empty;
        long sequence = Interlocked.Increment(ref _sequence);

        List<string> matches = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        if (TryParse(value, out string folderText, out string listPath, out string prefix, out char separator))
        {
            IEnumerable<DirectoryChild> children = ListCached(listPath)
                .Where(child => child.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && IsOffered(child))
                .OrderBy(child => child.IsFolder ? 0 : 1)
                .ThenBy(child => child.Name, StringComparer.OrdinalIgnoreCase);

            foreach (DirectoryChild child in children)
            {
                if (matches.Count >= MaxMatches)
                {
                    break;
                }

                string match = folderText + child.Name + (child.IsFolder ? separator.ToString() : string.Empty);
                if (seen.Add(Key(match)))
                {
                    matches.Add(match);
                }
            }
        }

        if (_history is not null)
        {
            // Prefix matches before ones that merely contain the text: the box greys the first match after the cursor,
            // and only a prefix match can extend what was typed.
            IEnumerable<string> remembered = _history.Completions
                .Where(c => !string.IsNullOrWhiteSpace(c) && c.Contains(value, StringComparison.InvariantCultureIgnoreCase))
                .OrderBy(c => c.StartsWith(value, StringComparison.InvariantCultureIgnoreCase) ? 0 : 1);
            foreach (string path in remembered)
            {
                if (matches.Count >= MaxMatches)
                {
                    break;
                }

                if (seen.Add(Key(path)))
                {
                    matches.Add(path);
                }
            }
        }

        // The newest update to have started is the only one allowed to publish: an older one that finishes last
        // (a slow listing) must not put its matches over the newer ones.
        lock (_publishGate)
        {
            if (sequence < _published)
            {
                return;
            }

            _published = sequence;
            MatchingValues.ReplaceAll(matches);
        }
    }

    private bool IsOffered(DirectoryChild child)
    {
        if (child.IsFolder)
        {
            return true;
        }

        if (_mode == DirectoryCompletionMode.FoldersOnly)
        {
            return false;
        }

        return _extensions is null || _extensions.Contains(Path.GetExtension(child.Name));
    }

    /// <summary>A path without its trailing separator, so <c>C:\a</c> from the history and <c>C:\a\</c> from the disk are one match.</summary>
    private static string Key(string path) => path.TrimEnd('/', '\\');

    /// <summary>
    /// Splits the typed text into the folder as typed (<paramref name="folderText"/>, with its separator), the path to
    /// list, the prefix after the last separator and the separator used. False when there is nothing to list: no
    /// separator yet, or a scheme that is not <c>file://</c>.
    /// </summary>
    internal static bool TryParse(string text, out string folderText, out string listPath, out string prefix, out char separator)
    {
        folderText = listPath = prefix = string.Empty;
        separator = Path.DirectorySeparatorChar;

        int last = text.LastIndexOfAny(['/', '\\']);
        if (last < 0)
        {
            return false;
        }

        folderText = text[..(last + 1)];
        prefix = text[(last + 1)..];
        separator = text[last];
        listPath = folderText;

        if (text.Contains("://", StringComparison.Ordinal))
        {
            if (!text.StartsWith(Container.FILE, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // file:///C:/Users/ -> C:/Users/ ; file:///home/ -> /home/
            listPath = folderText[Container.FILE.Length..];
            if (listPath.Length > 2 && listPath[0] == '/' && listPath[2] == ':')
            {
                listPath = listPath[1..];
            }
        }

        return true;
    }

    private IReadOnlyList<DirectoryChild> ListCached(string path)
    {
        Task<IReadOnlyList<DirectoryChild>> listing;
        lock (_cacheGate)
        {
            if (_cachedPath is not null
                && string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase)
                && _clock() - _cachedAt < CacheLifetime.TotalMilliseconds)
            {
                return _cached;
            }

            // One listing per folder at a time: while a slow one is still running, the next keystroke waits on it
            // rather than starting another thread that would block just as long.
            if (_inflight is { IsCompleted: false } running && string.Equals(_inflightPath, path, StringComparison.OrdinalIgnoreCase))
            {
                listing = running;
            }
            else
            {
                listing = Task.Run(() => _lister.List(path, MaxListed));
                _inflight = listing;
                _inflightPath = path;

                // A listing that outlasts the wait below is not lost: it fills the cache when it ends, so the next
                // keystroke has it. A timeout is therefore never cached as an empty folder.
                _ = listing.ContinueWith(done => Store(path, done), TaskScheduler.Default);
            }
        }

        try
        {
            if (listing.Wait(ListTimeout))
            {
                Store(path, listing);
                return listing.Result;
            }
        }
        catch (Exception)
        {
            // A lister that throws (the contract says it does not) offers nothing.
        }

        return [];
    }

    private void Store(string path, Task<IReadOnlyList<DirectoryChild>> listing)
    {
        if (!listing.IsCompletedSuccessfully)
        {
            return;
        }

        lock (_cacheGate)
        {
            _cachedPath = path;
            _cached = listing.Result;
            _cachedAt = _clock();
        }
    }
}
