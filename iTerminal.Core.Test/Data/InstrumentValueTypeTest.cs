using iTerminal.Core.Data;

namespace iTerminal.Core.Test.Data;

[TestClass]
public class InstrumentValueTypeTest
{
    [TestMethod]
    public void InstrumentValueType_Open_HasValue()
    {
        var type = InstrumentValueType.Open;
        Assert.AreEqual(0, (int)type);
    }

    [TestMethod]
    public void InstrumentValueType_Intraday_HasValue()
    {
        var type = InstrumentValueType.Intraday;
        Assert.AreEqual(1, (int)type);
    }

    [TestMethod]
    public void InstrumentValueType_Close_HasValue()
    {
        var type = InstrumentValueType.Close;
        Assert.AreEqual(2, (int)type);
    }

    [TestMethod]
    public void InstrumentValueType_Low_HasValue()
    {
        var type = InstrumentValueType.Low;
        Assert.AreEqual(3, (int)type);
    }

    [TestMethod]
    public void InstrumentValueType_High_HasValue()
    {
        var type = InstrumentValueType.High;
        Assert.AreEqual(4, (int)type);
    }

    [TestMethod]
    public void InstrumentValueType_Bid_HasValue()
    {
        var type = InstrumentValueType.Bid;
        Assert.AreEqual(5, (int)type);
    }

    [TestMethod]
    public void InstrumentValueType_Ask_HasValue()
    {
        var type = InstrumentValueType.Ask;
        Assert.AreEqual(6, (int)type);
    }

    [TestMethod]
    public void InstrumentValueType_AllValuesAreUnique()
    {
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

        var distinctTypes = types.Distinct().ToArray();
        Assert.AreEqual(types.Length, distinctTypes.Length);
    }

    [TestMethod]
    public void InstrumentValueType_CanConvertToString()
    {
        var type = InstrumentValueType.Close;
        var str = type.ToString();
        Assert.AreEqual("Close", str);
    }

    [TestMethod]
    public void InstrumentValueType_CanCompare()
    {
        var type1 = InstrumentValueType.Open;
        var type2 = InstrumentValueType.Open;
        var type3 = InstrumentValueType.Close;

        Assert.AreEqual(type1, type2);
        Assert.AreNotEqual(type1, type3);
    }
}
