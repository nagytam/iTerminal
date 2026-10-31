using iConsole.Localization;
using Serilog;

namespace iTerminal.Core.Services.Search;

/// <summary>
/// The one search the app has: started from the F7 dialog, read by that dialog and by the
/// <c>search://</c> panel.
///
/// <para>A singleton because the results outlive the dialog that asked for them — "put the hits in
/// the panel" is the point of a file manager's search, and the panel is built after the dialog has
/// closed. Starting a search cancels the one before it, and every result write is checked against
/// a generation counter, so a cancelled walk that finishes a file after the new search started
/// cannot add its hit to the new list.</para>
/// </summary>
public sealed class FileSearchService(ILogger? logger = null) : IFileSearchService
{
    /// <summary>The fastest <see cref="ResultsChanged"/> is raised while a search runs.</summary>
    public static readonly TimeSpan NotifyInterval = TimeSpan.FromMilliseconds(250);

    private readonly Lock _lock = new();
    private readonly List<FileSearchHit> _hits = [];
    private CancellationTokenSource? _cancellation;
    private int _generation;
    private long _lastNotifyTicks;

    public FileSearchQuery? Query { get; private set; }

    public FileSearchProgress Progress { get; private set; } = new(0, 0, 0, string.Empty);

    public bool IsRunning { get; private set; }

    public string StatusMessage { get; private set; } = string.Empty;

    /// <summary>Moves on with every change to the results, so a poller can tell cheaply whether to redraw.</summary>
    public int Version { get; private set; }

    /// <summary>The search started last; nothing waits on it — it exists so a test can.</summary>
    public Task SearchTask { get; private set; } = Task.CompletedTask;

    public event EventHandler? ResultsChanged;

    public IReadOnlyList<FileSearchHit> Hits
    {
        get
        {
            lock (_lock)
            {
                return [.. _hits];
            }
        }
    }

    public Task StartAsync(FileSearchQuery query)
    {
        CancellationTokenSource cancellation = new();
        int generation;
        lock (_lock)
        {
            _cancellation?.Cancel();
            _cancellation = cancellation;
            generation = ++_generation;
            _hits.Clear();
            Query = query;
            Progress = new FileSearchProgress(0, 0, 0, query.StartFolder);
            IsRunning = true;
            StatusMessage = UiText.Get("Msg.Searching", "Searching…");
            Version++;
        }

        Notify(force: true);

        SearchTask = Task.Run(() =>
        {
            try
            {
                var final = FileSearch.Run(
                    query,
                    hit =>
                    {
                        lock (_lock)
                        {
                            if (generation != _generation)
                            {
                                throw new OperationCanceledException();
                            }

                            _hits.Add(hit);
                            Version++;
                        }

                        Notify(force: false);
                    },
                    progress =>
                    {
                        if (generation == Volatile.Read(ref _generation))
                        {
                            Progress = progress;
                            Notify(force: false);
                        }
                    },
                    cancellation.Token);

                Finish(generation, Describe(final), final);
            }
            catch (OperationCanceledException)
            {
                Finish(generation, "Search stopped.", null);
            }
            catch (Exception exception)
            {
                logger?.Warning(exception, "File search failed");
                Finish(generation, $"Search failed: {exception.Message}", null);
            }
        });

        return SearchTask;
    }

    public void Cancel()
    {
        lock (_lock)
        {
            _cancellation?.Cancel();
        }
    }

    private void Finish(int generation, string message, FileSearchProgress? final)
    {
        lock (_lock)
        {
            if (generation != _generation)
            {
                return;
            }

            if (final is not null)
            {
                Progress = final;
            }

            IsRunning = false;
            StatusMessage = message;
            Version++;
        }

        Notify(force: true);
    }

    internal static string Describe(FileSearchProgress progress)
    {
        var found = progress.Hits == 0
            ? "Nothing found"
            : $"Found {progress.Hits:N0}{(progress.Hits >= FileSearch.MaxHits ? "+ (stopped at the limit)" : string.Empty)}";
        return $"{found} in {progress.FilesScanned:N0} file{(progress.FilesScanned == 1 ? string.Empty : "s")}, {progress.FoldersScanned:N0} folder{(progress.FoldersScanned == 1 ? string.Empty : "s")}.";
    }

    /// <summary>
    /// Raised from the walk's own thread, so it is throttled: a search over a source tree finds
    /// hundreds of hits a second, and every listener repaints on each.
    /// </summary>
    private void Notify(bool force)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastNotifyTicks);
        if (!force && now - last < NotifyInterval.TotalMilliseconds)
        {
            return;
        }

        Interlocked.Exchange(ref _lastNotifyTicks, now);
        try
        {
            ResultsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            logger?.Warning(exception, "A search results listener failed");
        }
    }
}

/// <summary>See <see cref="FileSearchService"/>.</summary>
public interface IFileSearchService
{
    FileSearchQuery? Query { get; }
    IReadOnlyList<FileSearchHit> Hits { get; }
    FileSearchProgress Progress { get; }
    bool IsRunning { get; }
    string StatusMessage { get; }
    int Version { get; }

    /// <summary>Raised, at most every <see cref="FileSearchService.NotifyInterval"/>, while results arrive, and once at the end.</summary>
    event EventHandler? ResultsChanged;

    /// <summary>Cancels any running search and starts <paramref name="query"/> on a background task; returns that task.</summary>
    Task StartAsync(FileSearchQuery query);

    void Cancel();
}
