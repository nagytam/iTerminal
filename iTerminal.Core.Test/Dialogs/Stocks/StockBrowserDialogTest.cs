using iConsole.Services;
using iTerminal.Core.Dialogs.Stocks;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Stocks;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Stocks;

/// <summary>Alt+F7: the list is live, and stocks are added from a search and removed with a confirmation.</summary>
[TestClass]
public class StockBrowserDialogTest
{
    private sealed class MemorySettings : ISettingsService
    {
        private readonly Dictionary<string, string> _values = [];
        public bool IsDirty => false;
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public bool? GetAsBool(string key, bool? defaultValue = false) => defaultValue;
        public void Set(string key, string value) => _values[key] = value;
        public void Write() { }
    }

    private sealed class FakeProvider : IStockQuoteProvider
    {
        public decimal Price { get; set; } = 110m;
        public List<StockSearchHit> Hits { get; } = [];

        public Task<IReadOnlyDictionary<string, StockQuote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, StockQuote>>(symbols
                .Where(s => s != "NOPE")
                .ToDictionary(s => s, s => new StockQuote(s, s + " Corp", s == "TSLA" ? 90m : Price, 100m, "USD", "NASDAQ", DateTime.Now,
                    DayLow: 95m, DayHigh: 115m, YearLow: 50m, YearHigh: 200m, Volume: 1234567), StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyList<StockSearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockSearchHit>>([.. Hits]);
    }

    private static (StockBrowserDialog Dialog, StockService Service, FakeProvider Provider) Create()
    {
        var provider = new FakeProvider();
        var service = new StockService(new MemorySettings(), provider, null, TimeSpan.FromMilliseconds(40));
        var dialog = new StockBrowserDialog(service, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
        return (dialog, service, provider);
    }

    private static Task QuotesLandedAsync(StockBrowserDialog dialog) =>
        WaitUntilAsync(() => dialog.RowsForTest.Count > 0 && dialog.RowsForTest.All(r => r.Quote is not null), "every stock to be quoted");

    [TestMethod]
    public async Task ItListsTheDefaultStocksWithLivePrices()
    {
        var (dialog, _, _) = Create();
        await ShowAsync(dialog);
        await QuotesLandedAsync(dialog);

        var screen = Render(dialog);

        AssertScreenContains(screen, "Symbol");
        AssertScreenContains(screen, "Exchange");
        foreach (var name in new[] { "AAPL", "MSFT", "TSLA", "GOOGL", "NVDA", "INTC", "SPCX" })
        {
            AssertScreenContains(screen, name);
        }

        AssertScreenContains(screen, "+10.00%");
        AssertScreenContains(screen, "-10.00%");
        AssertScreenContains(screen, "live, updated");
        dialog.Close();
    }

    [TestMethod]
    public async Task ThePricesFollowTheQuotesWithoutAKeyPress()
    {
        var (dialog, service, provider) = Create();
        await ShowAsync(dialog);
        await QuotesLandedAsync(dialog);

        provider.Price = 120m;

        await WaitUntilAsync(() => service.GetQuote("AAPL")?.Price == 120m && dialog.RowsForTest[0].Quote?.Price == 120m, "the new price to reach the list");
        AssertScreenContains(Render(dialog), "+20.00%");
        dialog.Close();
    }

    [TestMethod]
    public async Task ClosingStopsTheRefreshing()
    {
        var (dialog, service, _) = Create();
        await ShowAsync(dialog);
        await QuotesLandedAsync(dialog);

        dialog.Close();
        await service.LoopTask;
        var updated = service.LastUpdated;
        await Task.Delay(150);

        Assert.AreEqual(updated, service.LastUpdated);
    }

    [TestMethod]
    public async Task TheCursorStockIsDescribedBelowTheList()
    {
        var (dialog, _, _) = Create();
        await ShowAsync(dialog);
        await QuotesLandedAsync(dialog);

        var screen = Render(dialog);

        AssertScreenContains(screen, "day 95.00–115.00");
        AssertScreenContains(screen, "52w 50.00 ▕███▎    ▏ 200.00");
        AssertScreenContains(screen, "vol 1,234,567");
        dialog.Close();
    }

    [TestMethod]
    public async Task RemovingAsksFirstAndOnlyYesRemoves()
    {
        var (dialog, service, _) = Create();
        await ShowAsync(dialog);
        await QuotesLandedAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('d'));
        Assert.IsTrue(dialog.IsConfirmingForTest);
        AssertScreenContains(Render(dialog), "Remove AAPL from the list?");
        await dialog.HandleKeyEventAsync(Char('n'));
        Assert.AreEqual(7, service.Watchlist.Count);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        await dialog.HandleKeyEventAsync(Char('y'));

        await WaitUntilAsync(() => service.Watchlist.Count == 6 && dialog.RowsForTest.Count == 6, "AAPL to leave the list");
        Assert.IsFalse(dialog.RowsForTest.Any(r => r.Stock.Symbol == "AAPL"));
        dialog.Close();
    }

    [TestMethod]
    public async Task AddingSearchesAcrossExchangesAndAddsTheChosenHit()
    {
        var (dialog, service, provider) = Create();
        provider.Hits.Add(new StockSearchHit("VOD", "Vodafone Group", "NASDAQ", "EQUITY"));
        provider.Hits.Add(new StockSearchHit("VOD.L", "Vodafone Group", "London", "EQUITY"));
        await ShowAsync(dialog);
        await QuotesLandedAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('a'));
        Assert.IsTrue(dialog.IsAddingForTest);
        dialog.InputBoxForTest.Text = "vodafone";
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await WaitUntilAsync(() => dialog.HitsForTest.Count == 2, "the hits to be listed");

        var screen = Render(dialog);
        AssertScreenContains(screen, "London");
        AssertScreenContains(screen, "NASDAQ");

        await WaitUntilAsync(() => dialog.MessageForTest.Contains("2 found"), "the search to be reported");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        await WaitUntilAsync(() => service.Watchlist.Any(s => s.Symbol == "VOD.L"), "VOD.L to be added");
        dialog.Close();
    }

