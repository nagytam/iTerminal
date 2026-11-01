using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class LinkChipsScrollBarTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static MouseEvent Click(long x, long y) => new() { Position = new Position(x, y), LeftClicked = true };

    private static KeyEvent Key(ConsoleKey key) => new('\0', key, false, false, false);

    [Timeout(60_000)]
    [TestMethod]
    public void LinkLabel_Width_FollowsText()
    {
        LinkLabel link = new(Theme()) { Text = "Help" };
        Assert.AreEqual(4, (int)link.ViewSize.Width);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task LinkLabel_ClickAndEnter_RaiseActivated()
    {
        LinkLabel link = new(Theme()) { Text = "Help", Position = new Position(0, 0) };
        int count = 0;
        link.Activated += (_, _) => count++;
        await link.HandleMouseEventAsync(Click(1, 0));
        link.SetFocus();
        await link.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Assert.AreEqual(2, count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task LinkLabel_Disabled_IgnoresClick()
    {
        LinkLabel link = new(Theme()) { Text = "Help", Position = new Position(0, 0), IsEnabled = false };
        int count = 0;
        link.Activated += (_, _) => count++;
        await link.HandleMouseEventAsync(Click(1, 0));
        Assert.AreEqual(0, count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Chips_Add_RejectsBlankAndDuplicates()
    {
        Chips chips = new(Theme());
        Assert.IsTrue(chips.Add("rock"));
        Assert.IsFalse(chips.Add("ROCK"));
        Assert.IsFalse(chips.Add("  "));
        Assert.AreEqual(1, chips.Items.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Chips_Draw_ShowsChipsAndPlus()
    {
        MockBuffer buffer = new(new Size(40, 1));
        Chips chips = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 1) };
        chips.Add("rock");
        chips.Add("jazz");
        chips.Draw(buffer);
        Assert.AreEqual("[rock ×] [jazz ×] [+]", buffer.ContentAsString.Split(Environment.NewLine)[0].TrimEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Chips_ClickCross_RemovesChip()
    {
        Chips chips = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 1) };
        chips.Add("rock");
        chips.Add("jazz");
        await chips.HandleMouseEventAsync(Click(6, 0));
        CollectionAssert.AreEqual(new[] { "jazz" }, chips.Items.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Chips_EnterOnPlus_RaisesAddRequested()
    {
        Chips chips = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 1) };
        chips.Add("rock");
        chips.SetFocus();
        chips.ActiveIndex = 1;
        bool requested = false;
        chips.AddRequested += (_, _) => requested = true;
        await chips.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Assert.IsTrue(requested);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Chips_Delete_RemovesActiveChip()
    {
        Chips chips = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 1) };
        chips.Add("a");
        chips.Add("b");
        chips.SetFocus();
        chips.ActiveIndex = 0;
        await chips.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        CollectionAssert.AreEqual(new[] { "b" }, chips.Items.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Chips_Visible_SlidesToKeepActiveChipOnScreen()
    {
        Chips chips = new(Theme()) { ViewSize = new Size(20, 1) };
        for (int i = 0; i < 6; i++)
        {
            chips.Add("item" + i);
        }

        chips.ActiveIndex = 5;
        Assert.IsTrue(chips.Visible(20).Any(v => v.Index == 5));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ScrollBar_CursorForClick_ArrowsStepAndTrackJumps()
    {
        Assert.AreEqual(4, global::iConsole.Controls.ScrollBar.ScrollBar.CursorForClick(0, 10, 100, 5));
        Assert.AreEqual(6, global::iConsole.Controls.ScrollBar.ScrollBar.CursorForClick(9, 10, 100, 5));
        Assert.AreEqual(0, global::iConsole.Controls.ScrollBar.ScrollBar.CursorForClick(1, 10, 100, 50));
        Assert.AreEqual(99, global::iConsole.Controls.ScrollBar.ScrollBar.CursorForClick(8, 10, 100, 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ScrollBar_ClickDownArrow_MovesCursor()
    {
        ScrollableModel model = new() { ScrollableSize = new Size(1, 50), ViewSize = new Size(1, 10) };
        global::iConsole.Controls.ScrollBar.ScrollBar bar = new(Theme())
        {
            ScrollableModel = model,
            Position = new Position(0, 0),
            ViewSize = new Size(1, 10),
        };
        await bar.HandleMouseEventAsync(Click(0, 9));
        Assert.AreEqual(1, (int)model.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ScrollBar_Wheel_MovesCursorThreeRows()
    {
        ScrollableModel model = new() { ScrollableSize = new Size(1, 50), ViewSize = new Size(1, 10) };
        global::iConsole.Controls.ScrollBar.ScrollBar bar = new(Theme())
        {
            ScrollableModel = model,
            Position = new Position(0, 0),
            ViewSize = new Size(1, 10),
        };
        await bar.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 4), WheelDelta = -1 });
        Assert.AreEqual(3, (int)model.CursorPosition.Y);
    }

    private static Chips ThreeChips()
    {
        Chips chips = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 1) };
        chips.Add("a");
        chips.Add("b");
        chips.Add("c");
        chips.SetFocus();
        return chips;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Chips_HomeAndEnd_JumpToTheFirstChipAndThePlus()
    {
        Chips chips = ThreeChips();
        chips.ActiveIndex = 1;

        Assert.IsTrue(await chips.HandleKeyEventAsync(Key(ConsoleKey.End)));
        Assert.AreEqual(3, chips.ActiveIndex, "End lands on [+]");

        Assert.IsTrue(await chips.HandleKeyEventAsync(Key(ConsoleKey.Home)));
        Assert.AreEqual(0, chips.ActiveIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Chips_ReadOnly_EndStopsAtTheLastChip()
    {
        Chips chips = ThreeChips();
        chips.IsReadOnly = true;

        await chips.HandleKeyEventAsync(Key(ConsoleKey.End));

        Assert.AreEqual(2, chips.ActiveIndex, "a read-only list has no [+]");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Chips_TheWheel_StepsTheActiveChip_AndStopsAtTheEnds()
    {
        Chips chips = ThreeChips();
        chips.ActiveIndex = 0;
        MouseEvent down = new() { Position = new Position(1, 0), WheelDelta = -1 };
        MouseEvent up = new() { Position = new Position(1, 0), WheelDelta = 1 };

        Assert.IsTrue(await chips.HandleMouseEventAsync(down));
        Assert.AreEqual(1, chips.ActiveIndex);
        for (int i = 0; i < 6; i++)
        {
            await chips.HandleMouseEventAsync(down);
        }

        Assert.AreEqual(3, chips.ActiveIndex);
        await chips.HandleMouseEventAsync(up);
        Assert.AreEqual(2, chips.ActiveIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Chips_TheWheelElsewhereOrDisabled_IsIgnored()
    {
        Chips chips = ThreeChips();
        chips.ActiveIndex = 1;

        await chips.HandleMouseEventAsync(new MouseEvent { Position = new Position(30, 5), WheelDelta = -1 });
        Assert.AreEqual(1, chips.ActiveIndex);

        chips.IsEnabled = false;
        await chips.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 0), WheelDelta = -1 });
        Assert.AreEqual(1, chips.ActiveIndex);
    }
}
