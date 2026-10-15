using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Dialogs.Portfolio;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Portfolio;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Portfolio;

/// <summary>
/// Ctrl+P: holdings joined against live quotes. Everything the dialog does goes through a
/// background task, so the tests drive it with keys and then wait for it to settle.
/// </summary>
[TestClass]
public class PortfolioManagerDialogBehaviourTest
{
    private sealed class FakePortfolioService : IPortfolioService
    {
        private readonly object _lock = new();
        private int _nextId = 1;

        public List<PortfolioHolding> Holdings { get; } = [];
        public Exception? UpdateFailure { get; set; }

        public PortfolioHolding Add(string symbol, decimal quantity, decimal cost, string? exchange = null)
        {
            var holding = new PortfolioHolding { Id = _nextId++, Symbol = symbol, Exchange = exchange, Quantity = quantity, CostBasisPerUnit = cost, Currency = "USD" };
            Holdings.Add(holding);
            return holding;
        }

        private static PortfolioHolding Copy(PortfolioHolding h) =>
            new() { Id = h.Id, Symbol = h.Symbol, Exchange = h.Exchange, Quantity = h.Quantity, CostBasisPerUnit = h.CostBasisPerUnit, Currency = h.Currency };

        public Task<List<PortfolioHolding>> GetHoldingsAsync()
        {
            lock (_lock)
            {
                return Task.FromResult(Holdings.Select(Copy).ToList());
            }
        }

        public Task<PortfolioHolding> AddHoldingAsync(string symbol, string? exchange, decimal quantity, decimal costBasisPerUnit, string currency, DateTime? acquiredDate, string? notes)
        {
            lock (_lock)
            {
                var holding = Add(symbol, quantity, costBasisPerUnit, exchange);
                holding.Currency = currency;
                return Task.FromResult(holding);
            }
        }

        public Task UpdateHoldingAsync(PortfolioHolding holding)
        {
            if (UpdateFailure is not null)
            {
                throw UpdateFailure;
            }

            lock (_lock)
            {
                var index = Holdings.FindIndex(h => h.Id == holding.Id);
                Holdings[index] = Copy(holding);
            }

            return Task.CompletedTask;
        }

        public Task DeleteHoldingAsync(int id)
        {
            lock (_lock)
            {
                Holdings.RemoveAll(h => h.Id == id);
            }

            return Task.CompletedTask;
        }
    }

    private static (PortfolioManagerDialog Dialog, FakePortfolioService Service, OnlineQuoteService Quotes, Mock<IQuoteProvider> Provider) Create()
    {
        var service = new FakePortfolioService();
        var quotes = new OnlineQuoteService();
        var provider = new Mock<IQuoteProvider>();
        provider.Setup(p => p.GetQuotesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, StooqQuote>());
        var dialog = new PortfolioManagerDialog(service, quotes, provider.Object, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
        return (dialog, service, quotes, provider);
    }

    private static Task SettleAsync(PortfolioManagerDialog dialog, int rows) =>
        WaitUntilAsync(() => !dialog.IsLoadingForTest && dialog.RowsForTest.Count == rows, $"{rows} holdings to be listed");

    private static async Task EnterAsync(PortfolioManagerDialog dialog, string text)
    {
        dialog.InputBoxForTest.Text = text;
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
    }

    private static void Quote(OnlineQuoteService quotes, string symbol, decimal price)
    {
        var instrument = new Instrument { Id = Guid.NewGuid(), Name = symbol, Currency = "USD", InstrumentType = InstrumentType.Stock };
        instrument.InstrumentValues.Add(new InstrumentValue
        {
            Id = Guid.NewGuid(), InstrumentId = instrument.Id, Value = price, ValueDateTime = DateTime.Now,
            ValueType = InstrumentValueType.Intraday, Source = "test", SourceDateTime = DateTime.Now,
        });
        quotes.Instruments[symbol] = instrument;
    }

    [TestMethod]
    public async Task ColumnsHaveHeadings()
    {
        var (dialog, service, _, _) = Create();
        service.Add("AAPL", 10, 100);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 1);

        var screen = Render(dialog);

        AssertScreenContains(screen, "Symbol");
        AssertScreenContains(screen, "Gain/loss");
        AssertScreenContains(screen, "Portfolio — 1 holding");
    }

