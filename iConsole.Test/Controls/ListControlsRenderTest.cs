using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

/// <summary>
/// The rendered list-shaped controls — <see cref="ListBox{T}"/>, <see cref="Table{T}"/>,
/// <see cref="TreeView"/> and <see cref="PopupListView"/>. What they share, and what these tests
/// pin down: the cursor row is visible (and stronger while the control has focus), the view
/// scrolls to keep it on screen, and every row of the control is repainted on every draw — a list
/// that shrinks must not leave its old rows behind, since nothing else clears the screen.
/// </summary>
[TestClass]
public class ListControlsRenderTest
{
    private readonly TestThemeService _theme = new();
    private ColorScheme Scheme => _theme.CurrentColorScheme;

    private static KeyEvent Key(ConsoleKey key) => new('\0', key, false, false, false);

    #region ListBox
    private ListBox<string> CreateListBox(ConcurrentObservableCollection<string> items, int height = 4, Func<string, bool>? isEnabled = null)
    {
        var model = new ListBoxModel<string>(items);
        if (isEnabled is not null)
        {
            model.IsItemEnabled = (item, _) => isEnabled(item);
        }
        model.UpdateListItems();
        var listBox = new ListBox<string>(model, _theme)
        {
            Position = new Position(1, 1),
            ViewSize = new Size(10, height),
        };
        listBox.OnInitializeLayout();
        return listBox;
    }

    [TestMethod]
    public void ListBox_DrawsOneItemPerRow_AtItsPosition()
    {
        var screen = ScreenBuffer.Render(CreateListBox(["alpha", "beta", "gamma"]), 12, 6);

        screen.AssertLines(
            "",
            " alpha",
            " beta",
            " gamma");
    }

    [TestMethod]
    public void ListBox_CursorRow_IsHighlightedAcrossTheWholeWidth()
    {
        var listBox = CreateListBox(["alpha", "beta"]);
        listBox.ScrollableModel.CursorPosition.Y = 1;

        var screen = ScreenBuffer.Render(listBox, 12, 6);

        screen.AssertColors(1, 1, 10, Scheme.Foreground, Scheme.Background);
        screen.AssertColors(1, 2, 10, Scheme.SelectedForeground, Scheme.SelectedBackground);
    }

    [TestMethod]
    public void ListBox_Focused_UsesTheStrongerCursorHighlight()
    {
        var listBox = CreateListBox(["alpha", "beta"]);
        listBox.SetFocus();

        var screen = ScreenBuffer.Render(listBox, 12, 6);

        screen.AssertColors(1, 1, 10, Scheme.SelectedFocusedForeground, Scheme.SelectedFocusedBackground);
    }

    [TestMethod]
    public void ListBox_DisabledItems_UseTheDisabledColours()
    {
        var listBox = CreateListBox(["alpha", "beta", "gamma"], isEnabled: item => item != "beta");

        var screen = ScreenBuffer.Render(listBox, 12, 6);

        screen.AssertColors(1, 2, 10, Scheme.DisabledForeground, Scheme.DisabledBackground);
        screen.AssertColors(1, 3, 10, Scheme.Foreground, Scheme.Background);
    }

    [TestMethod]
    public void ListBox_ShrinkingList_ErasesTheRowsItNoLongerHas()
    {
        ConcurrentObservableCollection<string> items = ["alpha", "beta", "gamma", "delta"];
        var listBox = CreateListBox(items);
        var screen = new ScreenBuffer(12, 6);
        listBox.Draw(screen);

        items.ReplaceAll(["alpha"]);
        listBox.UpdateModel();
        listBox.Draw(screen);

        screen.AssertLines(
            "",
            " alpha");
        screen.AssertFillsBounds(listBox);
    }

    [TestMethod]
    public void ListBox_Empty_ShowsItsEmptyText()
    {
        var listBox = CreateListBox([]);
        listBox.EmptyText = "(empty)";

        var screen = ScreenBuffer.Render(listBox, 12, 6);

        screen.AssertLines(
            "",
            " (empty)");
        screen.AssertColors("(empty)", Scheme.DisabledForeground, Scheme.Background);
        screen.AssertFillsBounds(listBox);
    }

    [TestMethod]
    public void ListBox_EmptyText_IsNotShownOnceThereAreItems()
    {
        var listBox = CreateListBox(["alpha"]);
        listBox.EmptyText = "(empty)";

        Assert.IsFalse(ScreenBuffer.Render(listBox, 12, 6).Contains("(empty)"));
    }

