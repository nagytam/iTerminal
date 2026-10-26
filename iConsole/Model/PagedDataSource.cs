namespace iConsole.Model;

/// <summary>
/// An <see cref="IVirtualDataSource{T}"/> that loads its rows a page at a time, in the background, as the control asks for
/// them. Only the pages near the screen are kept (the least recently used go first), a page already being loaded is not
/// asked for twice, and a page that fails is not retried on every frame: it stays failed, with <see cref="LastError"/> set,
/// until <see cref="Retry"/> or <see cref="Reset"/>.
/// </summary>
/// <remarks>
/// <see cref="Reset"/> throws away everything loaded and cancels what is loading, for a new query or a changed filter: a
/// page that was already on its way when the reset happened is dropped on arrival and never shown. The fetch delegate runs
/// on the thread pool, never on the caller's thread, so it may block.
/// </remarks>
public sealed class PagedDataSource<T> : IVirtualDataSource<T>, IDisposable
{
    private readonly Func<int, int, CancellationToken, Task<IReadOnlyList<T>>> _fetchPage;
    private readonly int _pageSize;
    private readonly int _maxCachedPages;
    private readonly int _readAheadPages;
    private readonly int _maxInFlight;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, IReadOnlyList<T>> _pages = [];
    private readonly LinkedList<int> _recent = new();
    private readonly Dictionary<int, LinkedListNode<int>> _recentNodes = [];
    private readonly HashSet<int> _inFlight = [];
    private readonly HashSet<int> _failed = [];
    private CancellationTokenSource _cancellation = new();
    private int _count;
    private int _generation;
    private bool _disposed;

    /// <param name="fetchPage">Loads <c>count</c> rows from <c>start</c>; may return fewer at the end.</param>
    /// <param name="count">The number of rows.</param>
    /// <param name="pageSize">Rows per request.</param>
    /// <param name="maxCachedPages">How many pages are kept; never fewer than the pages one screen needs.</param>
    /// <param name="readAheadPages">Pages loaded beyond each edge of the visible rows, so scrolling meets rows that are already there.</param>
    /// <param name="maxInFlight">Requests running at once.</param>
    public PagedDataSource(
        Func<int, int, CancellationToken, Task<IReadOnlyList<T>>> fetchPage,
        int count,
        int pageSize = 100,
        int maxCachedPages = 32,
        int readAheadPages = 1,
        int maxInFlight = 4)
    {
        ArgumentNullException.ThrowIfNull(fetchPage);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCachedPages, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(readAheadPages);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxInFlight, 1);
        _fetchPage = fetchPage;
        _count = count;
        _pageSize = pageSize;
        _maxCachedPages = maxCachedPages;
        _readAheadPages = readAheadPages;
        _maxInFlight = maxInFlight;
    }

    public event EventHandler? Changed;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <summary>The error of the last page that failed to load, or null.</summary>
    public Exception? LastError { get; private set; }

    /// <summary>Pages loaded and kept right now.</summary>
    public int CachedPageCount
    {
        get
        {
            lock (_gate)
            {
                return _pages.Count;
            }
        }
    }

    /// <summary>True while any page is being loaded.</summary>
    public bool IsLoading
    {
        get
        {
            lock (_gate)
            {
                return _inFlight.Count > 0;
            }
        }
    }

    public bool TryGet(int index, out T? item)
    {
        lock (_gate)
        {
            if (index >= 0 && index < _count)
            {
                int page = index / _pageSize;
                int offset = index % _pageSize;
                if (_pages.TryGetValue(page, out IReadOnlyList<T>? rows) && offset < rows.Count)
                {
                    Touch(page);
                    item = rows[offset];
                    return true;
                }
            }
        }

        item = default;
        return false;
    }

    public void Prefetch(int start, int count)
    {
        List<(int Page, int Generation, CancellationToken Token)> toLoad = [];
        lock (_gate)
        {
            if (_disposed || _count == 0 || count <= 0)
            {
                return;
            }

            int first = Math.Clamp(start, 0, _count - 1) / _pageSize;
            int last = Math.Clamp(start + count - 1, 0, _count - 1) / _pageSize;
            int lastPage = (_count - 1) / _pageSize;

            // Visible pages first, then the read-ahead, so the rows on screen are the first to be asked for.
            List<int> wanted = [];
            for (int page = first; page <= last; page++)
            {
                wanted.Add(page);
            }

            for (int ahead = 1; ahead <= _readAheadPages; ahead++)
            {
                if (last + ahead <= lastPage)
                {
                    wanted.Add(last + ahead);
                }

                if (first - ahead >= 0)
                {
                    wanted.Add(first - ahead);
                }
            }

            foreach (int page in wanted)
            {
                if (_pages.ContainsKey(page))
                {
                    Touch(page);
                    continue;
                }

                if (_inFlight.Contains(page) || _failed.Contains(page))
                {
                    continue;
                }

                if (_inFlight.Count >= _maxInFlight)
                {
                    break;
                }

                _inFlight.Add(page);
                toLoad.Add((page, _generation, _cancellation.Token));
            }
        }

        foreach ((int page, int generation, CancellationToken token) in toLoad)
        {
            // On the thread pool, not inline: the delegate may block, and this is called from Draw.
            _ = Task.Run(() => LoadAsync(page, generation, token));
        }
    }

    /// <summary>Drops everything loaded, cancels what is loading, and starts again with <paramref name="count"/> rows.</summary>
    public void Reset(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        CancellationTokenSource old;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            old = _cancellation;
            _cancellation = new CancellationTokenSource();
            _generation++;
            _count = count;
            _pages.Clear();
            _recent.Clear();
            _recentNodes.Clear();
            _inFlight.Clear();
            _failed.Clear();
            LastError = null;
        }

        old.Cancel();
        old.Dispose();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Changes the number of rows without throwing away the pages already loaded, for a source that grows or shrinks at the end.</summary>
    public void SetCount(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        lock (_gate)
        {
            if (_count == count)
            {
                return;
            }

            _count = count;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lets pages that failed be asked for again the next time they are on screen.</summary>
    public void Retry()
    {
        lock (_gate)
        {
            _failed.Clear();
            LastError = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            cancellation = _cancellation;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private async Task LoadAsync(int page, int generation, CancellationToken token)
    {
        IReadOnlyList<T>? rows = null;
        Exception? error = null;
        try
        {
            rows = await _fetchPage(page * _pageSize, _pageSize, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            error = exception;
        }

        lock (_gate)
        {
            // A reset since this request started: whatever came back belongs to a query that is gone.
            if (generation != _generation)
            {
                return;
            }

            _inFlight.Remove(page);
            if (rows is not null)
            {
                _pages[page] = rows;
                Touch(page);
                Evict();
            }
            else if (error is not null)
            {
                _failed.Add(page);
                LastError = error;
            }
            else
            {
                // Cancelled without a reset (the source was disposed): nothing to show.
                return;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Touch(int page)
    {
        if (_recentNodes.TryGetValue(page, out LinkedListNode<int>? node))
        {
            _recent.Remove(node);
            _recent.AddFirst(node);
        }
        else
        {
            _recentNodes[page] = _recent.AddFirst(page);
        }
    }

    private void Evict()
    {
        while (_pages.Count > _maxCachedPages && _recent.Last is { } oldest)
        {
            _recent.RemoveLast();
            _recentNodes.Remove(oldest.Value);
            _pages.Remove(oldest.Value);
        }
    }
}
