using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

/// <summary>Hot keys, check marks and submenus of <see cref="ContextMenu"/>.</summary>
[TestClass]
public class ContextMenuSubmenuTest
{
    private readonly TestThemeService _theme = new();

    private static KeyEvent Key(ConsoleKey key, char c = ' ') => new(c, key, false, false, false);

    private static MouseEvent Click(long x, long y) => new() { Position = new Position(x, y), LeftClicked = true };

    private sealed record Setup(Control Root, Control Owner, ContextMenu Menu, List<string> Ran);

    /// <summary>
    /// Open, Edit (a submenu with Copy and Paste), Bold (a switch, on), Delete (disabled). The menu is at (10, 6); entry
    /// <c>i</c> is on screen row <c>6 + 1 + i</c>.
    /// </summary>
    private async Task<Setup> OpenAsync(long x = 10, long y = 6)
    {
        Control root = new(_theme) { ViewSize = new Size(60, 20) };
        Control owner = new(_theme) { Position = new Position(5, 5), ViewSize = new Size(20, 5), CanFocus = true };
        root.AddControl(owner);
        List<string> ran = [];
        Func<string, Func<Task>> run = name => () =>
        {
            ran.Add(name);
            return Task.CompletedTask;
        };

        ContextMenu menu = new(new PopoverService(), _theme);
        menu.Items.Add(new ContextMenuItem("&Open") { Execute = run("open") });
        ContextMenuItem edit = new("&Edit");
        edit.Items.Add(new ContextMenuItem("&Copy") { Execute = run("copy") });
        edit.Items.Add(new ContextMenuItem("&Paste") { Execute = run("paste") });
        menu.Items.Add(edit);
        menu.Items.Add(new ContextMenuItem("&Bold") { IsChecked = true, Execute = run("bold") });
        menu.Items.Add(new ContextMenuItem("&Delete") { IsEnabled = false, Execute = run("delete") });
        await menu.OpenAtAsync(owner, new Position(x, y));
        return new Setup(root, owner, menu, ran);
    }

    // ---- Hot keys ----