    /// <summary>
    /// A message longer than the list is wide is wrapped onto the rows below, keeping its indent —
    /// cut at the edge, it lost its end, which is usually the part that says what to do.
    /// </summary>
    [TestMethod]
    public void ListBox_ALongEmptyText_IsWrappedNotCut()
    {
        var listBox = CreateListBox([]);
        listBox.EmptyText = " None here. Press r.";

        var screen = ScreenBuffer.Render(listBox, 12, 6);

        screen.AssertLines(
            "",
            "  None",
            "  here.",
            "  Press r.");
        screen.AssertFillsBounds(listBox);
    }

    [TestMethod]
    public async Task ListBox_MovingPastTheLastRow_ScrollsTheCursorIntoView()
    {
        var listBox = CreateListBox(["a0", "a1", "a2", "a3", "a4", "a5"], height: 3);
        listBox.SetFocus();

        for (var i = 0; i < 4; i++)
        {
            await listBox.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        }
        var screen = ScreenBuffer.Render(listBox, 12, 5);

        Assert.AreEqual(4, (int)listBox.ScrollableModel.CursorPosition.Y);
        CollectionAssert.Contains(screen.Lines, " a4");
        Assert.IsFalse(screen.Contains("a0"));
        screen.AssertColors("a4", Scheme.SelectedFocusedForeground, Scheme.SelectedFocusedBackground);
    }

    [TestMethod]
    public void ListBox_LongItem_IsClippedToTheControl()
    {
        var listBox = CreateListBox(["a very long list item"]);

        var screen = ScreenBuffer.Render(listBox, 20, 6);

        Assert.AreEqual(" a very lon", screen.Lines[1]);
        Assert.IsFalse(screen.IsWritten(11, 1));
    }
    #endregion

    #region Table
    private sealed record Row(string Name, string Size);

    private Table<Row> CreateTable(ConcurrentObservableCollection<Row> rows, int height = 4)
    {
        var table = new Table<Row>(rows, _theme) { ViewSize = new Size(16, height) };
        table.Columns.Add(new ColumnDefinition<Row>("Name", 8, r => r.Name));
        table.Columns.Add(new ColumnDefinition<Row>("Size", 7, r => r.Size));
        table.OnInitializeLayout();
        return table;
    }

    [TestMethod]
    public void Table_DrawsAHeaderAndOneRowPerItem_InColumns()
    {
        var screen = ScreenBuffer.Render(CreateTable([new("a.txt", "12 B"), new("photo.jpg", "3 MB")]), 16, 4);

        screen.AssertLines(
            "Name     Size",
            "a.txt    12 B",
            "photo.jp 3 MB");
    }

    [TestMethod]
    public void Table_Header_UsesTheSelectionColoursAcrossTheWidth()
    {
        var screen = ScreenBuffer.Render(CreateTable([new("a", "1")]), 16, 4);

        screen.AssertColors(0, 0, 16, Scheme.SelectedForeground, Scheme.SelectedBackground);
    }

    [TestMethod]
    public void Table_CursorRow_FocusedAndUnfocused()
    {
        var table = CreateTable([new("a", "1"), new("b", "2")]);
        var unfocused = ScreenBuffer.Render(table, 16, 4);
        unfocused.AssertColors(0, 1, 16, Scheme.SelectedForeground, Scheme.SelectedBackground);
        unfocused.AssertColors(0, 2, 16, Scheme.Foreground, Scheme.Background);

        table.SetFocus();
        var focused = ScreenBuffer.Render(table, 16, 4);
        focused.AssertColors(0, 1, 16, Scheme.SelectedFocusedForeground, Scheme.SelectedFocusedBackground);
    }

    [TestMethod]
    public void Table_LosingRows_ErasesThem()
    {
        ConcurrentObservableCollection<Row> rows = [new("a", "1"), new("b", "2"), new("c", "3")];
        var table = CreateTable(rows);
        var screen = new ScreenBuffer(16, 4);
        table.Draw(screen);

        rows.ReplaceAll([new Row("a", "1")]);
        table.Draw(screen);

        screen.AssertLines(
            "Name     Size",
            "a        1");
        screen.AssertFillsBounds(table);
    }

    [TestMethod]
    public void Table_Empty_ShowsItsEmptyTextUnderTheHeader()
    {
        var table = CreateTable([]);
        table.EmptyText = "No holdings";

        var screen = ScreenBuffer.Render(table, 16, 4);

        screen.AssertLines(
            "Name     Size",
            "No holdings");
        screen.AssertColors("No holdings", Scheme.DisabledForeground, Scheme.Background);
    }

