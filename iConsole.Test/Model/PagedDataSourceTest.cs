using iConsole.Model;

namespace iConsole.Test.Model;

[TestClass]
public class PagedDataSourceTest
{
    private sealed class Fetcher
    {
        private int _calls;
        private readonly List<int> _starts = [];
        private readonly Lock _gate = new();

        public Func<int, Task<IReadOnlyList<int>>>? Behaviour { get; set; }
        public int Calls => Volatile.Read(ref _calls);
        public CancellationToken LastToken { get; private set; }

        public int[] Starts
        {
            get
            {
                lock (_gate)
                {
                    return [.. _starts];
                }
            }
        }

        public Task<IReadOnlyList<int>> Fetch(int start, int count, CancellationToken token)
        {
            Interlocked.Increment(ref _calls);
            lock (_gate)
            {
                _starts.Add(start);
                LastToken = token;
            }

            return Behaviour is not null ? Behaviour(start) : Task.FromResult<IReadOnlyList<int>>(Rows(start, count, int.MaxValue));
        }
    }

    private static IReadOnlyList<int> Rows(int start, int count, int total)
    {
        return [.. Enumerable.Range(start, Math.Max(0, Math.Min(count, total - start)))];
    }

    private static void WaitFor(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }

        Assert.IsTrue(condition(), "timed out waiting");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARowIsMissingUntilItsPageHasLoaded()
    {
        Fetcher fetcher = new();
        using PagedDataSource<int> source = new(fetcher.Fetch, 1000, pageSize: 10, readAheadPages: 0);
        int changed = 0;
        source.Changed += (_, _) => Interlocked.Increment(ref changed);

        Assert.IsFalse(source.TryGet(25, out _));
        source.Prefetch(25, 1);
        WaitFor(() => source.TryGet(25, out _));

        Assert.IsTrue(source.TryGet(25, out int item));
        Assert.AreEqual(25, item);
        Assert.IsTrue(changed >= 1);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APageBeingLoadedIsNotAskedForTwice()
    {
        TaskCompletionSource<IReadOnlyList<int>> gate = new();
        Fetcher fetcher = new() { Behaviour = _ => gate.Task };
        using PagedDataSource<int> source = new(fetcher.Fetch, 1000, pageSize: 10, readAheadPages: 0);

        source.Prefetch(0, 5);
        source.Prefetch(0, 5);
        source.Prefetch(3, 2);
        WaitFor(() => fetcher.Calls == 1);
        Thread.Sleep(50);

        Assert.AreEqual(1, fetcher.Calls);
        Assert.IsTrue(source.IsLoading);
        gate.SetResult(Rows(0, 10, 1000));
        WaitFor(() => !source.IsLoading);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReadAheadLoadsTheNeighbouringPages()
    {
        Fetcher fetcher = new();
        using PagedDataSource<int> source = new(fetcher.Fetch, 100, pageSize: 10, readAheadPages: 1);

        source.Prefetch(25, 5);
        WaitFor(() => source.CachedPageCount == 3);

        CollectionAssert.AreEquivalent(new[] { 10, 20, 30 }, fetcher.Starts);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheLeastRecentlyUsedPagesAreDroppedFirst()
    {
        Fetcher fetcher = new();
        using PagedDataSource<int> source = new(fetcher.Fetch, 1000, pageSize: 10, maxCachedPages: 2, readAheadPages: 0);

        foreach (int start in new[] { 0, 10, 20 })
        {
            source.Prefetch(start, 1);
            WaitFor(() => source.TryGet(start, out _));
        }

        Assert.AreEqual(2, source.CachedPageCount);
        Assert.IsFalse(source.TryGet(0, out _), "the oldest page is gone");
        Assert.IsTrue(source.TryGet(10, out _));
        Assert.IsTrue(source.TryGet(20, out _));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AShortLastPageEndsTheRows()
    {
        Fetcher fetcher = new() { Behaviour = start => Task.FromResult(Rows(start, 10, 25)) };
        using PagedDataSource<int> source = new(fetcher.Fetch, 25, pageSize: 10, readAheadPages: 0);

        source.Prefetch(24, 1);
        WaitFor(() => source.TryGet(24, out _));

        Assert.IsFalse(source.TryGet(25, out _));
        Assert.IsFalse(source.TryGet(-1, out _));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AResetDropsAPageThatWasStillOnItsWay()
    {
        TaskCompletionSource<IReadOnlyList<int>> gate = new();
        Fetcher fetcher = new() { Behaviour = _ => gate.Task };
        using PagedDataSource<int> source = new(fetcher.Fetch, 1000, pageSize: 10, readAheadPages: 0);
        source.Prefetch(0, 1);
        WaitFor(() => fetcher.Calls == 1);
        CancellationToken token = fetcher.LastToken;

        source.Reset(50);
        gate.SetResult(Rows(0, 10, 1000));
        Thread.Sleep(100);

        Assert.IsTrue(token.IsCancellationRequested, "the stale request is cancelled");
        Assert.AreEqual(50, source.Count);
        Assert.IsFalse(source.TryGet(0, out _), "what the old query returned is never shown");
        Assert.AreEqual(0, source.CachedPageCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFailedPageIsNotAskedForAgainUntilRetry()
    {
        int attempts = 0;
        Fetcher fetcher = new()
        {
            Behaviour = start =>
            {
                Interlocked.Increment(ref attempts);
                return Interlocked.CompareExchange(ref attempts, 0, 0) == 1
                    ? Task.FromException<IReadOnlyList<int>>(new IOException("down"))
                    : Task.FromResult(Rows(start, 10, 1000));
            },
        };
        using PagedDataSource<int> source = new(fetcher.Fetch, 1000, pageSize: 10, readAheadPages: 0);

        source.Prefetch(0, 1);
        WaitFor(() => source.LastError is not null);
        source.Prefetch(0, 1);
        source.Prefetch(0, 1);
        Thread.Sleep(50);

        Assert.AreEqual(1, fetcher.Calls, "no retry storm while the page is drawn every frame");
        Assert.IsInstanceOfType<IOException>(source.LastError);

        source.Retry();
        source.Prefetch(0, 1);
        WaitFor(() => source.TryGet(0, out _));

        Assert.AreEqual(2, fetcher.Calls);
        Assert.IsNull(source.LastError);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NoMoreThanTheLimitOfRequestsRunAtOnce()
    {
        TaskCompletionSource<IReadOnlyList<int>> gate = new();
        Fetcher fetcher = new() { Behaviour = _ => gate.Task };
        using PagedDataSource<int> source = new(fetcher.Fetch, 1000, pageSize: 10, readAheadPages: 0, maxInFlight: 2);

        source.Prefetch(0, 100);
        WaitFor(() => fetcher.Calls == 2);
        Thread.Sleep(50);

        Assert.AreEqual(2, fetcher.Calls);
        gate.SetResult(Rows(0, 10, 1000));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetCountKeepsTheLoadedPages()
    {
        Fetcher fetcher = new();
        using PagedDataSource<int> source = new(fetcher.Fetch, 100, pageSize: 10, readAheadPages: 0);
        source.Prefetch(0, 1);
        WaitFor(() => source.TryGet(0, out _));

        source.SetCount(500);

        Assert.AreEqual(500, source.Count);
        Assert.IsTrue(source.TryGet(0, out _));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnEmptySourceAsksForNothing()
    {
        Fetcher fetcher = new();
        using PagedDataSource<int> source = new(fetcher.Fetch, 0, pageSize: 10);

        source.Prefetch(0, 10);
        Thread.Sleep(30);

        Assert.AreEqual(0, fetcher.Calls);
    }
}
