using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Tabs;

/// <summary>A tab strip at awkward sizes and in awkward states.</summary>
[TestClass]
public class TabEdgeCaseTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static Tab NewTab(Orientation orientation, uint width, uint height, params string[] names)
    {
        Tab tab = new(Theme()) { Orientation = orientation, Position = new Position(0, 0), ViewSize = new Size(width, height) };
        foreach (string name in names)
        {
            tab.AddTabPage(new TabPage(name, Theme()));
        }

        return tab;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_AtTinySizes_NeverThrows()
    {
        foreach (Orientation orientation in new[] { Orientation.Horizontal, Orientation.Vertical })
        {
            foreach ((uint width, uint height) in new[] { (0u, 0u), (1u, 1u), (2u, 2u), (3u, 3u), (5u, 4u), (12u, 6u) })
            {
                Tab tab = NewTab(orientation, width, height, "General", "Advanced", "About");
                tab.OnInitializeLayout();
                tab.Draw(new ScreenBuffer((int)Math.Max(1, width), (int)Math.Max(1, height)));
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickOnAPageName_OfADisabledTab_DoesNotSwitch()
    {
        Tab tab = NewTab(Orientation.Horizontal, 40, 10, "One", "Two");
        tab.OnInitializeLayout();
        tab.IsEnabled = false;
        // " One " is 7 columns (name + 4), so column 10 is inside the second name.
        await tab.HandleMouseEventAsync(new MouseEvent { Position = new Position(10, 1), LeftClicked = true });
        Assert.AreEqual(0, tab.FocusedTabPage);
        tab.IsEnabled = true;
        await tab.HandleMouseEventAsync(new MouseEvent { Position = new Position(10, 1), LeftClicked = true });
        Assert.AreEqual(1, tab.FocusedTabPage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RightClickOrWheelOnTheStrip_DoesNotSwitch()
    {
        Tab tab = NewTab(Orientation.Horizontal, 40, 10, "One", "Two");
        tab.OnInitializeLayout();
        await tab.HandleMouseEventAsync(new MouseEvent { Position = new Position(10, 1), RightClicked = true });
        await tab.HandleMouseEventAsync(new MouseEvent { Position = new Position(10, 1), WheelDelta = 1 });
        Assert.AreEqual(0, tab.FocusedTabPage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATabWithoutPages_IgnoresKeysAndClicks()
    {
        Tab tab = NewTab(Orientation.Vertical, 30, 10);
        foreach (ConsoleKey key in new[] { ConsoleKey.LeftArrow, ConsoleKey.RightArrow, ConsoleKey.UpArrow, ConsoleKey.DownArrow, ConsoleKey.Enter })
        {
            Assert.IsFalse(await tab.HandleKeyEventAsync(new KeyEvent('\0', key, false, false, true)));
        }

        Assert.IsFalse(await tab.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 1), LeftClicked = true }));
        Assert.AreEqual(0, tab.FocusedTabPage);
        Assert.IsNull(tab.FocusedPage);
        Assert.AreEqual(0, tab.GetKeyHints().Count());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlArrows_StopAtTheFirstAndLastPage_AndAltOrShiftDoNotSwitch()
    {
        Tab tab = NewTab(Orientation.Horizontal, 40, 10, "One", "Two", "Three");
        tab.OnInitializeLayout();
        await tab.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.LeftArrow, false, false, true));
        Assert.AreEqual(0, tab.FocusedTabPage);
        for (int i = 0; i < 5; i++)
        {
            await tab.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.RightArrow, false, false, true));
        }

        Assert.AreEqual(2, tab.FocusedTabPage);
        await tab.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.LeftArrow, true, false, true));
        await tab.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.LeftArrow, false, true, true));
        Assert.AreEqual(2, tab.FocusedTabPage);
    }
}