    [TestMethod]
    public async Task Table_EnterOnAnEmptyTable_IsNotHandled()
    {
        var table = CreateTable([]);
        var activated = false;
        table.RowActivated += (_, _) => activated = true;

        var handled = await table.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.IsFalse(handled);
        Assert.IsFalse(activated);
    }

    [TestMethod]
    public async Task Table_EnterOnARow_ActivatesIt()
    {
        var table = CreateTable([new("a", "1")]);
        Row? activated = null;
        table.RowActivated += (_, row) => activated = row;
        table.SetFocus();

        Assert.IsTrue(await table.HandleKeyEventAsync(Key(ConsoleKey.Enter)));
        Assert.AreEqual("a", activated?.Name);
    }
    #endregion

    #region TreeView
    private static HierarchicalItemModel Node(HierarchicalItemModel? parent, string name, bool isOpened = false)
    {
        var node = new HierarchicalItemModel(parent, name) { IsOpened = isOpened };
        parent?.Children.Add(node);
        return node;
    }

    private TreeView CreateTree(HierarchicalItemModel root, int height = 6, int width = 16) =>
        new(root, _theme) { ViewSize = new Size(width, height) };

    [TestMethod]
    public void TreeView_IndentsByDepth_WithAGlyphForEveryParent()
    {
        var root = Node(null, "/", isOpened: true);
        var home = Node(root, "home", isOpened: true);
        Node(home, "tamas");
        Node(root, "etc");
        Node(Node(root, "var"), "log");

        var screen = ScreenBuffer.Render(CreateTree(root), 16, 6);

        screen.AssertLines(
            "▾ /",
            "  ▾ home",
            "      tamas",
            "    etc",
            "  ▸ var");
    }

    [TestMethod]
    public void TreeView_DeepNames_AreNotClipped()
    {
        var root = Node(null, "r", isOpened: true);
        var node = root;
        for (var depth = 1; depth <= 4; depth++)
        {
            node = Node(node, $"level{depth}", isOpened: true);
        }
        Node(node, "leaf");

        var screen = ScreenBuffer.Render(CreateTree(root, 8, 30), 30, 8);

        Assert.AreEqual("        ▾ level4", screen.Lines[4]);
        Assert.AreEqual("            leaf", screen.Lines[5]);
    }

    [TestMethod]
    public void TreeView_Collapsing_ErasesTheChildren()
    {
        var root = Node(null, "/", isOpened: true);
        var home = Node(root, "home", isOpened: true);
        Node(home, "a");
        Node(home, "b");
        var tree = CreateTree(root);
        var screen = new ScreenBuffer(16, 6);
        tree.Draw(screen);

        home.IsOpened = false;
        tree.Draw(screen);

        screen.AssertLines(
            "▾ /",
            "  ▸ home");
        screen.AssertFillsBounds(tree);
    }

    [TestMethod]
    public void TreeView_FocusedItem_IsHighlightedAcrossTheRow()
    {
        var root = Node(null, "/", isOpened: true);
        var etc = Node(root, "etc");
        etc.IsFocused = true;
        var tree = CreateTree(root);

        var unfocused = ScreenBuffer.Render(tree, 16, 6);
        unfocused.AssertColors(0, 1, 16, Scheme.SelectedForeground, Scheme.SelectedBackground);
        unfocused.AssertColors(0, 0, 16, Scheme.Foreground, Scheme.Background);

        tree.SetFocus();
        var focused = ScreenBuffer.Render(tree, 16, 6);
        focused.AssertColors(0, 1, 16, Scheme.SelectedFocusedForeground, Scheme.SelectedFocusedBackground);
    }

    [TestMethod]
    public void TreeView_FocusBelowTheFold_ScrollsItIntoView()
    {
        var root = Node(null, "/", isOpened: true);
        HierarchicalItemModel? last = null;
        for (var i = 0; i < 10; i++)
        {
            last = Node(root, $"dir{i}");
        }
        last!.IsFocused = true;
        var tree = CreateTree(root, height: 4);

        var screen = ScreenBuffer.Render(tree, 16, 4);

        screen.AssertLines(
            "    dir6",
            "    dir7",
            "    dir8",
            "    dir9");
        Assert.AreEqual(7, tree.ScrollOffset);
        screen.AssertColors(0, 3, 16, Scheme.SelectedForeground, Scheme.SelectedBackground);
    }

