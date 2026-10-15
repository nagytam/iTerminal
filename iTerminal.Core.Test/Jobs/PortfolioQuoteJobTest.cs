using iTerminal.Core.Data;
using iTerminal.Core.Jobs;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class PortfolioQuoteJobTest
{
    [TestMethod]
    public void BuildStooqSymbol_WithExchange_JoinsSymbolAndExchange()
    {
        var holding = new PortfolioHolding { Symbol = "AAPL", Exchange = "US", Currency = "USD" };

        Assert.AreEqual("AAPL.US", PortfolioQuoteJob.BuildStooqSymbol(holding));
    }

    [TestMethod]
    public void BuildStooqSymbol_WithoutExchange_ReturnsSymbolOnly()
    {
        var holding = new PortfolioHolding { Symbol = "BTC", Exchange = null, Currency = "USD" };

        Assert.AreEqual("BTC", PortfolioQuoteJob.BuildStooqSymbol(holding));
    }

    [TestMethod]
    public void BuildStooqSymbol_WithWhitespaceExchange_ReturnsSymbolOnly()
    {
        var holding = new PortfolioHolding { Symbol = "BTC", Exchange = "   ", Currency = "USD" };

        Assert.AreEqual("BTC", PortfolioQuoteJob.BuildStooqSymbol(holding));
    }
}
