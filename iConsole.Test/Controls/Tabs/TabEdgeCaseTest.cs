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

    private static string Show(ScreenBuffer screen) => string.Join(Environment.NewLine, screen.Lines);

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
    public async Task AVerticalStripTooShortForTwoRowsEach_UsesOneRowEach_AndKeepsInsideItsHeight()
    {
        string[] names = ["One", "Two", "Three", "Four", "Five", "Six", "Seven"];
        Tab tab = NewTab(Orientation.Vertical, 30, 10, names);
        tab.OnInitializeLayout();

        ScreenBuffer screen = ScreenBuffer.Render(tab, 30, 12);

        // Two rows each would be 15 rows: the names would hang below a 10-row strip. At one row each all seven show,
        // the bottom border follows the last name, and nothing is drawn under the strip.
        for (int i = 0; i < names.Length; i++)
        {
            Assert.IsTrue(screen.Lines[1 + i].Contains(names[i]), Show(screen));
        }

        Assert.IsTrue(screen.Lines[8].TrimStart().StartsWith('╚'), Show(screen));
        Assert.IsTrue(string.IsNullOrWhiteSpace(screen.Lines[10]) && string.IsNullOrWhiteSpace(screen.Lines[11]), Show(screen));

        await tab.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 5), LeftClicked = true });
        Assert.AreEqual(4, tab.FocusedTabPage, "a click on the fifth name is the fifth page");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AVeryShortVerticalStrip_ScrollsToKeepTheFocusedPageInView()
    {
        string[] names = ["One", "Two", "Three", "Four", "Five", "Six", "Seven"];
        Tab tab = NewTab(Orientation.Vertical, 30, 5, names);
        tab.OnInitializeLayout();
        tab.FocusedTabPage = 6;

        ScreenBuffer screen = ScreenBuffer.Render(tab, 30, 5);

        Assert.IsTrue(screen.Lines.Any(l => l.Contains("Seven")), Show(screen));
        Assert.IsFalse(screen.Lines.Any(l => l.Contains("One")), Show(screen));
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
