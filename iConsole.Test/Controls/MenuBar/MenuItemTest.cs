using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.PopUpWindows;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;
using MenuBarControl = iConsole.Controls.MenuBar;

namespace iConsole.Test.Controls.MenuBar;

[TestClass]
public class MenuItemTest
{
    private static Mock<IThemeService> CreateThemeServiceMock()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mockThemeService;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuItemCanBeCreated()
    {
        var mockThemeService = CreateThemeServiceMock();
        var menuItems = new List<MenuItem>();
        var menuItem = new MenuItem("File", null, menuItems, mockThemeService.Object);

        Assert.IsNotNull(menuItem);
        Assert.IsInstanceOfType(menuItem, typeof(PopUpWindow));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuItemHasText()
    {
        var mockThemeService = CreateThemeServiceMock();
        var menuItems = new List<MenuItem>();
        var menuItem = new MenuItem("Edit", null, menuItems, mockThemeService.Object);

        Assert.AreEqual("Edit", menuItem.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuItemHasMenuItems()
    {
        var mockThemeService = CreateThemeServiceMock();
        var subItems = new List<MenuItem>
        {
            new MenuItem("Open", null, new List<MenuItem>(), mockThemeService.Object),
            new MenuItem("Save", null, new List<MenuItem>(), mockThemeService.Object)
        };
        var menuItem = new MenuItem("File", null, subItems, mockThemeService.Object);

        var menuItemControls = menuItem.Controls.OfType<MenuItem>().Count();
        Assert.AreEqual(2, menuItemControls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuItemIsNotVisibleByDefault()
    {
        var mockThemeService = CreateThemeServiceMock();
        var menuItems = new List<MenuItem>();
        var menuItem = new MenuItem("View", null, menuItems, mockThemeService.Object);

        Assert.IsFalse(menuItem.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuItemIsNotOpenedByDefault()
    {
        var mockThemeService = CreateThemeServiceMock();
        var menuItems = new List<MenuItem>();
        var menuItem = new MenuItem("Tools", null, menuItems, mockThemeService.Object);

        Assert.IsFalse(menuItem.IsOpened);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuItemIsNotSelectedByDefault()
    {
        var mockThemeService = CreateThemeServiceMock();
        var menuItems = new List<MenuItem>();
        var menuItem = new MenuItem("Help", null, menuItems, mockThemeService.Object);

        Assert.IsFalse(menuItem.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuItemWithCommandCanBeCreated()
    {
        var mockThemeService = CreateThemeServiceMock();
        var mockCommand = new Mock<ICommand>();
        var menuItems = new List<MenuItem>();
        var menuItem = new MenuItem("Exit", mockCommand.Object, menuItems, mockThemeService.Object);

        Assert.IsNotNull(menuItem);
        Assert.AreEqual("Exit", menuItem.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuItemCanBeOpened()
    {
        var mockThemeService = CreateThemeServiceMock();
        var menuItems = new List<MenuItem>();
        var menuItem = new MenuItem("File", null, menuItems, mockThemeService.Object);

        menuItem.OpenMenu();

        Assert.IsTrue(menuItem.IsOpened);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuItemCanBeClosed()
    {
        var mockThemeService = CreateThemeServiceMock();
        var menuItems = new List<MenuItem>();
        var menuItem = new MenuItem("File", null, menuItems, mockThemeService.Object);

        menuItem.OpenMenu();
        Assert.IsTrue(menuItem.IsOpened);

        menuItem.CloseMenu();
        Assert.IsFalse(menuItem.IsOpened);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuItemTextCanBeSet()
    {
        var mockThemeService = CreateThemeServiceMock();
        var menuItems = new List<MenuItem>();
        var menuItem = new MenuItem("Original", null, menuItems, mockThemeService.Object);

        menuItem.Text = "Modified";

        Assert.AreEqual("Modified", menuItem.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuItemWithEmptyMenuItems()
    {
        var mockThemeService = CreateThemeServiceMock();
        var emptyList = new List<MenuItem>();
        var menuItem = new MenuItem("Empty", null, emptyList, mockThemeService.Object);

        var menuItemControls = menuItem.Controls.OfType<MenuItem>().Count();
        Assert.AreEqual(0, menuItemControls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuItemWithMultipleNestedItems()
    {
        var mockThemeService = CreateThemeServiceMock();
        var subItemsA1 = new List<MenuItem>
        {
            new MenuItem("SubA1_1", null, new List<MenuItem>(), mockThemeService.Object)
        };
        var subItemsA = new List<MenuItem>
        {
            new MenuItem("A1", null, subItemsA1, mockThemeService.Object),
            new MenuItem("A2", null, new List<MenuItem>(), mockThemeService.Object)
        };
        var rootItems = new List<MenuItem>
        {
            new MenuItem("A", null, subItemsA, mockThemeService.Object)
        };
        var root = new MenuItem("Root", null, rootItems, mockThemeService.Object);

        var firstLevelItems = root.Controls.OfType<MenuItem>().ToList();
        Assert.AreEqual(1, firstLevelItems.Count);

        var secondLevelItems = firstLevelItems[0].Controls.OfType<MenuItem>().ToList();
        Assert.AreEqual(2, secondLevelItems.Count);

        var thirdLevelItems = secondLevelItems[0].Controls.OfType<MenuItem>().ToList();
        Assert.AreEqual(1, thirdLevelItems.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuItemHasDefaultVisibility()
    {
        var mockThemeService = CreateThemeServiceMock();
        var menuItems = new List<MenuItem>();
        var menuItem = new MenuItem("Item", null, menuItems, mockThemeService.Object);

        Assert.IsFalse(menuItem.IsVisible);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuItemHasDefaultEnabled()
    {
        var mockThemeService = CreateThemeServiceMock();
        var menuItems = new List<MenuItem>();
        var menuItem = new MenuItem("Item", null, menuItems, mockThemeService.Object);

        Assert.IsTrue(menuItem.IsEnabled);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuItemHasTitle()
    {
        var mockThemeService = CreateThemeServiceMock();
        var menuItems = new List<MenuItem>();
        var menuItem = new MenuItem("File", null, menuItems, mockThemeService.Object);

        Assert.IsNotNull(menuItem.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextWithHotKeyIncludesCommandKey()
    {
        var mockThemeService = CreateThemeServiceMock();
        var mockCommand = new Mock<ICommand>();
        mockCommand.SetupGet(c => c.Key).Returns(new Key(ConsoleKey.F1, false, false, false));
        var menuItem = new MenuItem("Open", mockCommand.Object, [], mockThemeService.Object)
        {
            ViewSize = new Size(12, 1)
        };

        var textWithHotKey = menuItem.TextWithHotKey;

        Assert.IsTrue(textWithHotKey.StartsWith("Open"));
        Assert.IsTrue(textWithHotKey.EndsWith(" F1"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OpenMenuOpensParentAndSelectsFirstChild()
    {
        var mockThemeService = CreateThemeServiceMock();
        var childA = new MenuItem("ChildA", null, [], mockThemeService.Object);
        var childB = new MenuItem("ChildB", null, [], mockThemeService.Object);
        var parent = new MenuItem("Parent", null, [childA, childB], mockThemeService.Object);

        parent.OpenMenu();

        Assert.IsTrue(parent.IsOpened);
        Assert.IsTrue(childA.IsSelected);
        Assert.IsFalse(childB.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OpenSubMenuOpensAncestor()
    {
        var mockThemeService = CreateThemeServiceMock();
        var leaf = new MenuItem("Leaf", null, [], mockThemeService.Object);
        var child = new MenuItem("Child", null, [leaf], mockThemeService.Object);
        var root = new MenuItem("Root", null, [child], mockThemeService.Object);

        child.OpenMenu();

        Assert.IsTrue(root.IsOpened);
        Assert.IsTrue(child.IsOpened);
        Assert.IsTrue(leaf.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CloseMenuClosesDescendantsAndClearsSelection()
    {
        var mockThemeService = CreateThemeServiceMock();
        var leaf = new MenuItem("Leaf", null, [], mockThemeService.Object);
        var child = new MenuItem("Child", null, [leaf], mockThemeService.Object);
        var root = new MenuItem("Root", null, [child], mockThemeService.Object);

        root.OpenMenu();
        child.OpenMenu();

        root.CloseMenu();

        Assert.IsFalse(root.IsOpened);
        Assert.IsFalse(child.IsOpened);
        Assert.IsFalse(child.IsVisible);
        Assert.IsFalse(child.IsSelected);
        Assert.IsFalse(leaf.IsVisible);
        Assert.IsFalse(leaf.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetSelectedClearsSiblingSelection()
    {
        var mockThemeService = CreateThemeServiceMock();
        var first = new MenuItem("First", null, [], mockThemeService.Object);
        var second = new MenuItem("Second", null, [], mockThemeService.Object);
        _ = new MenuItem("Parent", null, [first, second], mockThemeService.Object);

        first.SetSelected();
        second.SetSelected();

        Assert.IsFalse(first.IsSelected);
        Assert.IsTrue(second.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TriggerHotKeyAsyncTogglesMenuState()
    {
        var mockThemeService = CreateThemeServiceMock();
        var child = new MenuItem("Child", null, [], mockThemeService.Object);
        var menuItem = new MenuItem("Parent", null, [child], mockThemeService.Object);

        await menuItem.TriggerHotKeyAsync();
        Assert.IsTrue(menuItem.IsOpened);

        await menuItem.TriggerHotKeyAsync();
        Assert.IsFalse(menuItem.IsOpened);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleKeyEventAsyncEscapeClosesMenu()
    {
        var mockThemeService = CreateThemeServiceMock();
        var child = new MenuItem("Child", null, [], mockThemeService.Object);
        var menuItem = new MenuItem("Parent", null, [child], mockThemeService.Object);
        menuItem.OpenMenu();

        var isHandled = await menuItem.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Escape, false, false, false));

        Assert.IsTrue(isHandled);
        Assert.IsFalse(menuItem.IsOpened);
        Assert.IsFalse(child.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_Closed_RendersSubItemText()
    {
        var mockThemeService = CreateThemeServiceMock();
        var buffer = new MockBuffer(new Size(30, 1));
        var child = new MenuItem("File", null, [], mockThemeService.Object);
        var menuItem = new MenuItem("Root", null, [child], mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(30, 1)
        };

        menuItem.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("File"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_Closed_MultipleSubItems_AllTextsRendered()
    {
        var mockThemeService = CreateThemeServiceMock();
        var buffer = new MockBuffer(new Size(40, 1));
        var childA = new MenuItem("File", null, [], mockThemeService.Object);
        var childB = new MenuItem("Edit", null, [], mockThemeService.Object);
        var menuItem = new MenuItem("Root", null, [childA, childB], mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(40, 1)
        };

        menuItem.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("File"));
        Assert.IsTrue(content.Contains("Edit"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_Opened_RendersBorderTopRow()
    {
        var mockThemeService = CreateThemeServiceMock();
        var child = new MenuItem("Open", null, [], mockThemeService.Object);
        var menuItem = new MenuItem("File", null, [child], mockThemeService.Object);
        menuItem.OpenMenu();

        var buffer = new MockBuffer(new Size(20, 4));

        menuItem.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(lines[0].Contains("╔") || lines[0].Contains("File"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_Opened_RendersSubItemInList()
    {
        var mockThemeService = CreateThemeServiceMock();
        var child = new MenuItem("Open", null, [], mockThemeService.Object);
        var menuItem = new MenuItem("File", null, [child], mockThemeService.Object);
        menuItem.OpenMenu();

        var buffer = new MockBuffer(new Size(20, 4));
        menuItem.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Open"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_Opened_SelectedItem_MarkedWithAngleBrackets()
    {
        var mockThemeService = CreateThemeServiceMock();
        var child = new MenuItem("Save", null, [], mockThemeService.Object);
        var menuItem = new MenuItem("File", null, [child], mockThemeService.Object);
        menuItem.OpenMenu(); // selects first child

        var buffer = new MockBuffer(new Size(20, 4));
        menuItem.Draw(buffer);

        // Selected items are wrapped in <...>
        Assert.IsTrue(buffer.ContentAsString.Contains("<"));
        Assert.IsTrue(buffer.ContentAsString.Contains(">"));
    }

    // ── Keyboard navigation while a menu is open ────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task DownArrow_MovesSelectionToNextSibling()
    {
        var mockThemeService = CreateThemeServiceMock();
        var childA = new MenuItem("ChildA", null, [], mockThemeService.Object);
        var childB = new MenuItem("ChildB", null, [], mockThemeService.Object);
        var parent = new MenuItem("Parent", null, [childA, childB], mockThemeService.Object);
        parent.OpenMenu(); // selects childA

        await parent.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.DownArrow, false, false, false));

        Assert.IsFalse(childA.IsSelected);
        Assert.IsTrue(childB.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DownArrow_WrapsToFirstSibling()
    {
        var mockThemeService = CreateThemeServiceMock();
        var childA = new MenuItem("ChildA", null, [], mockThemeService.Object);
        var childB = new MenuItem("ChildB", null, [], mockThemeService.Object);
        var parent = new MenuItem("Parent", null, [childA, childB], mockThemeService.Object);
        parent.OpenMenu(); // selects childA

        await parent.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.DownArrow, false, false, false));
        await parent.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.DownArrow, false, false, false));

        Assert.IsTrue(childA.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task UpArrow_WrapsToLastSibling()
    {
        var mockThemeService = CreateThemeServiceMock();
        var childA = new MenuItem("ChildA", null, [], mockThemeService.Object);
        var childB = new MenuItem("ChildB", null, [], mockThemeService.Object);
        var parent = new MenuItem("Parent", null, [childA, childB], mockThemeService.Object);
        parent.OpenMenu(); // selects childA

        await parent.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.UpArrow, false, false, false));

        Assert.IsFalse(childA.IsSelected);
        Assert.IsTrue(childB.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DownArrow_SkipsSeparators()
    {
        var mockThemeService = CreateThemeServiceMock();
        var childA = new MenuItem("ChildA", null, [], mockThemeService.Object);
        var separator = new MenuItem("-", null, [], mockThemeService.Object);
        var childB = new MenuItem("ChildB", null, [], mockThemeService.Object);
        var parent = new MenuItem("Parent", null, [childA, separator, childB], mockThemeService.Object);
        parent.OpenMenu(); // selects childA

        await parent.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.DownArrow, false, false, false));

        Assert.IsFalse(separator.IsSelected);
        Assert.IsTrue(childB.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_OnBranchItem_DescendsIntoSubmenu()
    {
        var mockThemeService = CreateThemeServiceMock();
        var leaf = new MenuItem("Leaf", null, [], mockThemeService.Object);
        var branch = new MenuItem("Branch", null, [leaf], mockThemeService.Object);
        var parent = new MenuItem("Parent", null, [branch], mockThemeService.Object);
        parent.OpenMenu(); // selects branch

        await parent.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Enter, false, false, false));

        Assert.IsTrue(branch.IsOpened);
        Assert.IsTrue(leaf.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_OnLeafItem_RunsCommandAndClosesWholeTree()
    {
        var mockThemeService = CreateThemeServiceMock();
        var mockCommand = new Mock<ICommand>();
        mockCommand.SetupGet(c => c.Key).Returns(new Key(ConsoleKey.Q, false, false, true));
        mockCommand.Setup(c => c.CanExecute()).Returns(true);
        mockCommand.Setup(c => c.RunAsync(null)).ReturnsAsync(true);
        var leaf = new MenuItem("Quit", mockCommand.Object, [], mockThemeService.Object);
        var fileMenu = new MenuItem("File", null, [leaf], mockThemeService.Object) { HotKeyChar = 'F' };
        var editMenu = new MenuItem("Edit", null, [], mockThemeService.Object) { HotKeyChar = 'E' };
        var menuBar = new MenuBarControl([fileMenu, editMenu], mockThemeService.Object);
        fileMenu.OpenMenu(); // selects leaf; MenuBar.IsModal becomes true

        await menuBar.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Enter, false, false, false));

        mockCommand.Verify(c => c.RunAsync(null), Times.Once);
        Assert.IsFalse(fileMenu.IsOpened);
        Assert.IsFalse(menuBar.IsModal);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RightArrow_AtTopLevel_SwitchesToNextMenu()
    {
        var mockThemeService = CreateThemeServiceMock();
        var fileMenu = new MenuItem("File", null, [], mockThemeService.Object) { HotKeyChar = 'F' };
        var editMenu = new MenuItem("Edit", null, [], mockThemeService.Object) { HotKeyChar = 'E' };
        var menuBar = new MenuBarControl([fileMenu, editMenu], mockThemeService.Object);
        fileMenu.OpenMenu();

        await menuBar.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.RightArrow, false, false, false));

        Assert.IsFalse(fileMenu.IsOpened);
        Assert.IsTrue(editMenu.IsOpened);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task LeftArrow_AtTopLevel_WrapsToLastMenu()
    {
        var mockThemeService = CreateThemeServiceMock();
        var fileMenu = new MenuItem("File", null, [], mockThemeService.Object) { HotKeyChar = 'F' };
        var editMenu = new MenuItem("Edit", null, [], mockThemeService.Object) { HotKeyChar = 'E' };
        var menuBar = new MenuBarControl([fileMenu, editMenu], mockThemeService.Object);
        fileMenu.OpenMenu();

        await menuBar.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.LeftArrow, false, false, false));

        Assert.IsFalse(fileMenu.IsOpened);
        Assert.IsTrue(editMenu.IsOpened);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task LeftArrow_Nested_AscendsKeepingParentSelectionAndTopLevelOpen()
    {
        var mockThemeService = CreateThemeServiceMock();
        var leaf = new MenuItem("Leaf", null, [], mockThemeService.Object);
        var branch = new MenuItem("Branch", null, [leaf], mockThemeService.Object);
        var fileMenu = new MenuItem("File", null, [branch], mockThemeService.Object) { HotKeyChar = 'F' };
        var menuBar = new MenuBarControl([fileMenu], mockThemeService.Object);
        fileMenu.OpenMenu();
        branch.OpenMenu(); // descend

        await menuBar.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.LeftArrow, false, false, false));

        Assert.IsFalse(branch.IsOpened);
        Assert.IsTrue(fileMenu.IsOpened);
        Assert.IsTrue(branch.IsSelected);
    }

    // ── MenuBar.IsModal drives whether the desktop routes keys into the menu ───

    [Timeout(60_000)]
    [TestMethod]
    public void MenuBar_IsModal_BecomesTrueWhenATopLevelMenuOpens()
    {
        var mockThemeService = CreateThemeServiceMock();
        var fileMenu = new MenuItem("File", null, [], mockThemeService.Object) { HotKeyChar = 'F' };
        var menuBar = new MenuBarControl([fileMenu], mockThemeService.Object);

        fileMenu.OpenMenu();

        Assert.IsTrue(menuBar.IsModal);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuBar_IsModal_BecomesFalseAfterCloseMenu()
    {
        var mockThemeService = CreateThemeServiceMock();
        var fileMenu = new MenuItem("File", null, [], mockThemeService.Object) { HotKeyChar = 'F' };
        var menuBar = new MenuBarControl([fileMenu], mockThemeService.Object);
        fileMenu.OpenMenu();

        fileMenu.CloseMenu();

        Assert.IsFalse(menuBar.IsModal);
    }

    // ── Help discoverability ────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void GetKeyHints_IncludesCtrlLetterForEachTopLevelMenuAndNavigationKeys()
    {
        var mockThemeService = CreateThemeServiceMock();
        var fileMenu = new MenuItem("File", null, [], mockThemeService.Object) { HotKeyChar = 'F' };
        var editMenu = new MenuItem("Edit", null, [], mockThemeService.Object) { HotKeyChar = 'E' };
        var menuBar = new MenuBarControl([fileMenu, editMenu], mockThemeService.Object);

        var hints = menuBar.GetKeyHints().ToList();

        Assert.IsTrue(hints.Any(h => h.Key == "Ctrl+F" && h.Description.Contains("File")));
        Assert.IsTrue(hints.Any(h => h.Key == "Ctrl+E" && h.Description.Contains("Edit")));
        Assert.IsTrue(hints.Any(h => h.Key == "Esc"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_MenuBar_RendersAllTopLevelItems()
    {
        var mockThemeService = CreateThemeServiceMock();
        var fileMenu = new MenuItem("File", null, [], mockThemeService.Object) { HotKeyChar = 'F' };
        var editMenu = new MenuItem("Edit", null, [], mockThemeService.Object) { HotKeyChar = 'E' };
        var menuBar = new MenuBarControl([fileMenu, editMenu], mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };
        var buffer = new MockBuffer(new Size(20, 1));

        menuBar.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("File"));
        Assert.IsTrue(content.Contains("Edit"));
    }

    /// <summary>
    /// A shortcut written into the label ("Create File\tShift+F4") is the one shown, once, right-aligned.
    /// The column used to append the command's bare console key as well — "Create File	Shift+F4 F4",
    /// a raw tab and a second, wrong shortcut (F4 is the editor).
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void AShortcutInTheLabelIsShownOnceAndNeverTheBareConsoleKey()
    {
        var command = new Mock<ICommand>();
        command.SetupGet(c => c.Key).Returns(new Key(ConsoleKey.F4, true, false, false));
        var menuItem = new MenuItem("Create File\tShift+F4", command.Object, [], CreateThemeServiceMock().Object);

        Assert.AreEqual("Create File", menuItem.Label);
        Assert.AreEqual("Shift+F4", menuItem.HotKeyText);
        Assert.AreEqual("Create File  Shift+F4", menuItem.TextWithHotKey);
        Assert.IsFalse(menuItem.TextWithHotKey.Contains('\t'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnOpenDropDownAlignsItsShortcutsInOneColumn()
    {
        var theme = new TestThemeService();
        var view = new MenuItem("View\tF3", null, [], theme);
        var create = new MenuItem("Create File\tShift+F4", null, [], theme);
        var file = new MenuItem("File", null, [view, create], theme) { HotKeyChar = 'F' };
        var bar = new MenuBarControl([file], theme) { ViewSize = new Size(80, 1) };
        var root = new Control(theme) { ViewSize = new Size(80, 25) };
        root.AddControl(bar);

        file.OpenMenu();
        var screen = new ScreenBuffer(80, 25);
        bar.Draw(screen);

        var viewRow = screen.Lines.Single(l => l.Contains("View"));
        var createRow = screen.Lines.Single(l => l.Contains("Create File"));
        Assert.AreEqual(viewRow.IndexOf("F3", StringComparison.Ordinal) + 2, createRow.IndexOf("Shift+F4", StringComparison.Ordinal) + 8,
            "shortcuts are right-aligned in one column");
        Assert.IsFalse(createRow.Contains("F4 F4"), createRow);
    }

    /// <summary>Every drop-down used to open at column 1, so the Tools menu appeared under "File".</summary>
    [Timeout(60_000)]
    [TestMethod]
    public void ADropDownOpensUnderItsOwnName()
    {
        var theme = new TestThemeService();
        var file = new MenuItem("File", null, [new MenuItem("Quit\tF10", null, [], theme)], theme) { HotKeyChar = 'F' };
        var tools = new MenuItem("Tools", null, [new MenuItem("Terminal\tF2", null, [], theme)], theme) { HotKeyChar = 'T' };
        var bar = new MenuBarControl([file, tools], theme) { ViewSize = new Size(80, 1) };
        var root = new Control(theme) { ViewSize = new Size(80, 25) };
        root.AddControl(bar);

        tools.OpenMenu();

        Assert.AreEqual(5, tools.Position.X, "\"File \" is five columns, so Tools starts at column 5");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ADropDownNearTheRightEdgeStaysOnScreen()
    {
        var theme = new TestThemeService();
        var help = new MenuItem("Help", null, [new MenuItem("About iTerminal\tCtrl+Shift+Alt+F1", null, [], theme)], theme) { HotKeyChar = 'H' };
        var bar = new MenuBarControl([new MenuItem(new string('x', 30), null, [], theme), help], theme) { ViewSize = new Size(40, 1) };
        var root = new Control(theme) { ViewSize = new Size(40, 25) };
        root.AddControl(bar);

        help.OpenMenu();

        Assert.IsTrue(help.Position.X + help.ViewSize.Width <= 40, $"{help.Position.X} + {help.ViewSize.Width}");
    }
    [Timeout(60_000)]
    [TestMethod]
    public async Task HomeAndEnd_SelectTheFirstAndLastEntry_SkippingSeparators()
    {
        var mockThemeService = CreateThemeServiceMock();
        var separatorFirst = new MenuItem("-", null, [], mockThemeService.Object);
        var childA = new MenuItem("ChildA", null, [], mockThemeService.Object);
        var childB = new MenuItem("ChildB", null, [], mockThemeService.Object);
        var childC = new MenuItem("ChildC", null, [], mockThemeService.Object);
        var separatorLast = new MenuItem("-", null, [], mockThemeService.Object);
        var parent = new MenuItem("Parent", null, [separatorFirst, childA, childB, childC, separatorLast], mockThemeService.Object);
        parent.OpenMenu();

        Assert.IsTrue(await parent.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, false)));
        Assert.IsTrue(childC.IsSelected);
        Assert.IsFalse(separatorLast.IsSelected);

        Assert.IsTrue(await parent.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Home, false, false, false)));
        Assert.IsTrue(childA.IsSelected);
        Assert.IsFalse(childC.IsSelected);
        Assert.IsFalse(separatorFirst.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HomeAndEnd_OnAnEmptyDropDown_AreHandledAndChangeNothing()
    {
        var mockThemeService = CreateThemeServiceMock();
        var parent = new MenuItem("Parent", null, [], mockThemeService.Object);
        parent.OpenMenu();

        Assert.IsTrue(await parent.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, false)));
        Assert.IsTrue(await parent.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Home, false, false, false)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Space_RunsTheSelectedEntryLikeEnter()
    {
        var mockThemeService = CreateThemeServiceMock();
        var mockCommand = new Mock<ICommand>();
        mockCommand.SetupGet(c => c.Key).Returns(new Key(ConsoleKey.Q, false, false, true));
        mockCommand.Setup(c => c.CanExecute()).Returns(true);
        mockCommand.Setup(c => c.RunAsync(null)).ReturnsAsync(true);
        var leaf = new MenuItem("Quit", mockCommand.Object, [], mockThemeService.Object);
        var fileMenu = new MenuItem("File", null, [leaf], mockThemeService.Object) { HotKeyChar = 'F' };
        var menuBar = new MenuBarControl([fileMenu], mockThemeService.Object);
        fileMenu.OpenMenu();

        Assert.IsTrue(await menuBar.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false)));

        mockCommand.Verify(c => c.RunAsync(null), Times.Once);
        Assert.IsFalse(fileMenu.IsOpened);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Space_OnABranchEntry_DescendsIntoTheSubmenu()
    {
        var mockThemeService = CreateThemeServiceMock();
        var leaf = new MenuItem("Leaf", null, [], mockThemeService.Object);
        var branch = new MenuItem("Branch", null, [leaf], mockThemeService.Object);
        var parent = new MenuItem("Parent", null, [branch], mockThemeService.Object);
        parent.OpenMenu();

        await parent.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));

        Assert.IsTrue(branch.IsOpened);
    }
}
