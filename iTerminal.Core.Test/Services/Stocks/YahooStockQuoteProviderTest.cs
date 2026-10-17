using iTerminal.Core.Services.Stocks;

namespace iTerminal.Core.Test.Services.Stocks;

[TestClass]
public class YahooStockQuoteProviderTest
{
    private const string Chart = """
        {"chart":{"result":[{"meta":{"currency":"USD","symbol":"AAPL","exchangeName":"NMS","fullExchangeName":"NasdaqGS",
        "regularMarketTime":1792180801,"regularMarketPrice":343.46,"fiftyTwoWeekHigh":348.15,"fiftyTwoWeekLow":243.42,
        "regularMarketDayHigh":347.82,"regularMarketDayLow":340.46,"regularMarketVolume":59852690,
        "longName":"Apple Inc.","shortName":"Apple Inc.","chartPreviousClose":342.0}}],"error":null}}
        """;

    [TestMethod]
    public void AChartResponseBecomesAQuote()
    {
        var quote = YahooStockQuoteProvider.ParseChart("AAPL", Chart);

        Assert.IsNotNull(quote);
        Assert.AreEqual("Apple Inc.", quote.Name);
        Assert.AreEqual(343.46m, quote.Price);
        Assert.AreEqual(342.0m, quote.PreviousClose);
        Assert.AreEqual(1.46m, quote.Change);
        Assert.AreEqual("NasdaqGS", quote.Exchange);
        Assert.AreEqual("USD", quote.Currency);
        Assert.AreEqual(59852690L, quote.Volume);
        Assert.AreEqual(340.46m, quote.DayLow);
        Assert.AreEqual(348.15m, quote.YearHigh);
        Assert.IsNotNull(quote.MarketTime);
    }

    [TestMethod]
    public void ASymbolTheSourceDoesNotKnowHasNoQuote()
    {
        const string json = """{"chart":{"result":null,"error":{"code":"Not Found","description":"No data found"}}}""";

        Assert.IsNull(YahooStockQuoteProvider.ParseChart("NOPE", json));
    }

    [TestMethod]
    public void AMissingPreviousCloseMeansNoChange()
    {
        const string json = """{"chart":{"result":[{"meta":{"regularMarketPrice":10.5,"symbol":"X"}}]}}""";

        var quote = YahooStockQuoteProvider.ParseChart("X", json);

        Assert.IsNotNull(quote);
        Assert.AreEqual(0m, quote.Change);
        Assert.AreEqual(0m, quote.ChangePercent);
        Assert.AreEqual("X", quote.Name);
    }

    [TestMethod]
    public void ASearchKeepsOnlyStocksAndNamesTheirExchange()
    {
        const string json = """
            {"quotes":[
              {"symbol":"VOD","quoteType":"EQUITY","longname":"Vodafone Group Public Limited Company","exchDisp":"NASDAQ"},
              {"symbol":"VOD.L","quoteType":"EQUITY","shortname":"VODAFONE GROUP PLC ORD USD0.20 ","exchDisp":"London"},
              {"symbol":"VODFX","quoteType":"MUTUALFUND","shortname":"A fund","exchDisp":"Other"},
              {"symbol":"VWRL.L","quoteType":"ETF","shortname":"Vanguard FTSE All-World","exchDisp":"London"}
            ]}
            """;

        var hits = YahooStockQuoteProvider.ParseSearch(json);

        CollectionAssert.AreEqual(new[] { "VOD", "VOD.L", "VWRL.L" }, hits.Select(h => h.Symbol).ToArray());
        Assert.AreEqual("London", hits[1].Exchange);
        Assert.AreEqual("VODAFONE GROUP PLC ORD USD0.20", hits[1].Name);
    }

    [TestMethod]
    public void AnythingElseInASearchResponseIsNoHits()
    {
        Assert.AreEqual(0, YahooStockQuoteProvider.ParseSearch("{}").Count);
    }
}
