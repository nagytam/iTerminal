using iConsole.Services;
using iTerminal.Core.Dialogs.Terminal;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Stocks;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Terminal;

/// <summary>Alt+Shift+F6: a command line that looks a stock up, a security screen for the result, and the watchlist as a monitor.</summary>
[TestClass]
public class MarketTerminalDialogTest
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

    /// <summary>Every symbol but NOPE is quoted; the search returns whatever the test puts in Hits.</summary>
    private sealed class FakeProvider : IStockQuoteProvider
    {
        public List<StockSearchHit> Hits { get; } = [];

        public Task<IReadOnlyDictionary<string, StockQuote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, StockQuote>>(symbols
                .Where(s => s != "NOPE")
                .ToDictionary(s => s, s => new StockQuote(s, s + " Corp", 110m, 100m, "USD", "NASDAQ", DateTime.Now,
                    DayLow: 95m, DayHigh: 115m, YearLow: 50m, YearHigh: 200m, Volume: 1234567), StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyList<StockSearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockSearchHit>>([.. Hits]);
    }

    private static readonly StockQuote Quoted = new("TSLA", "Tesla", 90m, 100m, "USD", "NASDAQ", new DateTime(2026, 11, 16, 21, 0, 0),
        DayLow: 88m, DayHigh: 101m, YearLow: 50m, YearHigh: 200m, Volume: 1234567);

    private static (MarketTerminalDialog Dialog, StockService Service, FakeProvider Provider) Create()
    {
        var provider = new FakeProvider();
        var service = new StockService(new MemorySettings(), provider, null, TimeSpan.FromMilliseconds(40));
        var dialog = new MarketTerminalDialog(service, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
        return (dialog, service, provider);
    }

    /// <summary>The dialog applies a lookup that landed on its idle pass; a test has no idle job, so it runs the pass itself.</summary>
    private static string? PumpedSecuritySymbol(MarketTerminalDialog dialog)
    {
        dialog.OnIdle();
        return dialog.SecurityForTest?.Symbol;
    }

    private static string PumpedMessage(MarketTerminalDialog dialog)
    {
        dialog.OnIdle();
        return dialog.MessageForTest;
    }

    #region Formatting

    [TestMethod]
    public void TheTapeListsEveryQuotedStockWithItsPriceAndChange()
    {
        var tape = MarketTerminalDialog.FormatTape([Quoted, Quoted with { Symbol = "AAPL", Price = 100m }], 120, "waiting");

        Assert.AreEqual(" TSLA 90.00 -10.00%   AAPL 100.00 0.00%", tape);
    }

    [TestMethod]
    public void TheTapeSaysItIsWaitingWhenNothingIsQuoted() =>
        Assert.AreEqual(" waiting", MarketTerminalDialog.FormatTape([], 120, "waiting"));

    [TestMethod]
    public void TheTapeIsCutToTheWidth() =>
        Assert.AreEqual(" TSLA 90.00", MarketTerminalDialog.FormatTape([Quoted], 11, "waiting"));

    [TestMethod]
    public void TheSecurityScreenShowsItsIdentityPriceAndRanges()
    {
        var title = MarketTerminalDialog.FormatSecurityTitle(Quoted, 120);
        var price = MarketTerminalDialog.FormatSecurityPrice(Quoted, 120);
        var range = MarketTerminalDialog.FormatSecurityRange(Quoted, 120);

        StringAssert.Contains(title, "TSLA  Tesla   NASDAQ   USD");
        StringAssert.Contains(price, "90.00 USD");
        StringAssert.Contains(price, "-10.00 (-10.00%)");
        StringAssert.Contains(price, "prev close 100.00 USD");
        StringAssert.Contains(price, "vol 1,234,567");
        StringAssert.Contains(range, "day 88.00–101.00");
        StringAssert.Contains(range, "52w 50.00 ▕");
        StringAssert.Contains(range, "last trade 2026-11-16 21:00");
    }

    #endregion

    #region Dialog

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATypedSymbolIsLookedUpAndShownWithoutBeingWatched()
    {
        var (dialog, service, _) = Create();
        await ShowAsync(dialog);

        await TypeAsync(dialog.InputBoxForTest, "VOD.L");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await WaitUntilAsync(() => PumpedSecuritySymbol(dialog) == "VOD.L", "the typed symbol to be looked up");

        var screen = Render(dialog);
        AssertScreenContains(screen, "VOD.L  VOD.L Corp");
        AssertScreenContains(screen, "110.00 USD");
        Assert.IsFalse(service.Watchlist.Any(stock => stock.Symbol == "VOD.L"), "a lookup must not add the stock to the watchlist");
        dialog.Close();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACompanyNameIsLookedUpThroughItsBestSearchHit()
    {
        var (dialog, _, provider) = Create();
        provider.Hits.Add(new StockSearchHit("VOD.L", "Vodafone", "LSE", "EQUITY"));
        await ShowAsync(dialog);

        await TypeAsync(dialog.InputBoxForTest, "NOPE");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await WaitUntilAsync(() => PumpedSecuritySymbol(dialog) == "VOD.L", "the search hit to be looked up");

        dialog.Close();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnknownSymbolSaysSoAndKeepsThePreviousSecurity()
    {
        var (dialog, _, _) = Create();
        await ShowAsync(dialog);

        await TypeAsync(dialog.InputBoxForTest, "NOPE");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await WaitUntilAsync(() => PumpedMessage(dialog).StartsWith("No quote", StringComparison.Ordinal), "the lookup to report back");

        StringAssert.Contains(dialog.MessageForTest, "No quote for NOPE");
        Assert.IsNull(dialog.SecurityForTest);
        dialog.Close();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnAnEmptyLineShowsTheMonitorsCursorRow()
    {
        var (dialog, service, _) = Create();
        await ShowAsync(dialog);
        await WaitUntilAsync(() => dialog.MonitorRowsShownForTest > 0 && dialog.CursorQuoteForTest is not null, "the monitor to be quoted");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual(service.Watchlist[0].Symbol, dialog.SecurityForTest?.Symbol);
        dialog.Close();
    }

    #endregion
}
