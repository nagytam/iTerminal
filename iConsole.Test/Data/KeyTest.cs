using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class KeyTest
{
    [TestMethod]
    public void KeyConstructor()
    {
        var key = new Key(ConsoleKey.A, shift: false, alt: false, control: false);
        Assert.AreEqual(ConsoleKey.A, key.ConsoleKey);
        Assert.IsFalse(key.Shift);
        Assert.IsFalse(key.Alt);
        Assert.IsFalse(key.Control);
    }

    [TestMethod]
    public void KeyWithShift()
    {
        var key = new Key(ConsoleKey.A, shift: true, alt: false, control: false);
        Assert.IsTrue(key.Shift);
        Assert.IsFalse(key.Alt);
        Assert.IsFalse(key.Control);
    }

    [TestMethod]
    public void KeyWithAlt()
    {
        var key = new Key(ConsoleKey.A, shift: false, alt: true, control: false);
        Assert.IsFalse(key.Shift);
        Assert.IsTrue(key.Alt);
        Assert.IsFalse(key.Control);
    }

    [TestMethod]
    public void KeyWithControl()
    {
        var key = new Key(ConsoleKey.A, shift: false, alt: false, control: true);
        Assert.IsFalse(key.Shift);
        Assert.IsFalse(key.Alt);
        Assert.IsTrue(key.Control);
    }

    [TestMethod]
    public void KeyIsIdentical()
    {
        var key = new Key(ConsoleKey.A, shift: true, alt: false, control: false);
        var keyEvent = new KeyEvent('A', ConsoleKey.A, shift: true, alt: false, control: false);
        Assert.IsTrue(key.IsIdentical(keyEvent));
    }

    [TestMethod]
    public void KeyIsNotIdenticalDifferentKey()
    {
        var key = new Key(ConsoleKey.A, shift: false, alt: false, control: false);
        var keyEvent = new KeyEvent('B', ConsoleKey.B, shift: false, alt: false, control: false);
        Assert.IsFalse(key.IsIdentical(keyEvent));
    }

    [TestMethod]
    public void KeyIsNotIdenticalDifferentModifier()
    {
        var key = new Key(ConsoleKey.A, shift: false, alt: false, control: false);
        var keyEvent = new KeyEvent('A', ConsoleKey.A, shift: true, alt: false, control: false);
        Assert.IsFalse(key.IsIdentical(keyEvent));
    }

    [TestMethod]
    public void KeyEquality()
    {
        var key1 = new Key(ConsoleKey.A, shift: true, alt: false, control: false);
        var key2 = new Key(ConsoleKey.A, shift: true, alt: false, control: false);
        Assert.AreEqual(key1, key2);
    }
}
