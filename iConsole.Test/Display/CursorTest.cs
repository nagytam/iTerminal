using iConsole.Data;
using iConsole.Display;

namespace iConsole.Test.Display;

[TestClass]
public class CursorTest
{
    [TestMethod]
    public void CursorConstructor()
    {
        var size = new Size(80, 24);
        var cursor = new Cursor(size);
        Assert.AreEqual(size, cursor.Size);
        Assert.IsFalse(cursor.IsCursorVisible);
        Assert.IsNull(cursor.CursorPosition);
    }

    [TestMethod]
    public void CursorSetVisible()
    {
        var cursor = new Cursor(new Size(80, 24));
        cursor.IsCursorVisible = true;
        Assert.IsTrue(cursor.IsCursorVisible);
    }

    [TestMethod]
    public void CursorSetPosition()
    {
        var cursor = new Cursor(new Size(80, 24));
        cursor.IsCursorVisible = true;
        var position = new Position(10, 5);
        cursor.CursorPosition = position;
        Assert.IsNotNull(cursor.CursorPosition);
        Assert.AreEqual(10, cursor.CursorPosition.X);
        Assert.AreEqual(5, cursor.CursorPosition.Y);
    }

    [TestMethod]
    public void CursorPositionNotVisibleWhenHidden()
    {
        var cursor = new Cursor(new Size(80, 24));
        cursor.IsCursorVisible = true;
        cursor.CursorPosition = new Position(10, 5);
        Assert.IsNotNull(cursor.CursorPosition);

        cursor.IsCursorVisible = false;
        Assert.IsNull(cursor.CursorPosition);
    }

    [TestMethod]
    public void CursorPositionClampedToWidth()
    {
        var cursor = new Cursor(new Size(80, 24));
        cursor.IsCursorVisible = true;
        cursor.CursorPosition = new Position(100, 5);
        Assert.IsNotNull(cursor.CursorPosition);
        Assert.AreEqual(79, cursor.CursorPosition.X);
        Assert.AreEqual(5, cursor.CursorPosition.Y);
    }

    [TestMethod]
    public void CursorPositionClampedToHeight()
    {
        var cursor = new Cursor(new Size(80, 24));
        cursor.IsCursorVisible = true;
        cursor.CursorPosition = new Position(10, 50);
        Assert.IsNotNull(cursor.CursorPosition);
        Assert.AreEqual(10, cursor.CursorPosition.X);
        Assert.AreEqual(23, cursor.CursorPosition.Y);
    }

    [TestMethod]
    public void CursorSetPositionToNull()
    {
        var cursor = new Cursor(new Size(80, 24));
        cursor.IsCursorVisible = true;
        cursor.CursorPosition = new Position(10, 5);
        Assert.IsNotNull(cursor.CursorPosition);

        cursor.CursorPosition = null;
        Assert.IsNull(cursor.CursorPosition);
    }

    [TestMethod]
    public void CursorResizeSize()
    {
        var cursor = new Cursor(new Size(80, 24));
        cursor.Size = new Size(120, 40);
        Assert.AreEqual(120, cursor.Size.Width);
        Assert.AreEqual(40, cursor.Size.Height);
    }

    [TestMethod]
    public void CursorEquality()
    {
        var cursor1 = new Cursor(new Size(80, 24));
        var cursor2 = new Cursor(new Size(80, 24));
        Assert.AreEqual(cursor1, cursor2);
    }

    [TestMethod]
    public void CursorBoundsValidation()
    {
        var cursor = new Cursor(new Size(80, 24));
        cursor.IsCursorVisible = true;
        cursor.CursorPosition = new Position(0, 0);
        Assert.IsNotNull(cursor.CursorPosition);
        Assert.AreEqual(0, cursor.CursorPosition.X);
        Assert.AreEqual(0, cursor.CursorPosition.Y);
    }

    [TestMethod]
    public void CursorMaxBounds()
    {
        var cursor = new Cursor(new Size(80, 24));
        cursor.IsCursorVisible = true;
        cursor.CursorPosition = new Position(79, 23);
        Assert.IsNotNull(cursor.CursorPosition);
        Assert.AreEqual(79, cursor.CursorPosition.X);
        Assert.AreEqual(23, cursor.CursorPosition.Y);
    }
}
