using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class PositionTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void PositionConstructor()
    {
        var position = new Position(5, 10);
        Assert.AreEqual(5, position.X);
        Assert.AreEqual(10, position.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PositionZero()
    {
        var position = new Position(0, 0);
        Assert.AreEqual(0, position.X);
        Assert.AreEqual(0, position.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PositionLargeValues()
    {
        var position = new Position(1000, 2000);
        Assert.AreEqual(1000, position.X);
        Assert.AreEqual(2000, position.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PositionSetX()
    {
        var position = new Position(0, 0);
        position.X = 15;
        Assert.AreEqual(15, position.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PositionSetY()
    {
        var position = new Position(0, 0);
        position.Y = 25;
        Assert.AreEqual(25, position.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PositionReset()
    {
        var position = new Position(5, 10);
        position.Reset();
        Assert.AreEqual(0, position.X);
        Assert.AreEqual(0, position.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PositionAddition()
    {
        var position1 = new Position(5, 10);
        var position2 = new Position(3, 7);
        var result = position1 + position2;
        Assert.AreEqual(8, result.X);
        Assert.AreEqual(17, result.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PositionAdditionWithNull()
    {
        Position? position1 = null;
        var position2 = new Position(3, 7);
        var result = position1 + position2;
        Assert.AreEqual(3, result.X);
        Assert.AreEqual(7, result.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PositionPropertyChanged()
    {
        var position = new Position(0, 0);
        bool xChanged = false;
        bool yChanged = false;

        position.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Position.X)) xChanged = true;
            if (e.PropertyName == nameof(Position.Y)) yChanged = true;
        };

        position.X = 5;
        Assert.IsTrue(xChanged);

        position.Y = 10;
        Assert.IsTrue(yChanged);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PositionEquality()
    {
        var position1 = new Position(5, 10);
        var position2 = new Position(5, 10);
        var position3 = new Position(5, 11);

        Assert.AreEqual(position1, position2);
        Assert.AreNotEqual(position1, position3);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PositionToString()
    {
        var position = new Position(5, 10);
        var str = position.ToString();
        Assert.IsTrue(str.Contains("5") && str.Contains("10"));
    }
}
