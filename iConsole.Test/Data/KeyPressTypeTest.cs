using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class KeyPressTypeTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void KeyPressType_Down_HasValue()
    {
        var type = KeyPressType.Down;
        Assert.AreEqual(0, (int)type);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyPressType_Up_HasValue()
    {
        var type = KeyPressType.Up;
        Assert.AreEqual(1, (int)type);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyPressType_AllValuesAreUnique()
    {
        var types = new[]
        {
            KeyPressType.Down,
            KeyPressType.Up
        };

        var distinctTypes = types.Distinct().ToArray();
        Assert.AreEqual(types.Length, distinctTypes.Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyPressType_CanCompare()
    {
        var type1 = KeyPressType.Down;
        var type2 = KeyPressType.Down;
        var type3 = KeyPressType.Up;

        Assert.AreEqual(type1, type2);
        Assert.AreNotEqual(type1, type3);
    }
}
