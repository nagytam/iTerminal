using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class MouseEventTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void MouseEventConstructor()
    {
        var mouseEvent = new MouseEvent();
        Assert.IsNotNull(mouseEvent.Position);
        Assert.AreEqual(0, mouseEvent.Position.X);
        Assert.AreEqual(0, mouseEvent.Position.Y);
        Assert.IsFalse(mouseEvent.LeftClicked);
        Assert.IsFalse(mouseEvent.RightClicked);
        Assert.IsFalse(mouseEvent.IsRelease);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MouseEventSetPosition()
    {
        var mouseEvent = new MouseEvent();
        var position = new Position(10, 20);
        mouseEvent.Position = position;
        Assert.AreEqual(10, mouseEvent.Position.X);
        Assert.AreEqual(20, mouseEvent.Position.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MouseEventLeftClicked()
    {
        var mouseEvent = new MouseEvent { LeftClicked = true };
        Assert.IsTrue(mouseEvent.LeftClicked);
        Assert.IsFalse(mouseEvent.RightClicked);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MouseEventRightClicked()
    {
        var mouseEvent = new MouseEvent { RightClicked = true };
        Assert.IsFalse(mouseEvent.LeftClicked);
        Assert.IsTrue(mouseEvent.RightClicked);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MouseEventBothClicked()
    {
        var mouseEvent = new MouseEvent { LeftClicked = true, RightClicked = true };
        Assert.IsTrue(mouseEvent.LeftClicked);
        Assert.IsTrue(mouseEvent.RightClicked);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MouseEventInitialize()
    {
        var position = new Position(15, 25);
        var mouseEvent = new MouseEvent
        {
            Position = position,
            LeftClicked = true,
            RightClicked = false
        };
        Assert.AreEqual(15, mouseEvent.Position.X);
        Assert.AreEqual(25, mouseEvent.Position.Y);
        Assert.IsTrue(mouseEvent.LeftClicked);
        Assert.IsFalse(mouseEvent.RightClicked);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MouseEventEquality()
    {
        var mouseEvent1 = new MouseEvent
        {
            Position = new Position(10, 20),
            LeftClicked = true,
            RightClicked = false,
            IsRelease = false,
        };
        var mouseEvent2 = new MouseEvent
        {
            Position = new Position(10, 20),
            LeftClicked = true,
            RightClicked = false,
            IsRelease = false,
        };
        Assert.AreEqual(mouseEvent1, mouseEvent2);
    }
}
