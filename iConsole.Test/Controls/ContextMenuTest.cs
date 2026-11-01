using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

[TestClass]
public class ContextMenuTest
{
    private readonly TestThemeService _theme = new();

    private static KeyEvent Key(ConsoleKey key, char c = ' ', bool shift = false) => new(c, key, shift, false, false);

    private (Control Root, Control Owner, ContextMenu Menu, List<string> Ran) Create()
    {
        Control root = new(_theme) { ViewSize = new Size(60, 20) };
        Control owner = new(_theme) { Position = new Position(5, 5), ViewSize = new Size(20, 5), CanFocus = true };
        root.AddControl(owner);
        List<string> ran = [];
        ContextMenu menu = new(new PopoverService(), _theme);
        menu.Items.Add(new ContextMenuItem("Open") { Shortcut = "F3", Execute = () => { ran.Add("open"); return Task.CompletedTask; } });
        menu.Items.Add(new ContextMenuItem("Copy") { Shortcut = "F5", Execute = () => { ran.Add("copy"); return Task.CompletedTask; } });
        menu.Items.Add(ContextMenuItem.Separator());
        menu.Items.Add(new ContextMenuItem("Delete") { IsEnabled = false, Execute = () => { ran.Add("delete"); return Task.CompletedTask; } });
        menu.Items.Add(new ContextMenuItem("Properties") { Execute = () => { ran.Add("properties"); return Task.CompletedTask; } });
        return (root, owner, menu, ran);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpensAtThePointAndHighlightsTheFirstEntry()
    {
        (Control root, Control owner, ContextMenu menu, _) = Create();

        await menu.OpenAtAsync(owner, new Position(10, 6));

        Assert.IsTrue(menu.IsOpen);
        Assert.IsTrue(root.Controls.Contains(menu));
        Assert.AreEqual(new Position(10, 6), menu.Position);
        Assert.AreEqual(0, menu.SelectedIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task IsSizedToItsEntriesAndKeptOnScreen()
    {
        (_, Control owner, ContextMenu menu, _) = Create();

        await menu.OpenAtAsync(owner, new Position(58, 19));

        // 5 entries plus the border; the pointer is in the corner, so it opens up and to the left.
        Assert.AreEqual(7, menu.ViewSize.Height);
        Assert.IsTrue(menu.Position.X + menu.ViewSize.Width <= 60);
        Assert.IsTrue(menu.Position.Y + menu.ViewSize.Height <= 20);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ArrowKeysSkipSeparatorsAndDisabledEntries()
    {
        (_, Control owner, ContextMenu menu, _) = Create();
        await menu.OpenAtAsync(owner, new Position(10, 6));

        await menu.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(1, menu.SelectedIndex);

        await menu.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(4, menu.SelectedIndex, "the separator and the disabled Delete are skipped");

        await menu.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(4, menu.SelectedIndex, "it stops at the end");

        await menu.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(1, menu.SelectedIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HomeAndEndJumpToTheFirstAndLastEntry()
    {
        (_, Control owner, ContextMenu menu, _) = Create();
        await menu.OpenAtAsync(owner, new Position(10, 6));

        await menu.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(4, menu.SelectedIndex);

        await menu.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, menu.SelectedIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterClosesTheMenuThenRunsTheEntry()
    {
        (Control root, Control owner, ContextMenu menu, List<string> ran) = Create();
        await menu.OpenAtAsync(owner, new Position(10, 6));
        await menu.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        bool wasOpenWhenRun = true;
        menu.Items[1].Execute = () =>
        {
            wasOpenWhenRun = menu.IsOpen;
            ran.Add("copy");
            return Task.CompletedTask;
        };

        await menu.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        CollectionAssert.AreEqual(new[] { "copy" }, ran);
        Assert.IsFalse(wasOpenWhenRun);
        Assert.IsFalse(menu.IsOpen);
        Assert.IsFalse(root.Controls.Contains(menu));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ALetterJumpsToTheNextEntryThatStartsWithIt()
    {
        (_, Control owner, ContextMenu menu, _) = Create();
        await menu.OpenAtAsync(owner, new Position(10, 6));

        await menu.HandleKeyEventAsync(Key(ConsoleKey.P, 'p'));

        Assert.AreEqual(4, menu.SelectedIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ALetterOfADisabledEntryDoesNotSelectIt()
    {
        (_, Control owner, ContextMenu menu, _) = Create();
        await menu.OpenAtAsync(owner, new Position(10, 6));

        await menu.HandleKeyEventAsync(Key(ConsoleKey.D, 'd'));

        Assert.AreEqual(0, menu.SelectedIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscapeClosesWithoutRunningAnything()
    {
        (_, Control owner, ContextMenu menu, List<string> ran) = Create();
        await menu.OpenAtAsync(owner, new Position(10, 6));

        await menu.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(menu.IsOpen);
        Assert.AreEqual(0, ran.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickingAnEntryRunsIt()
    {
        (_, Control owner, ContextMenu menu, List<string> ran) = Create();
        await menu.OpenAtAsync(owner, new Position(10, 6));

        // Entry 4 is on row 4 inside the border.
        await menu.HandleMouseEventAsync(new MouseEvent { Position = new Position(12, 6 + 1 + 4), LeftClicked = true });

        CollectionAssert.AreEqual(new[] { "properties" }, ran);
        Assert.IsFalse(menu.IsOpen);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickingADisabledEntryOrASeparatorKeepsTheMenuOpen()
    {
        (_, Control owner, ContextMenu menu, List<string> ran) = Create();
        await menu.OpenAtAsync(owner, new Position(10, 6));

        await menu.HandleMouseEventAsync(new MouseEvent { Position = new Position(12, 6 + 1 + 2), LeftClicked = true });
        await menu.HandleMouseEventAsync(new MouseEvent { Position = new Position(12, 6 + 1 + 3), LeftClicked = true });

        Assert.IsTrue(menu.IsOpen);
        Assert.AreEqual(0, ran.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HoveringAnEntryHighlightsIt()
    {
        (_, Control owner, ContextMenu menu, _) = Create();
        await menu.OpenAtAsync(owner, new Position(10, 6));

        await menu.HandleMouseEventAsync(new MouseEvent { Position = new Position(12, 6 + 1 + 1), IsHover = true });

        Assert.AreEqual(1, menu.SelectedIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickingOutsideClosesIt()
    {
        (_, Control owner, ContextMenu menu, List<string> ran) = Create();
        await menu.OpenAtAsync(owner, new Position(10, 6));

        await menu.HandleMouseEventAsync(new MouseEvent { Position = new Position(50, 15), LeftClicked = true });

        Assert.IsFalse(menu.IsOpen);
        Assert.AreEqual(0, ran.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DrawsTheEntriesWithTheirShortcuts()
    {
        (_, Control owner, ContextMenu menu, _) = Create();
        await menu.OpenAtAsync(owner, new Position(10, 6));
        ScreenBuffer buffer = new(60, 20);

        menu.Draw(buffer);

        string open = buffer.Line(7).Substring(10);
        string copy = buffer.Line(8).Substring(10);
        Assert.IsTrue(open.Contains("Open", StringComparison.Ordinal), open);
        Assert.IsTrue(open.Contains("F3", StringComparison.Ordinal), open);
        Assert.IsTrue(copy.Contains("Copy", StringComparison.Ordinal), copy);
        Assert.IsTrue(buffer.Line(9).Contains('─'), "the separator");
        Assert.IsTrue(buffer.CellOf("Delete").IsDim, "a disabled entry is faint");
        Assert.IsFalse(buffer.CellOf("Open").IsDim);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARightClickOnTheControlOpensItsMenuAtThePointer()
    {
        (_, Control owner, ContextMenu menu, _) = Create();
        owner.ContextMenu = menu;

        bool handled = await owner.HandleMouseEventAsync(new MouseEvent { Position = new Position(8, 7), RightClicked = true });

        Assert.IsTrue(handled);
        Assert.IsTrue(menu.IsOpen);
        Assert.AreEqual(new Position(8, 7), menu.Position);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARightClickOutsideTheControlDoesNotOpenItsMenu()
    {
        (_, Control owner, ContextMenu menu, _) = Create();
        owner.ContextMenu = menu;

        bool handled = await owner.HandleMouseEventAsync(new MouseEvent { Position = new Position(40, 15), RightClicked = true });

        Assert.IsFalse(handled);
        Assert.IsFalse(menu.IsOpen);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AChildsMenuWinsOverItsParents()
    {
        Control root = new(_theme) { ViewSize = new Size(60, 20) };
        Control parent = new(_theme) { Position = new Position(0, 0), ViewSize = new Size(40, 15) };
        Control child = new(_theme) { Position = new Position(2, 2), ViewSize = new Size(10, 3) };
        root.AddControl(parent);
        parent.AddControl(child);
        PopoverService service = new();
        ContextMenu parentMenu = new(service, _theme);
        parentMenu.Items.Add(new ContextMenuItem("Parent"));
        ContextMenu childMenu = new(service, _theme);
        childMenu.Items.Add(new ContextMenuItem("Child"));
        parent.ContextMenu = parentMenu;
        child.ContextMenu = childMenu;

        await parent.HandleMouseEventAsync(new MouseEvent { Position = new Position(3, 3), RightClicked = true });

        Assert.IsTrue(childMenu.IsOpen);
        Assert.IsFalse(parentMenu.IsOpen);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftF10OpensTheMenuOfTheFocusedControl()
    {
        (Control root, Control owner, ContextMenu menu, _) = Create();
        owner.ContextMenu = menu;
        owner.SetFocus();

        bool handled = await owner.HandleKeyEventAsync(Key(ConsoleKey.F10, shift: true));

        Assert.IsTrue(handled);
        Assert.IsTrue(menu.IsOpen);
        Assert.IsTrue(root.Controls.Contains(menu));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PlainF10DoesNotOpenTheMenu()
    {
        (_, Control owner, ContextMenu menu, _) = Create();
        owner.ContextMenu = menu;
        owner.SetFocus();

        await owner.HandleKeyEventAsync(Key(ConsoleKey.F10));

        Assert.IsFalse(menu.IsOpen);
    }
}
