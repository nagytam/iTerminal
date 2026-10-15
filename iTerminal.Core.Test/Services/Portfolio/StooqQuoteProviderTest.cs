using iTerminal.Core.Services.Portfolio;

namespace iTerminal.Core.Test.Services.Portfolio;

[TestClass]
public class StooqQuoteProviderTest
{
    [TestMethod]
    public void ParseCsv_NormalRow_ReturnsQuote()
    {
        const string csv = "Symbol,Date,Time,Open,High,Low,Close,Volume\r\n" +
                            "AAPL.US,2024-01-05,16:00:02,180.0,182.5,179.0,181.25,1000000\r\n";

        var quotes = StooqQuoteProvider.ParseCsv(csv);

        Assert.AreEqual(1, quotes.Count);
        Assert.IsTrue(quotes.TryGetValue("AAPL.US", out var quote));
        Assert.AreEqual(181.25m, quote!.Close);
        Assert.AreEqual(new DateTime(2024, 1, 5, 16, 0, 2), quote.Timestamp);
    }

    [TestMethod]
    public void ParseCsv_NoDataRow_IsExcluded()
    {
        const string csv = "Symbol,Date,Time,Open,High,Low,Close,Volume\r\n" +
                            "BOGUS.US,2024-01-05,16:00:02,N/D,N/D,N/D,N/D,N/D\r\n";

        var quotes = StooqQuoteProvider.ParseCsv(csv);

        Assert.AreEqual(0, quotes.Count);
    }

    [TestMethod]
    public void ParseCsv_MultipleSymbols_ReturnsAll()
    {
        const string csv = "Symbol,Date,Time,Open,High,Low,Close,Volume\r\n" +
                            "AAPL.US,2024-01-05,16:00:02,180.0,182.5,179.0,181.25,1000000\r\n" +
                            "MSFT.US,2024-01-05,16:00:02,370.0,375.0,369.0,372.50,900000\r\n";

        var quotes = StooqQuoteProvider.ParseCsv(csv);

        Assert.AreEqual(2, quotes.Count);
        Assert.IsTrue(quotes.ContainsKey("AAPL.US"));
        Assert.IsTrue(quotes.ContainsKey("MSFT.US"));
    }

    [TestMethod]
    public void ParseCsv_EmptyOrWhitespace_ReturnsEmptyDictionary()
    {
        Assert.AreEqual(0, StooqQuoteProvider.ParseCsv(string.Empty).Count);
        Assert.AreEqual(0, StooqQuoteProvider.ParseCsv("   ").Count);
    }
}
