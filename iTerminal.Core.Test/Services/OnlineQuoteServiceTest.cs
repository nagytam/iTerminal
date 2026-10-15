using iTerminal.Core.Data;
using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class OnlineQuoteServiceTest
{
    [TestMethod]
    public void Constructor_InitializesInstruments()
    {
        var svc = new OnlineQuoteService();
        Assert.IsNotNull(svc.Instruments);
    }

    [TestMethod]
    public void Instruments_IsEmpty_Initially()
    {
        var svc = new OnlineQuoteService();
        Assert.AreEqual(0, svc.Instruments.Count);
    }

    [TestMethod]
    public void Instruments_CanAddInstrument()
    {
        var svc = new OnlineQuoteService();
        var instrument = new Instrument { Name = "BTC", Currency = "USD", InstrumentType = InstrumentType.CryptoCurrency };
        svc.Instruments.TryAdd("BTC", instrument);
        Assert.AreEqual(1, svc.Instruments.Count);
        Assert.IsTrue(svc.Instruments.ContainsKey("BTC"));
    }

    [TestMethod]
    public void Instruments_CanRetrieveInstrument()
    {
        var svc = new OnlineQuoteService();
        var instrument = new Instrument { Name = "ETH", Currency = "USD", InstrumentType = InstrumentType.CryptoCurrency };
        svc.Instruments.TryAdd("ETH", instrument);
        Assert.IsTrue(svc.Instruments.TryGetValue("ETH", out var retrieved));
        Assert.AreEqual("ETH", retrieved!.Name);
    }

    [TestMethod]
    public void Instruments_CanUpdateInstrument()
    {
        var svc = new OnlineQuoteService();
        var instrument = new Instrument { Name = "BTC", Currency = "USD" };
        svc.Instruments.TryAdd("BTC", instrument);

        var updated = new Instrument { Name = "BTC", Currency = "USD" };
        svc.Instruments["BTC"] = updated;

        Assert.AreEqual("USD", svc.Instruments["BTC"].Currency);
    }

    [TestMethod]
    public void Instruments_MultipleEntries_AllStored()
    {
        var svc = new OnlineQuoteService();
        svc.Instruments.TryAdd("A", new Instrument { Name = "A", Currency = "USD" });
        svc.Instruments.TryAdd("B", new Instrument { Name = "B", Currency = "USD" });
        svc.Instruments.TryAdd("C", new Instrument { Name = "C", Currency = "USD" });
        Assert.AreEqual(3, svc.Instruments.Count);
    }
}