    [Timeout(60_000)]
    [TestMethod]
    public void AnAmpersandMarksTheHotKeyAndIsNotShown()
    {
        ContextMenuItem item = new("Save &As");

        Assert.AreEqual("Save As", item.Text);
        Assert.AreEqual('A', item.HotKey);
        Assert.AreEqual(5, item.HotKeyIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ADoubleAmpersandIsALiteralOne()
    {
        ContextMenuItem item = new("Fish && Chips");

        Assert.AreEqual("Fish & Chips", item.Text);
        Assert.IsNull(item.HotKey);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnAmpersandBeforeAnythingButALetterOrDigitIsLeftAlone()
    {
        Assert.AreEqual("Fish & Chips", new ContextMenuItem("Fish & Chips").Text);
        Assert.IsNull(new ContextMenuItem("Fish & Chips").HotKey);
        Assert.AreEqual("Tom &", new ContextMenuItem("Tom &").Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnlyTheFirstMarkerIsTheHotKey()
    {
        ContextMenuItem item = new("&Save &all");

        Assert.AreEqual("Save all", item.Text);
        Assert.AreEqual('S', item.HotKey);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ATitleWithoutAMarkerIsUnchanged()
    {
        ContextMenuItem item = new("Open");

        Assert.AreEqual("Open", item.Text);
        Assert.IsNull(item.HotKey);
        Assert.AreEqual(-1, item.HotKeyIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PressingAHotKeyChoosesTheEntryAtOnce()
    {
        Setup s = await OpenAsync();

        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.O, 'o'));

        CollectionAssert.AreEqual(new[] { "open" }, s.Ran);
        Assert.IsFalse(s.Menu.IsOpen);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AHotKeyIsNotCaseSensitive()
    {
        Setup s = await OpenAsync();

        await s.Menu.HandleKeyEventAsync(new KeyEvent('O', ConsoleKey.O, false, false, false));

        CollectionAssert.AreEqual(new[] { "open" }, s.Ran);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AHotKeyOfADisabledEntryDoesNothing()
    {
        Setup s = await OpenAsync();

        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.D, 'd'));

        Assert.AreEqual(0, s.Ran.Count);
        Assert.IsTrue(s.Menu.IsOpen);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AHotKeyOfASubmenuOpensIt()
    {
        Setup s = await OpenAsync();

        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.E, 'e'));

        Assert.IsNotNull(s.Menu.OpenSubmenu);
        Assert.IsTrue(s.Menu.IsOpen);
        Assert.AreEqual(0, s.Ran.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EntriesSharingAHotKeyAreSteppedThroughNotChosen()
    {
        Control root = new(_theme) { ViewSize = new Size(60, 20) };
        Control owner = new(_theme) { ViewSize = new Size(10, 1) };
        root.AddControl(owner);
        List<string> ran = [];
        ContextMenu menu = new(new PopoverService(), _theme);
        menu.Items.Add(new ContextMenuItem("&Copy") { Execute = () => { ran.Add("copy"); return Task.CompletedTask; } });
        menu.Items.Add(new ContextMenuItem("&Close") { Execute = () => { ran.Add("close"); return Task.CompletedTask; } });
        await menu.OpenAtAsync(owner, new Position(2, 2));

        await menu.HandleKeyEventAsync(Key(ConsoleKey.C, 'c'));

        Assert.AreEqual(1, menu.SelectedIndex);
        Assert.AreEqual(0, ran.Count, "pressing it again, not the first press, chooses");
        await menu.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        CollectionAssert.AreEqual(new[] { "close" }, ran);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ALetterThatIsNoOnesHotKeyStillJumpsToATitleThatStartsWithIt()
    {
        Control root = new(_theme) { ViewSize = new Size(60, 20) };
        Control owner = new(_theme) { ViewSize = new Size(10, 1) };
        root.AddControl(owner);
        ContextMenu menu = new(new PopoverService(), _theme);
        menu.Items.Add(new ContextMenuItem("Alpha"));
        menu.Items.Add(new ContextMenuItem("Beta"));
        await menu.OpenAtAsync(owner, new Position(2, 2));

        await menu.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));

        Assert.AreEqual(1, menu.SelectedIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheHotKeyIsDrawnUnderlinedAndTheAmpersandIsNot()
    {
        Setup s = await OpenAsync();
        ScreenBuffer buffer = new(60, 20);

        s.Menu.Draw(buffer);

        string open = buffer.Line(7);
        Assert.IsFalse(open.Contains('&'));
        int column = open.IndexOf("Open", StringComparison.Ordinal);
        Assert.IsTrue(buffer.Cell(column, 7).IsUnderline, "the O");
        Assert.IsFalse(buffer.Cell(column + 1, 7).IsUnderline, "the p");
    }

    // ---- Check marks ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACheckedEntryIsDrawnWithAMarkAndAnUncheckedOneWithoutIt()
    {
        Setup s = await OpenAsync();
        s.Menu.Items.Add(new ContextMenuItem("Italic") { IsChecked = false });
        await s.Menu.OpenAtAsync(s.Owner, new Position(10, 6));
        ScreenBuffer buffer = new(60, 20);

        s.Menu.Draw(buffer);

        Assert.IsTrue(buffer.Line(9).Contains("✓ Bold", StringComparison.Ordinal), buffer.Line(9));
        Assert.IsTrue(buffer.Line(11).Contains("Italic", StringComparison.Ordinal));
        Assert.IsFalse(buffer.Line(11).Contains('✓'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EntriesLineUpWhetherOrNotTheyAreSwitches()
    {
        Setup s = await OpenAsync();
        ScreenBuffer buffer = new(60, 20);

        s.Menu.Draw(buffer);

        int open = buffer.Line(7).IndexOf("Open", StringComparison.Ordinal);
        int bold = buffer.Line(9).IndexOf("Bold", StringComparison.Ordinal);
        Assert.AreEqual(open, bold, "the check column is reserved for every entry");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ChoosingASwitchTurnsItOverBeforeItRuns()
    {
        Setup s = await OpenAsync();
        bool? seen = null;
        s.Menu.Items[2].Execute = () =>
        {
            seen = s.Menu.Items[2].IsChecked;
            return Task.CompletedTask;
        };

        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));

        Assert.AreEqual(false, seen, "Execute sees the new value");
        Assert.AreEqual(false, s.Menu.Items[2].IsChecked);
        Assert.IsFalse(s.Menu.IsOpen);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEntryThatIsNotASwitchHasNoCheckState()
    {
        Setup s = await OpenAsync();

        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.O, 'o'));

        Assert.IsNull(s.Menu.Items[0].IsChecked);
    }

    // ---- Submenus ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASubmenuEntryIsDrawnWithAnArrow()
    {
        Setup s = await OpenAsync();
        ScreenBuffer buffer = new(60, 20);

        s.Menu.Draw(buffer);

        Assert.IsTrue(buffer.Line(8).Contains('▶'), "Edit has a submenu");
        Assert.IsFalse(buffer.Line(7).Contains('▶'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RightOpensTheSubmenuBesideTheEntry()
    {
        Setup s = await OpenAsync();
        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));

        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));

        ContextMenu? child = s.Menu.OpenSubmenu;
        Assert.IsNotNull(child);
        Assert.IsTrue(child.IsOpen);
        Assert.IsTrue(s.Root.Controls.Contains(child));
        Assert.AreEqual(s.Menu.Position.X + s.Menu.ViewSize.Width, child.Position.X, "to the right of the parent");
        Assert.AreEqual(s.Menu.Position.Y + 1, child.Position.Y, "its first row level with the entry");
        Assert.AreEqual(0, child.SelectedIndex);
        Assert.AreSame(s.Menu, child.ParentMenu);
        Assert.IsTrue(s.Menu.IsOpen, "the parent stays open");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnASubmenuEntryOpensItRatherThanClosingTheMenu()
    {
        Setup s = await OpenAsync();
        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));

        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.IsNotNull(s.Menu.OpenSubmenu);
        Assert.IsTrue(s.Menu.IsOpen);
        Assert.AreEqual(0, s.Ran.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task LeftClosesTheSubmenuAndLeavesTheParent()
    {
        Setup s = await OpenAsync();
        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.E, 'e'));
        ContextMenu child = s.Menu.OpenSubmenu!;

        await child.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));

        Assert.IsFalse(child.IsOpen);
        Assert.IsFalse(s.Root.Controls.Contains(child));
        Assert.IsTrue(s.Menu.IsOpen);
        Assert.IsNull(s.Menu.OpenSubmenu);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscapeInASubmenuClosesOnlyTheSubmenu()
    {
        Setup s = await OpenAsync();
        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.E, 'e'));
        ContextMenu child = s.Menu.OpenSubmenu!;

        await child.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(child.IsOpen);
        Assert.IsTrue(s.Menu.IsOpen);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task LeftInATopMenuDoesNotCloseIt()
    {
        Setup s = await OpenAsync();

        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));

        Assert.IsTrue(s.Menu.IsOpen);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ChoosingAnEntryOfASubmenuClosesTheWholeChainThenRunsIt()
    {
        Setup s = await OpenAsync();
        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.E, 'e'));
        ContextMenu child = s.Menu.OpenSubmenu!;
        bool menuOpenWhenRun = true;
        child.Items[1].Execute = () =>
        {
            menuOpenWhenRun = s.Menu.IsOpen || child.IsOpen;
            s.Ran.Add("paste");
            return Task.CompletedTask;
        };

        await child.HandleKeyEventAsync(Key(ConsoleKey.P, 'p'));

        CollectionAssert.AreEqual(new[] { "paste" }, s.Ran);
        Assert.IsFalse(menuOpenWhenRun);
        Assert.IsFalse(child.IsOpen);
        Assert.IsFalse(s.Menu.IsOpen);
        Assert.IsFalse(s.Root.Controls.Contains(s.Menu));
        Assert.IsFalse(s.Root.Controls.Contains(child));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClosingTheParentClosesTheSubmenuToo()
    {
        Setup s = await OpenAsync();
        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.E, 'e'));
        ContextMenu child = s.Menu.OpenSubmenu!;

        s.Menu.Close();

        Assert.IsFalse(child.IsOpen);
        Assert.IsFalse(s.Root.Controls.Contains(child));
        Assert.IsFalse(s.Root.Controls.Contains(s.Menu));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASubmenuNearTheRightEdgeOpensOnTheLeft()
    {
        Setup s = await OpenAsync(x: 45);
        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.E, 'e'));

        ContextMenu child = s.Menu.OpenSubmenu!;

        Assert.IsTrue(child.Position.X + child.ViewSize.Width <= s.Menu.Position.X, "left of the parent");
        Assert.IsTrue(child.Position.X >= 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickingASubmenuEntryOpensIt()
    {
        Setup s = await OpenAsync();

        await s.Menu.HandleMouseEventAsync(Click(12, 6 + 1 + 1));

        Assert.IsNotNull(s.Menu.OpenSubmenu);
        Assert.IsTrue(s.Menu.IsOpen);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickingAnEntryOfTheSubmenuRunsIt()
    {
        Setup s = await OpenAsync();
        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.E, 'e'));
        ContextMenu child = s.Menu.OpenSubmenu!;

        await child.HandleMouseEventAsync(Click(child.Position.X + 2, child.Position.Y + 1));

        CollectionAssert.AreEqual(new[] { "copy" }, s.Ran);
        Assert.IsFalse(s.Menu.IsOpen);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickingOutsideEveryMenuClosesTheChain()
    {
        Setup s = await OpenAsync();
        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.E, 'e'));
        ContextMenu child = s.Menu.OpenSubmenu!;

        bool handled = await child.HandleMouseEventAsync(Click(2, 18));

        Assert.IsTrue(handled);
        Assert.IsFalse(child.IsOpen);
        Assert.IsFalse(s.Menu.IsOpen);
        Assert.AreEqual(0, s.Ran.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AClickOnTheParentWhileTheSubmenuIsOpenGoesToTheParent()
    {
        Setup s = await OpenAsync();
        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.E, 'e'));
        ContextMenu child = s.Menu.OpenSubmenu!;

        await child.HandleMouseEventAsync(Click(12, 6 + 1 + 0));

        CollectionAssert.AreEqual(new[] { "open" }, s.Ran);
        Assert.IsFalse(child.IsOpen);
        Assert.IsFalse(s.Menu.IsOpen);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HoveringAnotherEntryClosesTheSubmenu()
    {
        Setup s = await OpenAsync();
        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.E, 'e'));
        ContextMenu child = s.Menu.OpenSubmenu!;