    [TestMethod]
    public async Task ASearchWithNoHitsAddsTheTextAsASymbolOnTheSecondEnter()
    {
        var (dialog, service, _) = Create();
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('a'));
        dialog.InputBoxForTest.Text = "sap.de";
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await WaitUntilAsync(() => dialog.MessageForTest.Contains("adds it as a symbol"), "the empty search to be reported");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        await WaitUntilAsync(() => service.Watchlist.Any(s => s.Symbol == "SAP.DE"), "SAP.DE to be added");
        dialog.Close();
    }

    [TestMethod]
    public async Task ASymbolNoOneQuotesIsRefusedInWords()
    {
        var (dialog, service, _) = Create();
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('a'));
        dialog.InputBoxForTest.Text = "nope";
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await WaitUntilAsync(() => dialog.MessageForTest.Contains("adds it as a symbol"), "the empty search to be reported");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        await WaitUntilAsync(() => dialog.MessageForTest.StartsWith("Error: No quote for NOPE"), "the refusal");
        Assert.AreEqual(7, service.Watchlist.Count);
        dialog.Close();
    }

    [TestMethod]
    public async Task EscapeLeavesTheAddFlowBeforeItLeavesTheDialog()
    {
        var (dialog, _, _) = Create();
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('a'));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(dialog.IsAddingForTest);
        AssertScreenContains(Render(dialog), "a:add");
        dialog.Close();
    }

    [TestMethod]
    public async Task AnEmptyListSaysHowToStart()
    {
        var (dialog, service, _) = Create();
        foreach (var stock in service.Watchlist)
        {
            service.Remove(stock.Symbol);
        }

        await ShowAsync(dialog);

        AssertScreenContains(Render(dialog), "The watchlist is empty — press a to add a stock.");
        dialog.Close();
    }
}