    [TestMethod]
    public async Task TreeView_MovingUpPastTheTop_ScrollsBack()
    {
        var root = Node(null, "/", isOpened: true);
        HierarchicalItemModel? last = null;
        for (var i = 0; i < 10; i++)
        {
            last = Node(root, $"dir{i}");
        }
        last!.IsFocused = true;
        var tree = CreateTree(root, height: 4);
        tree.SetFocus();
        ScreenBuffer.Render(tree, 16, 4);

        for (var i = 0; i < 6; i++)
        {
            await tree.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        }
        var screen = ScreenBuffer.Render(tree, 16, 4);

        Assert.AreEqual("    dir3", screen.Lines[0]);
        screen.AssertColors(0, 0, 16, Scheme.SelectedFocusedForeground, Scheme.SelectedFocusedBackground);
    }

    [TestMethod]
    public void TreeView_FormatRow()
    {
        var root = Node(null, "root", isOpened: true);
        var closed = Node(root, "closed");
        Node(closed, "child");
        var leaf = Node(root, "leaf");

        Assert.AreEqual("▾ root", TreeView.FormatRow(root, 0));
        Assert.AreEqual("  ▸ closed", TreeView.FormatRow(closed, 1));
        Assert.AreEqual("    leaf", TreeView.FormatRow(leaf, 1));
    }
    #endregion

    #region PopupListView
    private PopupListView CreatePopupList(IReadOnlyList<string> items, int highlight = 0, int height = 3)
    {
        var list = new PopupListView(_theme) { ViewSize = new Size(10, height) };
        list.SetItems(items, highlight);
        return list;
    }

    [TestMethod]
    public void PopupListView_IndentsRows_AndHighlightsOne()
    {
        var screen = ScreenBuffer.Render(CreatePopupList(["Name", "Size"], highlight: 1), 10, 3);

        screen.AssertLines(
            " Name",
            " Size");
        screen.AssertColors(0, 0, 10, Scheme.Foreground, Scheme.Background);
        screen.AssertColors(0, 1, 10, Scheme.SelectedFocusedForeground, Scheme.SelectedFocusedBackground);
        screen.AssertColors(0, 2, 10, Scheme.Foreground, Scheme.Background);
    }

    [TestMethod]
    public void PopupListView_HighlightBelowTheFold_IsScrolledIntoView()
    {
        var screen = ScreenBuffer.Render(CreatePopupList(["a", "b", "c", "d", "e"], highlight: 4), 10, 3);

        screen.AssertLines(
            " c",
            " d",
            " e");
    }

    [TestMethod]
    public async Task PopupListView_End_MovesTheHighlightToTheLastRow()
    {
        var list = CreatePopupList(["a", "b", "c", "d", "e"]);

        await list.HandleKeyEventAsync(Key(ConsoleKey.End));
        var screen = ScreenBuffer.Render(list, 10, 3);

        screen.AssertColors(" e", Scheme.SelectedFocusedForeground, Scheme.SelectedFocusedBackground);
    }

    [TestMethod]
    public void PopupListView_PaintsItsWholeArea()
    {
        var list = CreatePopupList(["a"]);

        ScreenBuffer.Render(list, 10, 3).AssertFillsBounds(list);
    }
    #endregion

    private static KeyEvent Char(char c, ConsoleKey key = ConsoleKey.NoName, bool shift = false) => new(c, key, shift, false, false);

    [TestMethod]
    public async Task TreeView_EnterAndSpace_ToggleTheFocusedNode()
    {
        var root = Node(null, "/", isOpened: true);
        var home = Node(root, "home");
        Node(home, "tamas");
        home.IsFocused = true;
        var tree = CreateTree(root);
        tree.SetFocus();

        Assert.IsTrue(await tree.HandleKeyEventAsync(Key(ConsoleKey.Enter)));
        Assert.IsTrue(home.IsOpened);
        Assert.IsTrue(home.IsFocused, "the focus stays on the node");

        Assert.IsTrue(await tree.HandleKeyEventAsync(Char(' ', ConsoleKey.Spacebar)));
        Assert.IsFalse(home.IsOpened);
    }

