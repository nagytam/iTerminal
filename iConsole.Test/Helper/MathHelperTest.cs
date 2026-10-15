using iConsole.Helper;

namespace iConsole.Test.Helper;

[TestClass]
public class MathHelperTest
{
    [TestMethod]
    public void MathHelperMin()
    {
        var result = MathHelper.Min(5, 3, 8);
        Assert.AreEqual(3, result);
    }

    [TestMethod]
    public void MathHelperMinFirst()
    {
        var result = MathHelper.Min(1, 5, 8);
        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public void MathHelperMinLast()
    {
        var result = MathHelper.Min(5, 8, 2);
        Assert.AreEqual(2, result);
    }

    [TestMethod]
    public void MathHelperMinEqual()
    {
        var result = MathHelper.Min(5, 5, 5);
        Assert.AreEqual(5, result);
    }

    [TestMethod]
    public void MathHelperMinNegative()
    {
        var result = MathHelper.Min(-5, -3, -8);
        Assert.AreEqual(-8, result);
    }

    [TestMethod]
    public void IntRangeInBounds()
    {
        var result = 5.Range(0, 10);
        Assert.AreEqual(5, result);
    }

    [TestMethod]
    public void IntRangeBelowMin()
    {
        var result = (-5).Range(0, 10);
        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public void IntRangeAboveMax()
    {
        var result = 15.Range(0, 10);
        Assert.AreEqual(10, result);
    }

    [TestMethod]
    public void IntRangeAtMin()
    {
        var result = 0.Range(0, 10);
        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public void IntRangeAtMax()
    {
        var result = 10.Range(0, 10);
        Assert.AreEqual(10, result);
    }

    [TestMethod]
    public void LongRangeInBounds()
    {
        var result = 5L.Range(0, 10);
        Assert.AreEqual(5, result);
    }

    [TestMethod]
    public void LongRangeBelowMin()
    {
        var result = (-5L).Range(0, 10);
        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public void LongRangeAboveMax()
    {
        var result = 15L.Range(0, 10);
        Assert.AreEqual(10, result);
    }

    [TestMethod]
    public void LongRangeAtMin()
    {
        var result = 0L.Range(0, 10);
        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public void LongRangeAtMax()
    {
        var result = 10L.Range(0, 10);
        Assert.AreEqual(10, result);
    }
}
