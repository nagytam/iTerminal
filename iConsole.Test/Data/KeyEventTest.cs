using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class KeyEventTest
{
    [TestMethod]
    public void KeyEventConstructor()
    {
        var keyEvent = new KeyEvent('A', ConsoleKey.A, shift: false, alt: false, control: false);
        Assert.AreEqual('A', keyEvent.KeyChar);
        Assert.AreEqual(ConsoleKey.A, keyEvent.ConsoleKey);
        Assert.IsFalse(keyEvent.Shift);
        Assert.IsFalse(keyEvent.Alt);
        Assert.IsFalse(keyEvent.Control);
        Assert.AreEqual(KeyPressType.Down, keyEvent.KeyPressType);
    }

    [TestMethod]
    public void KeyEventConstructorWithKeyPressType()
    {
        var keyEvent = new KeyEvent('A', ConsoleKey.A, shift: false, alt: false, control: false, KeyPressType.Up);
        Assert.AreEqual(KeyPressType.Up, keyEvent.KeyPressType);
    }

    [TestMethod]
    public void KeyEventDefaultKeyPressType()
    {
        var keyEvent = new KeyEvent('A', ConsoleKey.A, shift: false, alt: false, control: false);
        Assert.AreEqual(KeyPressType.Down, keyEvent.KeyPressType);
    }

    [TestMethod]
    public void KeyEventWithModifiers()
    {
        var keyEvent = new KeyEvent('A', ConsoleKey.A, shift: true, alt: true, control: true, KeyPressType.Down);
        Assert.IsTrue(keyEvent.Shift);
        Assert.IsTrue(keyEvent.Alt);
        Assert.IsTrue(keyEvent.Control);
    }

    [TestMethod]
    public void KeyEventDefaultKeyChar()
    {
        var keyEvent = new KeyEvent(default, ConsoleKey.Escape, shift: false, alt: false, control: false);
        Assert.AreEqual('\0', keyEvent.KeyChar);
    }

    [TestMethod]
    public void KeyEventIsIdentical()
    {
        var keyEvent1 = new KeyEvent('A', ConsoleKey.A, shift: true, alt: false, control: false);
        var keyEvent2 = new KeyEvent('A', ConsoleKey.A, shift: true, alt: false, control: false);
        Assert.IsTrue(keyEvent1.IsIdentical(keyEvent2));
    }

    [TestMethod]
    public void KeyEventIsNotIdenticalDifferentKeyChar()
    {
        var keyEvent1 = new KeyEvent('A', ConsoleKey.A, shift: false, alt: false, control: false);
        var keyEvent2 = new KeyEvent('B', ConsoleKey.A, shift: false, alt: false, control: false);
        Assert.IsFalse(keyEvent1.IsIdentical(keyEvent2));
    }

    [TestMethod]
    public void KeyEventIsNotIdenticalDifferentConsoleKey()
    {
        var keyEvent1 = new KeyEvent('A', ConsoleKey.A, shift: false, alt: false, control: false);
        var keyEvent2 = new KeyEvent('A', ConsoleKey.B, shift: false, alt: false, control: false);
        Assert.IsFalse(keyEvent1.IsIdentical(keyEvent2));
    }

    [TestMethod]
    public void KeyEventIsNotIdenticalDifferentModifier()
    {
        var keyEvent1 = new KeyEvent('A', ConsoleKey.A, shift: false, alt: false, control: false);
        var keyEvent2 = new KeyEvent('A', ConsoleKey.A, shift: true, alt: false, control: false);
        Assert.IsFalse(keyEvent1.IsIdentical(keyEvent2));
    }

    [TestMethod]
    public void KeyEventEquality()
    {
        var keyEvent1 = new KeyEvent('A', ConsoleKey.A, shift: true, alt: false, control: false, KeyPressType.Down);
        var keyEvent2 = new KeyEvent('A', ConsoleKey.A, shift: true, alt: false, control: false, KeyPressType.Down);
        Assert.AreEqual(keyEvent1, keyEvent2);
    }

    [TestMethod]
    public void KeyEventInheritance()
    {
        var keyEvent = new KeyEvent('A', ConsoleKey.A, shift: true, alt: false, control: false);
        Assert.IsInstanceOfType(keyEvent, typeof(Key));
    }
}
