using iTerminal.Core.Services;
using iTerminal.Core.Services.Stocks;
using Moq;

namespace iTerminal.Core.Test.Services.Stocks;

[TestClass]
public class StockServiceTest
{
    private sealed class MemorySettings : ISettingsService
    {
        public Dictionary<string, string> Values { get; } = [];
        public bool IsDirty => false;
        public string? Get(string key) => Values.GetValueOrDefault(key);
        public bool? GetAsBool(string key, bool? defaultValue = false) => defaultValue;
        public void Set(string key, string value) => Values[key] = value;
        public void Write() { }
    }

    private sealed class FakeProvider : IStockQuoteProvider
    {
        public HashSet<string> Unknown { get; } = new(StringComparer.OrdinalIgnoreCase);
        public decimal Price { get; set; } = 110m;
        public int Calls { get; private set; }

        public Task<IReadOnlyDictionary<string, StockQuote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyDictionary<string, StockQuote>>(symbols
                .Where(s => !Unknown.Contains(s))
                .ToDictionary(s => s, s => new StockQuote(s, s + " Corp", Price, 100m, "USD", "NASDAQ", DateTime.Now), StringComparer.OrdinalIgnoreCase));
        }

        public Task<IReadOnlyList<StockSearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockSearchHit>>([new StockSearchHit("X", "X", "NYSE", "EQUITY")]);
    }

    private static (StockService Service, MemorySettings Settings, FakeProvider Provider) Create()
    {
        var settings = new MemorySettings();
        var provider = new FakeProvider();
        return (new StockService(settings, provider, null, TimeSpan.FromMilliseconds(30)), settings, provider);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
        }

        Assert.IsTrue(condition(), "the condition was not met in time");
    }

    [TestMethod]
    public void TheDefaultWatchlistIsAppleMicrosoftTeslaGoogleNvidiaIntelAndSpaceX()
    {
        var (service, _, _) = Create();

        CollectionAssert.AreEqual(
            new[] { "AAPL", "MSFT", "TSLA", "GOOGL", "NVDA", "INTC", "SPCX" },
            service.Watchlist.Select(s => s.Symbol).ToArray());
        Assert.AreEqual("SpaceX", service.Watchlist.Last().Name);
    }

    [TestMethod]
    public async Task AddingAStockKeepsItAcrossRestarts()
    {
        var (service, settings, provider) = Create();

        var stock = await service.AddAsync("vod.l");

        Assert.AreEqual("VOD.L", stock.Symbol);
        Assert.AreEqual("VOD.L Corp", stock.Name);
        var restarted = new StockService(settings, provider);
        Assert.AreEqual("VOD.L", restarted.Watchlist.Last().Symbol);
        Assert.AreEqual(8, restarted.Watchlist.Count);
    }

    [TestMethod]
    public async Task ASymbolTheSourceDoesNotKnowIsNotAdded()
    {
        var (service, _, provider) = Create();
        provider.Unknown.Add("NOPE");

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.AddAsync("nope"));

        StringAssert.Contains(exception.Message, "No quote for NOPE");
        Assert.AreEqual(7, service.Watchlist.Count);
    }

    [TestMethod]
    public async Task AStockAlreadyOnTheListIsNotAddedTwice()
    {
        var (service, _, _) = Create();

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.AddAsync("aapl"));

        StringAssert.Contains(exception.Message, "already on the list");
    }

    [TestMethod]
    public void RemovingAStockTakesItOffTheListAndStaysRemoved()
    {
        var (service, settings, provider) = Create();

        Assert.IsTrue(service.Remove("tsla"));
        Assert.IsFalse(service.Remove("tsla"));

        var restarted = new StockService(settings, provider);
        Assert.IsFalse(restarted.Watchlist.Any(s => s.Symbol == "TSLA"));
        Assert.AreEqual(6, restarted.Watchlist.Count);
    }

    [TestMethod]
    public void AnEmptiedListStaysEmptyInsteadOfReturningToTheDefaults()
    {
        var (service, settings, provider) = Create();
        foreach (var stock in service.Watchlist)
        {
            service.Remove(stock.Symbol);
        }

        Assert.AreEqual(0, new StockService(settings, provider).Watchlist.Count);
    }

    [TestMethod]
    public async Task RefreshingFillsInTheQuotesAndRaisesChanged()
    {
        var (service, _, _) = Create();
        var changes = 0;
        service.Changed += (_, _) => changes++;

        await service.RefreshAsync();

        var quote = service.GetQuote("AAPL");
        Assert.IsNotNull(quote);
        Assert.AreEqual(10m, quote.ChangePercent);
        Assert.IsNotNull(service.LastUpdated);
        Assert.AreEqual(string.Empty, service.LastError);
        Assert.IsTrue(changes > 0);
    }

    [TestMethod]
    public async Task NoQuotesAtAllIsReportedAndKeepsTheOldOnes()
    {
        var (service, _, provider) = Create();
        await service.RefreshAsync();
        foreach (var stock in service.Watchlist)
        {
            provider.Unknown.Add(stock.Symbol);
        }

        await service.RefreshAsync();

        StringAssert.Contains(service.LastError, "No quotes received");
        Assert.IsNotNull(service.GetQuote("AAPL"));
    }

    [TestMethod]
    public async Task AProviderThatThrowsIsReportedNotRaised()
    {
        var settings = new MemorySettings();
        var provider = new Mock<IStockQuoteProvider>();
        provider.Setup(p => p.GetQuotesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        var service = new StockService(settings, provider.Object);

        await service.RefreshAsync();

        Assert.AreEqual("offline", service.LastError);
    }

    [TestMethod]
    public async Task AStockRemovedIsNotQuotedAgain()
    {
        var (service, _, _) = Create();
        await service.RefreshAsync();
        service.Remove("INTC");

        Assert.IsNull(service.GetQuote("INTC"));
        await service.RefreshAsync();
        Assert.IsNull(service.GetQuote("INTC"));
    }

    [TestMethod]
    public async Task QuotesRefreshWhileSomeoneIsWatchingAndStopWhenTheLastWatcherLeaves()
    {
        var (service, _, provider) = Create();

        var first = service.Watch();
        var second = service.Watch();
        await WaitUntilAsync(() => provider.Calls >= 3);
        first.Dispose();
        Assert.IsFalse(service.LoopTask.IsCompleted, "one watcher is still there");

        second.Dispose();
        await service.LoopTask;
        var calls = provider.Calls;
        await Task.Delay(120);

        Assert.AreEqual(calls, provider.Calls);
    }

    [TestMethod]
    public async Task ADisposedWatchHandleCountsOnlyOnce()
    {
        var (service, _, provider) = Create();
        var first = service.Watch();
        var second = service.Watch();

        first.Dispose();
        first.Dispose();
        var before = provider.Calls;
        await WaitUntilAsync(() => provider.Calls > before + 1);

        Assert.IsFalse(service.LoopTask.IsCompleted);
        second.Dispose();
    }
}
