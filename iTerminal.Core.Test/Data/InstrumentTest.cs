using iConsole.Data;
using iTerminal.Core.Data;

namespace iTerminal.Core.Test.Data;

[TestClass]
public class InstrumentTest
{
    [TestMethod]
    public void Constructor_InitializesProperties()
    {
        var instrument = new Instrument { Name = "Test", Currency = "USD" };
        Assert.IsNotNull(instrument);
        Assert.IsNotNull(instrument.InstrumentValues);
        Assert.AreEqual(0, instrument.InstrumentValues.Count);
    }

    [TestMethod]
    public void Name_SetAndGet()
    {
        var instrument = new Instrument { Name = "AAPL", Currency = "USD" };
        Assert.AreEqual("AAPL", instrument.Name);
    }

    [TestMethod]
    public void Currency_SetAndGet()
    {
        var instrument = new Instrument { Name = "AAPL", Currency = "USD" };
        Assert.AreEqual("USD", instrument.Currency);
    }

    [TestMethod]
    public void InstrumentType_SetAndGet()
    {
        var instrument = new Instrument { Name = "AAPL", Currency = "USD", InstrumentType = InstrumentType.Stock };
        Assert.AreEqual(InstrumentType.Stock, instrument.InstrumentType);
    }

    [TestMethod]
    public void InstrumentValues_IsInitializedAsEmptyList()
    {
        var instrument = new Instrument { Name = "AAPL", Currency = "USD" };
        Assert.IsNotNull(instrument.InstrumentValues);
        Assert.AreEqual(0, instrument.InstrumentValues.Count);
    }

    [TestMethod]
    public void InstrumentValues_CanAddValues()
    {
        var instrument = new Instrument { Name = "AAPL", Currency = "USD" };
        var value = new InstrumentValue
        {
            Id = Guid.NewGuid(),
            InstrumentId = instrument.Id,
            ValueDateTime = DateTime.Now,
            Value = 150.50m,
            ValueType = InstrumentValueType.Close,
            Source = "Yahoo Finance",
            SourceDateTime = DateTime.Now
        };

        instrument.InstrumentValues.Add(value);
        Assert.AreEqual(1, instrument.InstrumentValues.Count);
        Assert.AreEqual(value.Id, instrument.InstrumentValues[0].Id);
    }

    [TestMethod]
    public void Id_CanBeSet()
    {
        var id = Guid.NewGuid();
        var instrument1 = new Instrument { Name = "AAPL", Currency = "USD", Id = id };
        var instrument2 = new Instrument { Name = "MSFT", Currency = "USD", Id = id };
        Assert.AreEqual(instrument1.Id, instrument2.Id);
    }

    [TestMethod]
    public void MultipleInstrumentTypes_CanBeSet()
    {
        var types = new[] 
        { 
            InstrumentType.Cash, 
            InstrumentType.CryptoCurrency, 
            InstrumentType.Fund, 
            InstrumentType.Stock 
        };

        foreach (var type in types)
        {
            var instrument = new Instrument { Name = "Test", Currency = "USD", InstrumentType = type };
            Assert.AreEqual(type, instrument.InstrumentType);
        }
    }
}
