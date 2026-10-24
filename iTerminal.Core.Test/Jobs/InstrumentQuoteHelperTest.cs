using iTerminal.Core.Data;
using iTerminal.Core.Jobs;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Jobs.Test;

/// <summary>Recording a quote keeps the newest first and a bounded history; priming restores the last value per instrument.</summary>
[TestClass]
public class InstrumentQuoteHelperTest
{
    private static async Task RecordAsync(OnlineQuoteService service, decimal value, string name = "BTC")
    {
        var cache = new Mock<ICacheService>();
        await InstrumentQuoteHelper.RecordQuoteAsync(service, cache.Object, "test", name, "USD", InstrumentType.CryptoCurrency, value, DateTime.UtcNow, DateTime.UtcNow);
    }

    [TestMethod]
    public async Task ANewQuote_IsTheFirstValueOfItsInstrument()
    {
        var service = new OnlineQuoteService();

        await RecordAsync(service, 100m);
        await RecordAsync(service, 101m);

        Assert.AreEqual(101m, service.Instruments["BTC"].InstrumentValues.First().Value);
    }

    [TestMethod]
    public async Task TheHistory_IsBounded_AndKeepsTheNewest()
    {
        var service = new OnlineQuoteService();

        for (var i = 1; i <= InstrumentQuoteHelper.MaxValuesKept + 50; i++)
        {
            await RecordAsync(service, i);
        }

        var values = service.Instruments["BTC"].InstrumentValues;
        Assert.AreEqual(InstrumentQuoteHelper.MaxValuesKept, values.Count);
        Assert.AreEqual(InstrumentQuoteHelper.MaxValuesKept + 50m, values.First().Value);
    }

    [TestMethod]
    public void Priming_RestoresTheCachedValueAsTheLatest()
    {
        var service = new OnlineQuoteService();
        var cached = new[]
        {
            new CachedInstrumentValue
            {
                InstrumentName = "ETH", Currency = "USD", Value = 3000m, InstrumentType = (int)InstrumentType.CryptoCurrency,
                InstrumentValueType = (int)InstrumentValueType.Intraday, SourceDateTime = DateTime.UtcNow, CachedAtUtc = DateTime.UtcNow,
            },
        };

        InstrumentQuoteHelper.PrimeFromCache(service, cached, "cache");

        Assert.AreEqual(3000m, service.Instruments["ETH"].InstrumentValues.First().Value);
    }
}
