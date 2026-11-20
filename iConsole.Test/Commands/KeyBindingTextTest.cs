using iConsole.Commands;
using iConsole.Data;

namespace iConsole.Test.Commands;

[TestClass]
public class KeyBindingTextTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("F5", ConsoleKey.F5, false, false, false)]
    [DataRow("Ctrl+Shift+F5", ConsoleKey.F5, true, false, true)]
    [DataRow("Shift+Ctrl+F5", ConsoleKey.F5, true, false, true)]
    [DataRow("alt+f1", ConsoleKey.F1, false, true, false)]
    [DataRow("Ctrl+a", ConsoleKey.A, false, false, true)]
    [DataRow("ctrl+f5", ConsoleKey.F5, false, false, true)]
    [DataRow("Ctrl+5", ConsoleKey.D5, false, false, true)]
    [DataRow("Esc", ConsoleKey.Escape, false, false, false)]
    [DataRow("PgDn", ConsoleKey.PageDown, false, false, false)]
    [DataRow("Ctrl+Del", ConsoleKey.Delete, false, false, true)]
    [DataRow("NumPad7", ConsoleKey.NumPad7, false, false, false)]
    [DataRow("Ctrl+Shift+Alt+Up", ConsoleKey.UpArrow, true, true, true)]
    public void ParsesTheKeyTheHelpScreenPrints(string text, ConsoleKey consoleKey, bool shift, bool alt, bool control)
    {
        bool parsed = KeyBindingText.TryParse(text, out Key? key, out string? error);

        Assert.IsTrue(parsed, error);
        Assert.IsNotNull(key);
        Assert.AreEqual(consoleKey, key.ConsoleKey);
        Assert.AreEqual(shift, key.Shift);
        Assert.AreEqual(alt, key.Alt);
        Assert.AreEqual(control, key.Control);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("Ctrl+Shift+F5")]
    [DataRow("Alt+F1")]
    [DataRow("Ctrl+Shift+Alt+Up")]
    [DataRow("Ctrl+Oem5")]
    [DataRow("Ctrl+Del")]
    public void FormatThenParseGivesTheSameKey(string printed)
    {
        Assert.IsTrue(KeyBindingText.TryParse(printed, out Key? parsed, out _));

        string reprinted = Command.FormatKey(parsed);
        Assert.IsTrue(KeyBindingText.TryParse(reprinted, out Key? again, out _));
        Assert.AreEqual(reprinted, Command.FormatKey(again));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("Hyper+F5")]
    [DataRow("Ctrl+Ctrl+F5")]
    [DataRow("F99")]
    [DataRow("Ctrl+")]
    [DataRow("300")]
    [DataRow("13")]
    public void RefusesTextThatIsNotAKey(string text)
    {
        bool parsed = KeyBindingText.TryParse(text, out Key? key, out string? error);

        Assert.IsFalse(parsed);
        Assert.IsNull(key);
        Assert.IsFalse(string.IsNullOrEmpty(error), "a refusal must say why");
    }
}