        await child.HandleMouseEventAsync(new MouseEvent { Position = new Position(12, 6 + 1 + 0), IsHover = true });

        Assert.IsFalse(child.IsOpen);
        Assert.IsTrue(s.Menu.IsOpen);
        Assert.AreEqual(0, s.Menu.SelectedIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADisabledSubmenuEntryDoesNotOpen()
    {
        Setup s = await OpenAsync();
        s.Menu.Items[1].IsEnabled = false;
        await s.Menu.OpenAtAsync(s.Owner, new Position(10, 6));

        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.E, 'e'));
        await s.Menu.OpenSubmenuAsync(1);

        Assert.IsNull(s.Menu.OpenSubmenu);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpeningAnotherSubmenuReplacesTheFirst()
    {
        Setup s = await OpenAsync();
        ContextMenuItem view = new("&View");
        view.Items.Add(new ContextMenuItem("Zoom"));
        s.Menu.Items.Add(view);
        await s.Menu.OpenAtAsync(s.Owner, new Position(10, 6));
        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.E, 'e'));
        ContextMenu child = s.Menu.OpenSubmenu!;
        Assert.AreEqual("Copy", child.Items[0].Text);

        await child.HandleMouseEventAsync(new MouseEvent { Position = new Position(12, 6 + 1 + 4), IsHover = true });
        await s.Menu.OpenSubmenuAsync(4);

        Assert.AreEqual("Zoom", s.Menu.OpenSubmenu!.Items[0].Text);
        Assert.AreEqual(1, s.Menu.OpenSubmenu.Items.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SubmenusCanNest()
    {
        Setup s = await OpenAsync();
        ContextMenuItem more = new("&More");
        more.Items.Add(new ContextMenuItem("&Deep") { Execute = () => { s.Ran.Add("deep"); return Task.CompletedTask; } });
        s.Menu.Items[1].Items.Add(more);
        await s.Menu.HandleKeyEventAsync(Key(ConsoleKey.E, 'e'));
        ContextMenu first = s.Menu.OpenSubmenu!;
        await first.HandleKeyEventAsync(Key(ConsoleKey.M, 'm'));
        ContextMenu second = first.OpenSubmenu!;

        await second.HandleKeyEventAsync(Key(ConsoleKey.D, 'd'));

        CollectionAssert.AreEqual(new[] { "deep" }, s.Ran);
        Assert.IsFalse(s.Menu.IsOpen);
        Assert.IsFalse(first.IsOpen);
        Assert.IsFalse(second.IsOpen);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheMenuIsWideEnoughForTheCheckColumnAndTheArrow()
    {
        Setup s = await OpenAsync();
        ScreenBuffer buffer = new(60, 20);

        s.Menu.Draw(buffer);

        // Longest title "Delete" (6), check column (2), arrow (1): one space on each side plus the gap and the border.
        Assert.IsTrue(s.Menu.ViewSize.Width >= 1 + 2 + 6 + 2 + 1 + 1 + 2);
        Assert.IsTrue(buffer.Line(8).Contains('▶'), "the arrow fits inside the frame");
    }
}
