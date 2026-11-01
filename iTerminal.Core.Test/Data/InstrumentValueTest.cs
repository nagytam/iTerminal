using iTerminal.Core.Data;

namespace iTerminal.Core.Test.Data;

[TestClass]
public class InstrumentValueTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_InitializesRequiredProperties()
    {
        var instrumentId = Guid.NewGuid();
        var valueId = Guid.NewGuid();
        var valueDateTime = DateTime.Now;
        var sourceDateTime = DateTime.Now;

        var value = new InstrumentValue
        {
            Id = valueId,
            InstrumentId = instrumentId,
            ValueDateTime = valueDateTime,
            Value = 150.50m,
            ValueType = InstrumentValueType.Close,
            Source = "Yahoo Finance",
            SourceDateTime = sourceDateTime
        };

        Assert.AreEqual(valueId, value.Id);
        Assert.AreEqual(instrumentId, value.InstrumentId);
        Assert.AreEqual(valueDateTime, value.ValueDateTime);
        Assert.AreEqual(150.50m, value.Value);
        Assert.AreEqual(InstrumentValueType.Close, value.ValueType);
        Assert.AreEqual("Yahoo Finance", value.Source);
        Assert.AreEqual(sourceDateTime, value.SourceDateTime);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Key_IsGeneratedCorrectly()
    {
        var instrumentId = Guid.NewGuid();
        var valueDateTime = DateTime.Parse("2024-01-15 10:30:00");

        var value = new InstrumentValue
        {
            Id = Guid.NewGuid(),
            InstrumentId = instrumentId,
            ValueDateTime = valueDateTime,
            Value = 100.00m,
            ValueType = InstrumentValueType.Open,
            Source = "Market Data",
            SourceDateTime = DateTime.Now
        };

        var expectedKey = string.Join("-", instrumentId, valueDateTime, InstrumentValueType.Open);
        Assert.AreEqual(expectedKey, value.Key);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Key_WithDifferentValueTypes_GeneratesDifferentKeys()
    {
        var instrumentId = Guid.NewGuid();
        var valueDateTime = DateTime.Parse("2024-01-15 10:30:00");

        var openValue = new InstrumentValue
        {
            Id = Guid.NewGuid(),
            InstrumentId = instrumentId,
            ValueDateTime = valueDateTime,
            Value = 100.00m,
            ValueType = InstrumentValueType.Open,
            Source = "Market Data",
            SourceDateTime = DateTime.Now
        };

        var closeValue = new InstrumentValue
        {
            Id = Guid.NewGuid(),
            InstrumentId = instrumentId,
            ValueDateTime = valueDateTime,
            Value = 105.00m,
            ValueType = InstrumentValueType.Close,
            Source = "Market Data",
            SourceDateTime = DateTime.Now
        };

        Assert.AreNotEqual(openValue.Key, closeValue.Key);
        Assert.IsTrue(openValue.Key.Contains("Open"));
        Assert.IsTrue(closeValue.Key.Contains("Close"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InstrumentValueType_AllEnumValuesCanBeUsed()
    {
        var instrumentId = Guid.NewGuid();
        var types = new[] 
        { 
            InstrumentValueType.Open,
            InstrumentValueType.Intraday,
            InstrumentValueType.Close,
            InstrumentValueType.Low,
            InstrumentValueType.High,
            InstrumentValueType.Bid,
            InstrumentValueType.Ask
        };

        foreach (var type in types)
        {
            var value = new InstrumentValue
            {
                Id = Guid.NewGuid(),
                InstrumentId = instrumentId,
                ValueDateTime = DateTime.Now,
                Value = 100.00m,
                ValueType = type,
                Source = "Test",
                SourceDateTime = DateTime.Now
            };

            Assert.AreEqual(type, value.ValueType);
            Assert.IsTrue(value.Key.Contains(type.ToString()));
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Value_SupportsDecimalValues()
    {
        var values = new decimal[] { 0.01m, 100.50m, 1000000.99m, -50.25m };

        foreach (var decimalValue in values)
        {
            var instrumentValue = new InstrumentValue
            {
                Id = Guid.NewGuid(),
                InstrumentId = Guid.NewGuid(),
                ValueDateTime = DateTime.Now,
                Value = decimalValue,
                ValueType = InstrumentValueType.Close,
                Source = "Test",
                SourceDateTime = DateTime.Now
            };

            Assert.AreEqual(decimalValue, instrumentValue.Value);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsRecord_CanCreateNewInstance()
    {
        var value1 = new InstrumentValue
        {
            Id = Guid.NewGuid(),
            InstrumentId = Guid.NewGuid(),
            ValueDateTime = DateTime.Now,
            Value = 100.00m,
            ValueType = InstrumentValueType.Close,
            Source = "Test",
            SourceDateTime = DateTime.Now
        };

        // Records should support with expressions
        var value2 = value1 with { Value = 200.00m };

        Assert.AreEqual(value1.Id, value2.Id);
        Assert.AreEqual(value1.InstrumentId, value2.InstrumentId);
        Assert.AreEqual(100.00m, value1.Value);
        Assert.AreEqual(200.00m, value2.Value);
    }
}
