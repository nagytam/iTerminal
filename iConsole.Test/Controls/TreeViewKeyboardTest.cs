using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Moving through a tall tree: Home, End, PageUp, PageDown, and the arrows around them.</summary>
[TestClass]
public class TreeViewKeyboardTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    /// <summary>A root with <paramref name="count"/> leaf children, opened, the root focused; the view is 6 rows tall.</summary>
    private static (TreeView Tree, HierarchicalItemModel Root) NewTree(int count, uint height = 6)
    {
        HierarchicalItemModel root = new(null, "/") { IsOpened = true };
        for (int i = 0; i < count; i++)
        {
            root.Children.Add(new HierarchicalItemModel(root, "item " + i));
        }

        root.IsFocused = true;
        TreeView tree = new(root, Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, height) };
        tree.SetFocus();
        return (tree, root);
    }

    private static Task Press(TreeView tree, ConsoleKey key, bool control = false)
        => tree.HandleKeyEventAsync(new KeyEvent('\0', key, false, false, control));

    private static int FocusedIndex(TreeView tree) => tree.GetVisibleRows().FindIndex(row => row.Item.IsFocused);

    private static int FocusedCount(TreeView tree) => tree.GetVisibleRows().Count(row => row.Item.IsFocused);

    [TestMethod]
    public async Task End_FocusesTheLastRow_AndHomeTheFirst()
    {
        (TreeView tree, _) = NewTree(30);
        await Press(tree, ConsoleKey.End);
        Assert.AreEqual(30, FocusedIndex(tree));
        Assert.AreEqual(1, FocusedCount(tree));
        await Press(tree, ConsoleKey.Home);
        Assert.AreEqual(0, FocusedIndex(tree));
        Assert.AreEqual(1, FocusedCount(tree));
    }

    [TestMethod]
    public async Task PageDown_AndPageUp_MoveByOneRowLessThanTheView()
    {
        (TreeView tree, _) = NewTree(30);
        await Press(tree, ConsoleKey.PageDown);
        Assert.AreEqual(5, FocusedIndex(tree));
        await Press(tree, ConsoleKey.PageDown);
        Assert.AreEqual(10, FocusedIndex(tree));
        await Press(tree, ConsoleKey.PageUp);
        Assert.AreEqual(5, FocusedIndex(tree));
        Assert.AreEqual(1, FocusedCount(tree));
    }

    [TestMethod]
    public async Task Paging_StopsAtTheEnds()
    {
        (TreeView tree, _) = NewTree(8);
        await Press(tree, ConsoleKey.PageUp);
        Assert.AreEqual(0, FocusedIndex(tree));
        for (int i = 0; i < 5; i++)
        {
            await Press(tree, ConsoleKey.PageDown);
        }

        Assert.AreEqual(8, FocusedIndex(tree));
        Assert.AreEqual(1, FocusedCount(tree));
    }

    [TestMethod]
    public async Task TheFocusedRow_IsAlwaysDrawnInTheView()
    {
        (TreeView tree, _) = NewTree(30);
        foreach (ConsoleKey key in new[] { ConsoleKey.End, ConsoleKey.PageUp, ConsoleKey.PageUp, ConsoleKey.Home, ConsoleKey.PageDown, ConsoleKey.DownArrow })
        {
            await Press(tree, key);
            MockBuffer buffer = new(new Size(20, 6));
            tree.Draw(buffer);
            int focused = FocusedIndex(tree);
            Assert.IsTrue(focused >= tree.ScrollOffset && focused < tree.ScrollOffset + 6, $"{key}: row {focused}, offset {tree.ScrollOffset}");
        }
    }

    [TestMethod]
    public async Task WithNoRowFocused_PageDownStartsFromTheTop()
    {
        (TreeView tree, HierarchicalItemModel root) = NewTree(30);
        root.IsFocused = false;
        await Press(tree, ConsoleKey.PageDown);
        Assert.AreEqual(5, FocusedIndex(tree));
    }

    [TestMethod]
    public async Task ACollapsedTree_HasOnlyTheRootToVisit()
    {
        (TreeView tree, HierarchicalItemModel root) = NewTree(5);
        root.IsOpened = false;
        await Press(tree, ConsoleKey.End);
        await Press(tree, ConsoleKey.PageDown);
        Assert.AreEqual(0, FocusedIndex(tree));
        Assert.IsTrue(root.IsFocused);
    }

    [TestMethod]
    public async Task Keys_AreIgnoredWithoutFocus_WhenDisabled_OrWithAModifier()
    {
        (TreeView tree, _) = NewTree(30);
        await Press(tree, ConsoleKey.End, control: true);
        Assert.AreEqual(0, FocusedIndex(tree));
        tree.IsEnabled = false;
        Assert.IsFalse(await tree.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, false)));
        tree.IsEnabled = true;
        tree.ResetFocus();
        Assert.IsFalse(await tree.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, false)));
        Assert.AreEqual(0, FocusedIndex(tree));
    }

    [TestMethod]
    public async Task TheArrows_StillStepOneRow_AndOtherKeysAreNotHandled()
    {
        (TreeView tree, _) = NewTree(5);
        await Press(tree, ConsoleKey.DownArrow);
        await Press(tree, ConsoleKey.DownArrow);
        Assert.AreEqual(2, FocusedIndex(tree));
        await Press(tree, ConsoleKey.UpArrow);
        Assert.AreEqual(1, FocusedIndex(tree));
        Assert.IsFalse(await tree.HandleKeyEventAsync(new KeyEvent('a', ConsoleKey.A, false, false, false)));
    }

    [TestMethod]
    public async Task ARootWithoutChildren_SurvivesEveryNavigationKey()
    {
        (TreeView tree, _) = NewTree(0);
        foreach (ConsoleKey key in new[] { ConsoleKey.Home, ConsoleKey.End, ConsoleKey.PageUp, ConsoleKey.PageDown, ConsoleKey.UpArrow, ConsoleKey.DownArrow, ConsoleKey.LeftArrow, ConsoleKey.RightArrow })
        {
            await Press(tree, key);
        }

        Assert.AreEqual(0, FocusedIndex(tree));
        tree.Draw(new MockBuffer(new Size(20, 6)));
    }
}
