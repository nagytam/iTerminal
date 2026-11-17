using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iTerminal.Core.Controls.StatusBar;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Controls.StatusBar.Test;

/// <summary>The bottom row: the hot key bar followed by the date button.</summary>
[TestClass]
public class TerminalStatusBarTest
{
    private static (TerminalStatusBar Bar, Control Parent) Create(int width = 200)
    {
        var parent = new Control(TestTheme()) { ViewSize = new Size(width, 30) };
        var bar = new TerminalStatusBar(null!, null!, TestTheme());
        parent.AddControl(bar);
        return (bar, parent);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutInjectedPartsItBuildsItsOwn()
    {
        var (bar, _) = Create();

        Assert.AreEqual(nameof(TerminalStatusBar), bar.Name);
        Assert.AreEqual(1, bar.Controls.AsSnapshot().OfType<HotKeys>().Count());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SpansTheScreenOnOneRow()
    {
        var (bar, _) = Create(width: 150);

        bar.OnInitializeLayout();

        Assert.AreEqual(new Size(150, 1), bar.ViewSize);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheDateButtonFollowsTheHotKeysDirectly()
    {
        var (bar, _) = Create();
        bar.OnInitializeLayout();

        bar.Draw(new iConsole.Test.Core.ScreenBuffer(200, 30));

        var hotKeys = bar.Controls.AsSnapshot().OfType<HotKeys>().Single();
        var dateButton = bar.Controls.AsSnapshot().OfType<Button>().Single(b => b.Title == "Calendar");
        Assert.AreEqual(hotKeys.Position.X + hotKeys.ViewSize.Width, dateButton.Position.X);
        Assert.AreEqual(hotKeys.Position.Y, dateButton.Position.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheDateButtonSaysWhatItOpens()
    {
        var (bar, _) = Create(width: 120);
        bar.OnInitializeLayout();
        var buffer = new iConsole.Test.Core.ScreenBuffer(120, 30);

        bar.Draw(buffer);

        // The button used to be a bare "[*]" that gave no hint of its calendar.
        StringAssert.Contains(buffer.Line((int)bar.Position.Y), "[Calendar]");
        Assert.IsFalse(buffer.Line((int)bar.Position.Y).Contains("[*]"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheDateButtonStaysWholeWhenTheRowIsNarrow()
    {
        var (bar, _) = Create(width: 60);
        bar.OnInitializeLayout();
        var buffer = new iConsole.Test.Core.ScreenBuffer(60, 30);

        bar.Draw(buffer);

        var dateButton = bar.Controls.AsSnapshot().OfType<Button>().Single(b => b.Title == "Calendar");
        Assert.IsTrue(dateButton.Position.X + dateButton.ViewSize.Width <= 60, "the date button must stay on screen");
        StringAssert.Contains(buffer.Line((int)bar.Position.Y), "[Calendar]");
    }
}
