using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class SearchBoxTest
{
    private static (SearchBox box, MockBuffer buffer) Create(string text = "", int width = 24)
    {
        var beep  = new Mock<IBeepService>();
        var dt    = new Mock<IDateTimeProviderService>();
        var theme = new Mock<IThemeService>();
        theme.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var box = new SearchBox(beep.Object, dt.Object, theme.Object)
        {
            Text = text,
            Position = new Position(0, 0),
            ViewSize = new Size(width, 1)
        };
        box.ScrollableModel.ViewSize = new Size(width, 1);
        box.ScrollableModel.ScrollableSize = new Size(text.Length, 1);

        var buffer = new MockBuffer(new Size(width, 1));
        return (box, buffer);
    }

    // ── Inherits TextBox ────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void IsInstanceOf_TextBox()
    {
        var (box, _) = Create();
        Assert.IsInstanceOfType(box, typeof(TextBox));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Typing_UpdatesText()
    {
        var (box, _) = Create();

        await box.HandleKeyEventAsync(new KeyEvent('h', ConsoleKey.H, false, false, false));
        await box.HandleKeyEventAsync(new KeyEvent('i', ConsoleKey.I, false, false, false));

        Assert.AreEqual("hi", box.Text);
    }

    // ── SearchChanged event ─────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task Typing_FiresSearchChanged()
    {
        var (box, _) = Create();
        string? received = null;
        box.SearchChanged += (_, v) => received = v;

        await box.HandleKeyEventAsync(new KeyEvent('a', ConsoleKey.A, false, false, false));

        Assert.AreEqual("a", received);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Typing_SearchChanged_PassesCurrentText()
    {
        var (box, _) = Create("foo");
        box.ScrollableModel.CursorPosition.X = 3;
        var received = new List<string>();
        box.SearchChanged += (_, v) => received.Add(v);

        await box.HandleKeyEventAsync(new KeyEvent('d', ConsoleKey.D, false, false, false));

        Assert.AreEqual("food", received[^1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NavigationKey_DoesNotFireSearchChanged()
    {
        var (box, _) = Create("abc");
        var calls = 0;
        box.SearchChanged += (_, _) => calls++;

        await box.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.LeftArrow, false, false, false));
        await box.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, false));

        Assert.AreEqual(0, calls);
    }

    // ── Escape / Cleared ────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_ClearsText()
    {
        var (box, _) = Create("hello");

        await box.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        Assert.AreEqual(string.Empty, box.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_FiresCleared()
    {
        var (box, _) = Create("hello");
        var fired = false;
        box.Cleared += (_, _) => fired = true;

        await box.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        Assert.IsTrue(fired);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_FiresSearchChangedWithEmptyString()
    {
        var (box, _) = Create("hello");
        string? received = null;
        box.SearchChanged += (_, v) => received = v;

        await box.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        Assert.AreEqual(string.Empty, received);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_FiresSearchChangedOnce()
    {
        var (box, _) = Create("hello");
        var calls = 0;
        box.SearchChanged += (_, _) => calls++;

        await box.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        Assert.AreEqual(1, calls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgrammaticClear_FiresSearchChangedSoFiltersFollowTheEmptyBox()
    {
        var (box, _) = Create("hello");
        string? received = null;
        box.SearchChanged += (_, v) => received = v;

        box.Clear();

        Assert.AreEqual(string.Empty, received);
        Assert.AreEqual(string.Empty, box.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProgrammaticClear_OnEmptyBox_DoesNotFireSearchChanged()
    {
        var (box, _) = Create(string.Empty);
        var calls = 0;
        box.SearchChanged += (_, _) => calls++;

        box.Clear();

        Assert.AreEqual(0, calls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_ReturnsHandledTrue()
    {
        var (box, _) = Create();

        var handled = await box.HandleKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        Assert.IsTrue(handled);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_ResetsCursorAndScroll()
    {
        var (box, _) = Create("hello");
        box.ScrollableModel.CursorPosition.X = 5;
        box.ScrollableModel.ScrollPosition.X = 2;

        await box.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        Assert.AreEqual(0u, box.ScrollableModel.CursorPosition.X);
        Assert.AreEqual(0u, box.ScrollableModel.ScrollPosition.X);
    }

    // ── Rendering ───────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ShowsPrefix()
    {
        var (box, buffer) = Create();

        box.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("/"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ShowsTextInField()
    {
        var (box, buffer) = Create("query");

        box.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("query"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ShowsBrackets()
    {
        var (box, buffer) = Create();

        box.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("[") || content.Contains("<"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_CustomPrefix_Rendered()
    {
        var (box, buffer) = Create();
        box.Prefix = "?";

        box.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("?"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_PrefixAppearsBeforeField()
    {
        var (box, buffer) = Create("abc");

        box.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.IndexOf('/') < content.IndexOf('['));
    }

    // ── Discoverability via Commands (so Help lists them) ──────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Commands_IncludeEscapeWithNonEmptyKeyDisplayText()
    {
        var (box, _) = Create();

        Assert.IsTrue(box.Commands.Any(c => c.Key!.ConsoleKey == ConsoleKey.Escape
            && !string.IsNullOrEmpty(c.KeyDisplayText)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Clear_ResetsTextCursorAndScroll_AndFiresCleared()
    {
        var (box, _) = Create("hello");
        box.ScrollableModel.CursorPosition.X = 5;
        box.ScrollableModel.ScrollPosition.X = 2;
        var fired = false;
        box.Cleared += (_, _) => fired = true;

        box.Clear();

        Assert.AreEqual(string.Empty, box.Text);
        Assert.AreEqual(0u, box.ScrollableModel.CursorPosition.X);
        Assert.AreEqual(0u, box.ScrollableModel.ScrollPosition.X);
        Assert.IsTrue(fired);
    }
}
