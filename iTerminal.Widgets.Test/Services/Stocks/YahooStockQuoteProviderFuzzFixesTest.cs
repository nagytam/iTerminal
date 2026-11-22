using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Widgets.Test.Services.Stocks;

/// <summary>Stock replies that threw for an empty or broken body, or a null text (found by fuzzing).</summary>
[TestClass]
public class YahooStockQuoteProviderFuzzFixesTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void YahooStockQuoteProvider_ParseSearch_OfAnEmptyOrBrokenReply_IsNoHits()
    {
        Assert.AreEqual(0, iTerminal.Widgets.Services.Stocks.YahooStockQuoteProvider.ParseSearch(string.Empty).Count);
        Assert.AreEqual(0, iTerminal.Widgets.Services.Stocks.YahooStockQuoteProvider.ParseSearch("<html>502 Bad Gateway</html>").Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void YahooStockQuoteProvider_ParseChart_OfAnEmptyReply_IsNoQuote()
    {
        Assert.IsNull(iTerminal.Widgets.Services.Stocks.YahooStockQuoteProvider.ParseChart("MSFT", string.Empty));
    }
}
