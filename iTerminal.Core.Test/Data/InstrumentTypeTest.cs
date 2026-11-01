using iTerminal.Core.Data;

namespace iTerminal.Core.Test.Data;

[TestClass]
public class InstrumentTypeTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void InstrumentType_Cash_HasValue()
    {
        var type = InstrumentType.Cash;
        Assert.AreEqual(0, (int)type);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InstrumentType_CryptoCurrency_HasValue()
    {
        var type = InstrumentType.CryptoCurrency;
        Assert.AreEqual(1, (int)type);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InstrumentType_Fund_HasValue()
    {
        var type = InstrumentType.Fund;
        Assert.AreEqual(2, (int)type);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InstrumentType_Stock_HasValue()
    {
        var type = InstrumentType.Stock;
        Assert.AreEqual(3, (int)type);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InstrumentType_AllValuesAreUnique()
    {
        var types = new[]
        {
            InstrumentType.Cash,
            InstrumentType.CryptoCurrency,
            InstrumentType.Fund,
            InstrumentType.Stock
        };

        var distinctTypes = types.Distinct().ToArray();
        Assert.AreEqual(types.Length, distinctTypes.Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InstrumentType_CanConvertToString()
    {
        var type = InstrumentType.Stock;
        var str = type.ToString();
        Assert.AreEqual("Stock", str);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InstrumentType_CanCompare()
    {
        var type1 = InstrumentType.Cash;
        var type2 = InstrumentType.Cash;
        var type3 = InstrumentType.Stock;

        Assert.AreEqual(type1, type2);
        Assert.AreNotEqual(type1, type3);
    }
}
