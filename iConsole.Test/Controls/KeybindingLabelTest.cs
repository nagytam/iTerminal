using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class KeybindingLabelTest
{
    private static Mock<IThemeService> ThemeMock()
    {
        var m = new Mock<IThemeService>();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m;
    }

    private static KeybindingLabel Create(Key? binding = null, string label = "", int width = 20)
    {
        return new KeybindingLabel(ThemeMock().Object)
        {
            Binding = binding,
            Label = label,
            Position = new Position(0, 0),
            ViewSize = new Size(width, 1)
        };
    }

    // ── KeyText formatting ──────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void KeyText_FunctionKey_FormatsCorrectly()
    {
        var ctrl = Create(new Key(ConsoleKey.F5, false, false, false));
        Assert.AreEqual("F5", ctrl.KeyText);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyText_WithControl_PrefixesCtrl()
    {
        var ctrl = Create(new Key(ConsoleKey.S, false, false, true));
        Assert.AreEqual("Ctrl+S", ctrl.KeyText);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyText_WithAlt_PrefixesAlt()
    {
        var ctrl = Create(new Key(ConsoleKey.F4, false, true, false));
        Assert.AreEqual("Alt+F4", ctrl.KeyText);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyText_WithShift_PrefixesShift()
    {
        var ctrl = Create(new Key(ConsoleKey.Tab, true, false, false));
        Assert.AreEqual("Shift+Tab", ctrl.KeyText);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyText_AllModifiers_OrderIsCtrlAltShift()
    {
        var ctrl = Create(new Key(ConsoleKey.Delete, true, true, true));
        Assert.AreEqual("Ctrl+Alt+Shift+Del", ctrl.KeyText);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyText_SpecialKeys_UseShortNames()
    {
        Assert.AreEqual("Enter",  Create(new Key(ConsoleKey.Enter,     false, false, false)).KeyText);
        Assert.AreEqual("Esc",    Create(new Key(ConsoleKey.Escape,    false, false, false)).KeyText);
        Assert.AreEqual("Space",  Create(new Key(ConsoleKey.Spacebar,  false, false, false)).KeyText);
        Assert.AreEqual("Tab",    Create(new Key(ConsoleKey.Tab,       false, false, false)).KeyText);
        Assert.AreEqual("Bksp",   Create(new Key(ConsoleKey.Backspace, false, false, false)).KeyText);
        Assert.AreEqual("Del",    Create(new Key(ConsoleKey.Delete,    false, false, false)).KeyText);
        Assert.AreEqual("Home",   Create(new Key(ConsoleKey.Home,      false, false, false)).KeyText);
        Assert.AreEqual("End",    Create(new Key(ConsoleKey.End,       false, false, false)).KeyText);
        Assert.AreEqual("PgUp",   Create(new Key(ConsoleKey.PageUp,    false, false, false)).KeyText);
        Assert.AreEqual("PgDn",   Create(new Key(ConsoleKey.PageDown,  false, false, false)).KeyText);
        Assert.AreEqual("Ins",    Create(new Key(ConsoleKey.Insert,    false, false, false)).KeyText);
        Assert.AreEqual("↑",      Create(new Key(ConsoleKey.UpArrow,   false, false, false)).KeyText);
        Assert.AreEqual("↓",      Create(new Key(ConsoleKey.DownArrow, false, false, false)).KeyText);
        Assert.AreEqual("←",      Create(new Key(ConsoleKey.LeftArrow, false, false, false)).KeyText);
        Assert.AreEqual("→",      Create(new Key(ConsoleKey.RightArrow,false, false, false)).KeyText);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeyText_NullBinding_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, Create(null).KeyText);
    }

    // ── Rendering ───────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_RendersKeyInBrackets()
    {
        var ctrl = Create(new Key(ConsoleKey.F5, false, false, false));
        var buffer = new MockBuffer(new Size(20, 1));

        ctrl.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("[F5]"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_WithLabel_RendersLabelAfterKeyCap()
    {
        var ctrl = Create(new Key(ConsoleKey.F5, false, false, false), label: "Refresh");
        var buffer = new MockBuffer(new Size(20, 1));

        ctrl.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("[F5]"));
        Assert.IsTrue(content.Contains("Refresh"));
        Assert.IsTrue(content.IndexOf("[F5]", StringComparison.Ordinal)
                    < content.IndexOf("Refresh", StringComparison.Ordinal));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_NoLabel_OnlyKeyCap()
    {
        var ctrl = Create(new Key(ConsoleKey.F1, false, false, false), label: "", width: 6);
        var buffer = new MockBuffer(new Size(6, 1));

        ctrl.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("[F1]"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_NullBinding_WritesNothing()
    {
        var ctrl = Create(null);
        var buffer = new MockBuffer(new Size(20, 1));

        ctrl.Draw(buffer);

        Assert.IsTrue(string.IsNullOrWhiteSpace(buffer.ContentAsString));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_PadsToViewWidth()
    {
        var ctrl = Create(new Key(ConsoleKey.F1, false, false, false), label: "Hi", width: 20);
        var buffer = new MockBuffer(new Size(20, 1));

        ctrl.Draw(buffer);

        var line = buffer.ContentAsString.Split(Environment.NewLine)[0];
        Assert.AreEqual(20, line.Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_CtrlS_RendersFullModifierCap()
    {
        var ctrl = Create(new Key(ConsoleKey.S, false, false, true), label: "Save");
        var buffer = new MockBuffer(new Size(20, 1));

        ctrl.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("[Ctrl+S]"));
        Assert.IsTrue(buffer.ContentAsString.Contains("Save"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_AtPosition_RenderedOnCorrectRow()
    {
        var ctrl = new KeybindingLabel(ThemeMock().Object)
        {
            Binding = new Key(ConsoleKey.F2, false, false, false),
            Position = new Position(0, 1),
            ViewSize = new Size(10, 1)
        };
        var buffer = new MockBuffer(new Size(10, 3));

        ctrl.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(lines[1].Contains("[F2]"));
        Assert.IsFalse(lines[0].Contains("[F2]"));
    }

    // ── CanFocus ────────────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void CanFocus_IsFalse()
    {
        Assert.IsFalse(Create().CanFocus);
    }
}
