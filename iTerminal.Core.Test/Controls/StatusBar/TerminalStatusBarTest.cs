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

    [TestMethod]
    public void WithoutInjectedPartsItBuildsItsOwn()
    {
        var (bar, _) = Create();

        Assert.AreEqual(nameof(TerminalStatusBar), bar.Name);
        Assert.AreEqual(1, bar.Controls.AsSnapshot().OfType<HotKeys>().Count());
    }

    [TestMethod]
    public void SpansTheScreenOnOneRow()
    {
        var (bar, _) = Create(width: 150);

        bar.OnInitializeLayout();

        Assert.AreEqual(new Size(150, 1), bar.ViewSize);
    }

    [TestMethod]
    public void TheDateButtonFollowsTheHotKeysDirectly()
    {
        var (bar, _) = Create();
        bar.OnInitializeLayout();

        bar.Draw(new iConsole.Test.Core.ScreenBuffer(200, 30));

        var hotKeys = bar.Controls.AsSnapshot().OfType<HotKeys>().Single();
        var dateButton = bar.Controls.AsSnapshot().OfType<Button>().Single(b => b.Title == "*");
        Assert.AreEqual(hotKeys.Position.X + hotKeys.ViewSize.Width, dateButton.Position.X);
        Assert.AreEqual(hotKeys.Position.Y, dateButton.Position.Y);
    }
}