    [TestMethod]
    public async Task AnEmptyPortfolioSaysHowToStart()
    {
        var (dialog, _, _, _) = Create();
        await ShowAsync(dialog);
        await SettleAsync(dialog, 0);

        AssertScreenContains(Render(dialog), "No holdings yet — press a to add one.");
    }

    [TestMethod]
    public async Task AQuotedHoldingShowsItsValueAndGain()
    {
        var (dialog, service, quotes, _) = Create();
        service.Add("AAPL", 10, 100);
        Quote(quotes, "AAPL", 110);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 1);

        var row = dialog.RowsForTest.Single();

        Assert.AreEqual(1100m, row.MarketValue);
        Assert.AreEqual(100m, row.GainLossAbsolute);
        Assert.AreEqual(10m, row.GainLossPercent);
        AssertScreenContains(Render(dialog), "+100.00");
    }

    [TestMethod]
    public async Task TheSummaryComparesLikeWithLike()
    {
        // The cost of every holding was set against the value of only the quoted ones, which read
        // as a loss the size of every unquoted holding.
        var (dialog, service, quotes, _) = Create();
        service.Add("AAPL", 10, 100);
        service.Add("NEW", 5, 50);
        Quote(quotes, "AAPL", 100);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 2);

        var screen = Render(dialog);

        AssertScreenContains(screen, $"G/L: {0m:+0.00;-0.00}");
        AssertScreenContains(screen, "1 not quoted yet");
    }

    [TestMethod]
    public async Task AddingWalksThroughFiveNumberedSteps()
    {
        var (dialog, service, _, _) = Create();
        await ShowAsync(dialog);
        await SettleAsync(dialog, 0);

        await dialog.HandleKeyEventAsync(Char('a'));
        StringAssert.Contains(dialog.InputLabelForTest, "(1/5)");
        await EnterAsync(dialog, "msft");
        StringAssert.Contains(dialog.InputLabelForTest, "(2/5)");
        await EnterAsync(dialog, "us");
        await EnterAsync(dialog, "3");
        await EnterAsync(dialog, "250.5");
        StringAssert.Contains(dialog.InputLabelForTest, "(5/5)");
        await EnterAsync(dialog, "usd");

        await SettleAsync(dialog, 1);
        var holding = service.Holdings.Single();
        Assert.AreEqual("MSFT", holding.Symbol);
        Assert.AreEqual("US", holding.Exchange);
        Assert.AreEqual(3m, holding.Quantity);
        Assert.AreEqual(250.5m, holding.CostBasisPerUnit);
    }

    [TestMethod]
    public void ADecimalCommaIsADecimalNotAThousandsSeparator()
    {
        // "1,5" was read invariantly with thousands allowed — and stored as 15.
        Assert.IsTrue(PortfolioManagerDialog.TryParseAmount("1,5", out var comma));
        Assert.AreEqual(1.5m, comma);
        Assert.IsTrue(PortfolioManagerDialog.TryParseAmount(" 2.25 ", out var point));
        Assert.AreEqual(2.25m, point);
        Assert.IsFalse(PortfolioManagerDialog.TryParseAmount("1,500.00", out _), "an ambiguous grouped number is refused, not guessed");
        Assert.IsFalse(PortfolioManagerDialog.TryParseAmount("abc", out _));
    }

    [TestMethod]
    public async Task AnInvalidQuantityStopsTheAddAndSaysWhy()
    {
        var (dialog, service, _, _) = Create();
        await ShowAsync(dialog);
        await SettleAsync(dialog, 0);

        await dialog.HandleKeyEventAsync(Char('a'));
        await EnterAsync(dialog, "msft");
        await EnterAsync(dialog, string.Empty);
        await EnterAsync(dialog, "lots");

        Assert.AreEqual("Error: invalid quantity.", dialog.MessageForTest);
        Assert.AreEqual(0, service.Holdings.Count);
        Assert.IsFalse(dialog.IsInputActiveForTest);
    }

    [TestMethod]
    public async Task AnEmptySymbolIsRefused()
    {
        var (dialog, _, _, _) = Create();
        await ShowAsync(dialog);
        await SettleAsync(dialog, 0);

        await dialog.HandleKeyEventAsync(Char('a'));
        await EnterAsync(dialog, "  ");

        Assert.AreEqual("Symbol is required.", dialog.MessageForTest);
    }

    [TestMethod]
    public async Task EscapeCancelsTheInputNotTheDialog()
    {
        var (dialog, _, _, _) = Create();
        var root = await ShowAsync(dialog);
        await SettleAsync(dialog, 0);
        await dialog.HandleKeyEventAsync(Char('a'));

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(dialog.IsInputActiveForTest);
        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog));
    }

    [TestMethod]
    public async Task EditingIsPrefilledAndSaved()
    {
        var (dialog, service, _, _) = Create();
        service.Add("AAPL", 10, 100);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 1);

        await dialog.HandleKeyEventAsync(Char('e'));
        Assert.IsTrue(PortfolioManagerDialog.TryParseAmount(dialog.InputBoxForTest.Text, out var prefilled));
        Assert.AreEqual(10m, prefilled, "the current quantity is offered for editing");
        await EnterAsync(dialog, "12");
        await EnterAsync(dialog, "90");

        await WaitUntilAsync(() => service.Holdings.Single().Quantity == 12m, "the edit to be saved");
        Assert.AreEqual(90m, service.Holdings.Single().CostBasisPerUnit);
    }

    [TestMethod]
    public async Task AFailedEditIsReportedAndTheListShowsWhatIsStored()
    {
        var (dialog, service, _, _) = Create();
        service.Add("AAPL", 10, 100);
        service.UpdateFailure = new InvalidOperationException("database locked");
        await ShowAsync(dialog);
        await SettleAsync(dialog, 1);

        await dialog.HandleKeyEventAsync(Char('e'));
        await EnterAsync(dialog, "12");
        await EnterAsync(dialog, "90");

        await WaitUntilAsync(() => dialog.MessageForTest.Contains("database locked"), "the failure to be reported");
        await WaitUntilAsync(() => dialog.RowsForTest.Single().Holding.Quantity == 10m, "the stored quantity to be shown again");
    }

    [TestMethod]
    public async Task DeletingAsksFirst()
    {
        var (dialog, service, _, _) = Create();
        service.Add("AAPL", 10, 100);
        await ShowAsync(dialog);
        await SettleAsync(dialog, 1);

        await dialog.HandleKeyEventAsync(Char('d'));
        Assert.IsTrue(dialog.IsConfirmingForTest);
        AssertScreenContains(Render(dialog), "Delete AAPL?  y:yes  any other key:no");

        await dialog.HandleKeyEventAsync(Char('n'));
        Assert.AreEqual(1, service.Holdings.Count);

        await dialog.HandleKeyEventAsync(Char('d'));
        await dialog.HandleKeyEventAsync(Char('y'));
        await SettleAsync(dialog, 0);
        Assert.AreEqual(0, service.Holdings.Count);
    }

    [TestMethod]
    public async Task RefreshingQuotesFetchesThemForEveryHolding()
    {
        var (dialog, service, quotes, provider) = Create();
        service.Add("AAPL", 10, 100);
        service.Add("SAP", 1, 100, exchange: "DE");
        provider.Setup(p => p.GetQuotesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, StooqQuote> { ["SAP.DE"] = new("SAP.DE", 120m, DateTime.Now) });
        await ShowAsync(dialog);
        await SettleAsync(dialog, 2);

        await dialog.HandleKeyEventAsync(Char('r'));

        await WaitUntilAsync(() => dialog.RowsForTest.Any(r => r.CurrentPrice == 120m), "the fetched quote to be shown");
        provider.Verify(p => p.GetQuotesAsync(It.Is<IReadOnlyList<string>>(s => s.Contains("AAPL") && s.Contains("SAP.DE")), It.IsAny<CancellationToken>()));
        Assert.IsTrue(quotes.Instruments.ContainsKey("SAP.DE"));
    }

    [TestMethod]
    public async Task AFailedQuoteRefreshKeepsTheKeyHintsVisible()
    {
        var (dialog, service, _, provider) = Create();
        service.Add("AAPL", 10, 100);
        provider.Setup(p => p.GetQuotesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        await ShowAsync(dialog);
        await SettleAsync(dialog, 1);

        await dialog.HandleKeyEventAsync(Char('r'));
        await WaitUntilAsync(() => dialog.MessageForTest.Contains("offline"), "the failure to be reported");

        var screen = Render(dialog);
        AssertScreenContains(screen, "Error: offline");
        AssertScreenContains(screen, "a:add", "the key hints used to be replaced by any message");
    }
}
