using iConsole.Test.Core.Automation;

namespace iConsole.Test.Automation;

[TestClass]
public class KeyChordTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("F1", ConsoleKey.F1)]
    [DataRow("f5", ConsoleKey.F5)]
    [DataRow("F12", ConsoleKey.F12)]
    [DataRow("Enter", ConsoleKey.Enter)]
    [DataRow("esc", ConsoleKey.Escape)]
    [DataRow("Tab", ConsoleKey.Tab)]
    [DataRow("PgDn", ConsoleKey.PageDown)]
    [DataRow("Up", ConsoleKey.UpArrow)]
    [DataRow("Delete", ConsoleKey.Delete)]
    [DataRow("Space", ConsoleKey.Spacebar)]
    public void NamedKeysParse(string chord, ConsoleKey expected)
    {
        var key = KeyChord.Parse(chord);

        Assert.AreEqual(expected, key.ConsoleKey);
        Assert.IsFalse(key.Shift || key.Alt || key.Control);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EnterCarriesACarriageReturn() => Assert.AreEqual('\r', KeyChord.Parse("Enter").KeyChar);

    [Timeout(60_000)]
    [TestMethod]
    public void CtrlLetterCarriesItsControlCode()
    {
        var key = KeyChord.Parse("Ctrl+G");

        Assert.AreEqual(ConsoleKey.G, key.ConsoleKey);
        Assert.IsTrue(key.Control);
        Assert.IsFalse(key.Shift, "a chord's letter is written in capitals without meaning Shift");
        Assert.AreEqual((char)7, key.KeyChar);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ShiftIsOnlyAddedToAChordWhenItIsNamed()
    {
        var key = KeyChord.Parse("Ctrl+Shift+M");

        Assert.IsTrue(key.Shift && key.Control);
        Assert.AreEqual(ConsoleKey.M, key.ConsoleKey);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AltLetterCarriesTheLetter()
    {
        // The file panel's type-to-search reads the character of an Alt chord.
        var key = KeyChord.Parse("Alt+g");

        Assert.IsTrue(key.Alt);
        Assert.AreEqual('g', key.KeyChar);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ModifiersCombineInAnyOrderAndCase()
    {
        var key = KeyChord.Parse("shift+CTRL+Alt+F3");

        Assert.AreEqual(ConsoleKey.F3, key.ConsoleKey);
        Assert.IsTrue(key.Shift && key.Control && key.Alt);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ACapitalLetterIsShifted()
    {
        var key = KeyChord.Parse("T");

        Assert.AreEqual(ConsoleKey.T, key.ConsoleKey);
        Assert.IsTrue(key.Shift);
        Assert.AreEqual('T', key.KeyChar);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ShiftTabIsTabWithShift()
    {
        var key = KeyChord.Parse("Shift+Tab");

        Assert.AreEqual(ConsoleKey.Tab, key.ConsoleKey);
        Assert.IsTrue(key.Shift);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThePlusKeyCanBeNamed()
    {
        Assert.AreEqual('+', KeyChord.Parse("+").KeyChar);

        var key = KeyChord.Parse("Ctrl++");
        Assert.AreEqual('+', key.KeyChar);
        Assert.IsTrue(key.Control);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DigitsMapToTheDigitKeys()
    {
        var key = KeyChord.Parse("Ctrl+3");

        Assert.AreEqual(ConsoleKey.D3, key.ConsoleKey);
        Assert.AreEqual('3', key.KeyChar);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextBecomesOneKeyPerCharacter()
    {
        var keys = KeyChord.FromText("Hi 2.");

        CollectionAssert.AreEqual(new[] { 'H', 'i', ' ', '2', '.' }, keys.Select(k => k.KeyChar).ToArray());
        Assert.AreEqual(ConsoleKey.Spacebar, keys[2].ConsoleKey);
        Assert.IsTrue(keys[0].Shift);
        Assert.IsFalse(keys[1].Shift);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ANewLineInTextIsEnter()
    {
        var key = KeyChord.FromChar('\n');

        Assert.AreEqual(ConsoleKey.Enter, key.ConsoleKey);
        Assert.AreEqual('\r', key.KeyChar);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("Hyper+A")]
    [DataRow("F25")]
    [DataRow("NoSuchKey")]
    public void AnUnknownChordIsRejected(string chord) =>
        Assert.ThrowsExactly<FormatException>(() => KeyChord.Parse(chord));

    [Timeout(60_000)]
    [TestMethod]
    public void AnEmptyChordIsRejected() =>
        Assert.ThrowsExactly<ArgumentException>(() => KeyChord.Parse(string.Empty));
}