    [TestMethod]
    public async Task TreeView_PlusOpens_MinusCloses_EvenWithShiftHeld()
    {
        var root = Node(null, "/", isOpened: true);
        var home = Node(root, "home");
        Node(home, "tamas");
        home.IsFocused = true;
        var tree = CreateTree(root);
        tree.SetFocus();

        Assert.IsTrue(await tree.HandleKeyEventAsync(Char('+', ConsoleKey.OemPlus, shift: true)));
        Assert.IsTrue(home.IsOpened);
        await tree.HandleKeyEventAsync(Char('+', ConsoleKey.Add));
        Assert.IsTrue(home.IsOpened, "plus never closes");

        Assert.IsTrue(await tree.HandleKeyEventAsync(Char('-', ConsoleKey.OemMinus)));
        Assert.IsFalse(home.IsOpened);
        await tree.HandleKeyEventAsync(Char('-', ConsoleKey.Subtract));
        Assert.IsFalse(home.IsOpened, "minus never opens");
    }

    [TestMethod]
    public async Task TreeView_ExpandKeys_OnALeafOrWithoutFocus_AreLeftToTheHost()
    {
        var root = Node(null, "/", isOpened: true);
        var leaf = Node(root, "etc");
        var home = Node(root, "home");
        Node(home, "tamas");
        leaf.IsFocused = true;
        var tree = CreateTree(root);

        Assert.IsFalse(await tree.HandleKeyEventAsync(Key(ConsoleKey.Enter)), "not focused");

        tree.SetFocus();
        Assert.IsFalse(await tree.HandleKeyEventAsync(Key(ConsoleKey.Enter)), "a leaf has nothing to open");
        Assert.IsFalse(await tree.HandleKeyEventAsync(Char('+', ConsoleKey.Add)));
        Assert.IsFalse(home.IsOpened);
    }

    [TestMethod]
    public async Task TreeView_Disabled_IgnoresTheExpandKeys()
    {
        var root = Node(null, "/", isOpened: true);
        var home = Node(root, "home");
        Node(home, "tamas");
        home.IsFocused = true;
        var tree = CreateTree(root);
        tree.SetFocus();
        tree.IsEnabled = false;

        await tree.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.IsFalse(home.IsOpened);
    }

    [TestMethod]
    public async Task TreeView_Right_OnAClosedNode_OpensItInPlace_ThenEntersIt()
    {
        var root = Node(null, "/", isOpened: true);
        var home = Node(root, "home");
        var tamas = Node(home, "tamas");
        home.IsFocused = true;
        var tree = CreateTree(root);
        tree.SetFocus();

        await tree.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.IsTrue(home.IsOpened);
        Assert.IsTrue(home.IsFocused, "the first Right only opens the node");
        Assert.IsFalse(tamas.IsFocused);

        await tree.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.IsTrue(tamas.IsFocused, "the second Right steps into the first child");
        Assert.IsTrue(home.IsOpened);
    }

    [TestMethod]
    public async Task TreeView_Left_OnAnOpenNode_ClosesItInPlace()
    {
        var root = Node(null, "/", isOpened: true);
        var home = Node(root, "home", isOpened: true);
        Node(home, "tamas");
        home.IsFocused = true;
        var tree = CreateTree(root);
        tree.SetFocus();

        await tree.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));

        Assert.IsFalse(home.IsOpened);
        Assert.IsTrue(home.IsFocused, "the focus stays on the node that closed");
        Assert.IsTrue(root.IsOpened, "and its parent is untouched");
    }

    [TestMethod]
    public async Task TreeView_Left_OnAClosedNodeOrALeaf_StepsUpToTheParent_WhichStaysOpen()
    {
        var root = Node(null, "/", isOpened: true);
        var home = Node(root, "home", isOpened: true);
        var tamas = Node(home, "tamas");
        var other = Node(home, "other");
        tamas.IsFocused = true;
        var tree = CreateTree(root);
        tree.SetFocus();

        await tree.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));

        Assert.IsTrue(home.IsFocused);
        Assert.IsFalse(tamas.IsFocused);
        Assert.IsTrue(home.IsOpened, "the siblings stay in view");
        Assert.IsNotNull(other);
    }

    [TestMethod]
    public async Task TreeView_Left_OnTheRoot_DoesNothing()
    {
        var root = Node(null, "/", isOpened: true);
        Node(root, "etc");
        root.IsFocused = true;
        var tree = CreateTree(root);
        tree.SetFocus();

        await tree.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        Assert.IsTrue(root.IsOpened || root.IsFocused);

        await tree.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        Assert.IsTrue(root.IsFocused, "a root has no parent to step up to");
    }
}
