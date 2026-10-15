using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class RectangleTest
{
    [TestMethod]
    public void RectangleConstructorPositionSize()
    {
        var position = new Position(10, 20);
        var size = new Size(30, 40);
        var rect = new Rectangle(position, size);
        Assert.AreEqual(10, rect.Left);
        Assert.AreEqual(20, rect.Top);
        Assert.AreEqual(40, rect.Right);
        Assert.AreEqual(60, rect.Bottom);
    }

    [TestMethod]
    public void RectangleConstructorInt()
    {
        var rect = new Rectangle(10, 20, 30, 40);
        Assert.AreEqual(10, rect.Left);
        Assert.AreEqual(20, rect.Top);
        Assert.AreEqual(40, rect.Right);
        Assert.AreEqual(60, rect.Bottom);
    }

    [TestMethod]
    public void RectangleConstructorUInt()
    {
        var rect = new Rectangle(10u, 20u, 30u, 40u);
        Assert.AreEqual(10, rect.Left);
        Assert.AreEqual(20, rect.Top);
        Assert.AreEqual(40, rect.Right);
        Assert.AreEqual(60, rect.Bottom);
    }

    [TestMethod]
    public void RectangleProperties()
    {
        var rect = new Rectangle(5, 10, 20, 15);
        Assert.AreEqual(5, rect.Left);
        Assert.AreEqual(10, rect.Top);
        Assert.AreEqual(25, rect.Right);
        Assert.AreEqual(25, rect.Bottom);
    }

    [TestMethod]
    public void RectangleContains()
    {
        var rect = new Rectangle(10, 10, 20, 20);
        Assert.IsTrue(rect.Contains(new Position(10, 10)));
        Assert.IsTrue(rect.Contains(new Position(15, 15)));
        Assert.IsTrue(rect.Contains(new Position(29, 29)));
    }

    [TestMethod]
    public void RectangleNotContains()
    {
        var rect = new Rectangle(10, 10, 20, 20);
        Assert.IsFalse(rect.Contains(new Position(9, 10)));
        Assert.IsFalse(rect.Contains(new Position(10, 9)));
        Assert.IsFalse(rect.Contains(new Position(31, 15)));
    }

    [TestMethod]
    public void RectangleCropWithPositionAndSize()
    {
        var r1 = new Rectangle(new Position(11, 22), new Size(33, 44));
        r1.Crop(new Position(23, 34), new Size(7, 8));
        Assert.AreEqual(23, r1.Left);
        Assert.AreEqual(34, r1.Top);
        Assert.AreEqual(30, r1.Right);
        Assert.AreEqual(42, r1.Bottom);
    }

    [TestMethod]
    public void RectangleCropWithRectangle()
    {
        var r1 = new Rectangle(new Position(11, 22), new Size(33, 44));
        var r2 = new Rectangle(new Position(23, 34), new Size(7, 8));
        r1.Crop(r2);
        Assert.AreEqual(23, r1.Left);
        Assert.AreEqual(34, r1.Top);
        Assert.AreEqual(30, r1.Right);
        Assert.AreEqual(42, r1.Bottom);
    }

    [TestMethod]
    public void RectangleCropMethodReturnsNewRectangle()
    {
        var r1 = new Rectangle(10, 10, 20, 20);
        var r2 = r1.Crop(15, 15, 10, 10);
        Assert.AreEqual(15, r2.Left);
        Assert.AreEqual(15, r2.Top);
        Assert.AreEqual(25, r2.Right);
        Assert.AreEqual(25, r2.Bottom);
    }

    [TestMethod]
    public void RectangleIncludeTopLeft()
    {
        var r1 = new Rectangle(new Position(10, 20), new Size(30, 40));
        var r2 = new Rectangle(new Position(0, 1), new Size(1, 1));
        r1.Include(r2);
        Assert.AreEqual(0, r1.Left);
        Assert.AreEqual(1, r1.Top);
        Assert.AreEqual(40, r1.Right);
        Assert.AreEqual(60, r1.Bottom);
    }

    [TestMethod]
    public void RectangleIncludeBottomRight()
    {
        var r1 = new Rectangle(new Position(10, 20), new Size(30, 40));
        var r2 = new Rectangle(new Position(70, 80), new Size(1, 1));
        r1.Include(r2);
        Assert.AreEqual(10, r1.Left);
        Assert.AreEqual(20, r1.Top);
        Assert.AreEqual(71, r1.Right);
        Assert.AreEqual(81, r1.Bottom);
    }

    [TestMethod]
    public void RectangleToString()
    {
        var rect = new Rectangle(5, 10, 20, 15);
        var str = rect.ToString();
        Assert.IsTrue(str.Contains("5") && str.Contains("10"));
    }
}
