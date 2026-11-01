using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class PasswordBoxTest
{
    private static (PasswordBox box, MockBuffer buffer) Create(string text = "", int width = 20)
    {
        var beep = new Mock<IBeepService>();
        var dt   = new Mock<IDateTimeProviderService>();
        var theme = new Mock<IThemeService>();
        theme.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var box = new PasswordBox(beep.Object, dt.Object, theme.Object)
        {
            Text = text,
            Label = "Pass",
            Position = new Position(0, 0),
            ViewSize = new Size(width, 1)
        };
        box.ScrollableModel.ViewSize = new Size(width, 1);
        box.ScrollableModel.ScrollableSize = new Size(text.Length, 1);

        var buffer = new MockBuffer(new Size(width, 1));
        return (box, buffer);
    }

    // ── Inherits TextBox editing ────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void IsInstanceOf_TextBox()
    {
        var (box, _) = Create();
        Assert.IsInstanceOfType(box, typeof(TextBox));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TypingCharacters_UpdatesTextProperty()
    {
        var (box, _) = Create();

        await box.HandleKeyEventAsync(new KeyEvent('a', ConsoleKey.A, false, false, false));
        await box.HandleKeyEventAsync(new KeyEvent('b', ConsoleKey.B, false, false, false));

        Assert.AreEqual("ab", box.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Backspace_RemovesLastCharacter()
    {
        var (box, _) = Create("abc");
        box.ScrollableModel.CursorPosition.X = 3;

        await box.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Backspace, false, false, false));

        Assert.AreEqual("ab", box.Text);
    }

    // ── Masking ─────────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_MasksRealText_WithBullets()
    {
        var (box, buffer) = Create("secret");

        box.Draw(buffer);

        Assert.IsFalse(buffer.ContentAsString.Contains("secret"),
            "Real password must not appear in the buffer");
        Assert.IsTrue(buffer.ContentAsString.Contains("●"),
            "Mask character '●' must appear in the buffer");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_MaskCount_MatchesTextLength()
    {
        const string password = "abc";
        var (box, buffer) = Create(password);

        box.Draw(buffer);

        var bullets = buffer.ContentAsString.Count(c => c == '●');
        Assert.AreEqual(password.Length, bullets,
            "Number of '●' characters must equal the password length");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_EmptyPassword_NoMaskChars()
    {
        var (box, buffer) = Create("");

        box.Draw(buffer);

        Assert.IsFalse(buffer.ContentAsString.Contains('●'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ShowsLabel()
    {
        var (box, buffer) = Create("x");

        box.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Pass"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ShowsBrackets()
    {
        var (box, buffer) = Create("x");

        box.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("[") || content.Contains("<"),
            "Field border must be present");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_AfterTyping_MaskCountUpdates()
    {
        var (box, buffer1) = Create("hi");

        box.Draw(buffer1);
        var before = buffer1.ContentAsString.Count(c => c == '●');

        // Simulate typing one more character
        box.Text = "hix";
        box.ScrollableModel.CursorPosition.X = 3;
        var buffer2 = new MockBuffer(new Size(20, 1));
        box.Draw(buffer2);
        var after = buffer2.ContentAsString.Count(c => c == '●');

        Assert.AreEqual(before + 1, after);
    }

    // ── IsReadOnly ──────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadOnly_IgnoresTypedCharacters()
    {
        var (box, _) = Create("initial");
        box.IsReadOnly = true;

        await box.HandleKeyEventAsync(new KeyEvent('z', ConsoleKey.Z, false, false, false));

        Assert.AreEqual("initial", box.Text);
    }
}
