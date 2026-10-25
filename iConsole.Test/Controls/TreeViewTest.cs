using System.Linq;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class TreeViewTest
{
    private Mock<IThemeService> _mockThemeService = null!;
    private MockBuffer _buffer = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockThemeService = new Mock<IThemeService>();
        _mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        _buffer = new MockBuffer(new Size(80, 40));
    }

    [TestMethod]
    public void KeyHints_DescribeWhatRightAndLeftDo()
    {
        var root = new HierarchicalItemModel(null, "/") { IsOpened = true };
        var treeView = new TreeView(root, _mockThemeService.Object) { Position = new Position(0, 0), ViewSize = new Size(20, 5) };

        var hints = treeView.GetKeyHints().ToDictionary(h => h.Key, h => h.Description);

        Assert.AreEqual("Open the node, or step into its first child", hints["Right"]);
        Assert.AreEqual("Close the node, or go to its parent", hints["Left"]);
    }

    [TestMethod]
    public async Task TreeViewRootOnly()
    {
        var root = new HierarchicalItemModel(null, "/") { IsOpened = true };
        var treeView = new TreeView(root, _mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 15)
        };
        var childA = new HierarchicalItemModel(root, "A") { IsOpened = true };
        childA.Children.Add(new HierarchicalItemModel(childA, "A1"));
        childA.Children.Add(new HierarchicalItemModel(childA, "A2"));
        childA.Children.Add(new HierarchicalItemModel(childA, "A3"));
        treeView.Root.Children.Add(childA);
        treeView.Root.Children.Add(new HierarchicalItemModel(root, "B"));
        treeView.Root.Children.Add(new HierarchicalItemModel(root, "C"));
        await treeView.OnInitializeAsync();
        treeView.OnInitializeLayout();

        treeView.Draw(_buffer);

        var content = _buffer.ContentAsString
            .Split(Environment.NewLine)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();

        CollectionAssert.AreEqual(
            new[] { "▾ /", "▾ A", "A1", "A2", "A3", "B", "C" },
            content.Take(7).ToArray());
    }

    [TestMethod]
    public async Task TreeViewClosedNodeDoesNotRenderChildren()
    {
        var root = new HierarchicalItemModel(null, "/") { IsOpened = true };
        var childA = new HierarchicalItemModel(root, "A") { IsOpened = false };
        childA.Children.Add(new HierarchicalItemModel(childA, "A1"));
        root.Children.Add(childA);

        var treeView = new TreeView(root, _mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 10)
        };

        await treeView.OnInitializeAsync();
        treeView.OnInitializeLayout();
        treeView.Draw(_buffer);

        var content = _buffer.ContentAsString
            .Split(Environment.NewLine)
            .Select(l => l.TrimEnd())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();
        Assert.AreEqual("▾ /", content[0]);
        Assert.AreEqual("  ▸ A", content.Single(l => l.Contains("▸ A")));
        Assert.IsFalse(content.Any(l => l.Contains("A1")));
    }

    [TestMethod]
    public async Task TreeViewFocusRendersSameText()
    {
        var root = new HierarchicalItemModel(null, "/") { IsOpened = true, IsFocused = true };
        root.Children.Add(new HierarchicalItemModel(root, "A"));

        var treeView = new TreeView(root, _mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 10)
        };

        await treeView.OnInitializeAsync();
        treeView.OnInitializeLayout();
        treeView.Draw(_buffer);

        var content = _buffer.ContentAsString
            .Split(Environment.NewLine)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();
        Assert.AreEqual("▾ /", content[0]);
        Assert.AreEqual("A", content.Single(l => l == "A"));
    }

    [TestMethod]
    public void GetAllItemsReturnsAllVisibleItems()
    {
        var root = new HierarchicalItemModel(null, "Root") { IsOpened = true };
        var child1 = new HierarchicalItemModel(root, "Child1") { IsOpened = true };
        var child2 = new HierarchicalItemModel(root, "Child2") { IsOpened = false };
        var grandchild1 = new HierarchicalItemModel(child1, "Grandchild1");
        var grandchild2 = new HierarchicalItemModel(child2, "Grandchild2");

        root.Children.Add(child1);
        root.Children.Add(child2);
        child1.Children.Add(grandchild1);
        child2.Children.Add(grandchild2);

        var treeView = new TreeView(root, _mockThemeService.Object);
        var allItems = treeView.GetAllItems().ToList();

        // Should include root, child1, child2, grandchild1 (but NOT grandchild2 since child2 is closed)
        Assert.AreEqual(4, allItems.Count);
        Assert.IsTrue(allItems.Contains(root));
        Assert.IsTrue(allItems.Contains(child1));
        Assert.IsTrue(allItems.Contains(child2));
        Assert.IsTrue(allItems.Contains(grandchild1));
        Assert.IsFalse(allItems.Contains(grandchild2));
    }

    [TestMethod]
    public void GetAllItemsIncludesAllWhenFullyExpanded()
    {
        var root = new HierarchicalItemModel(null, "Root") { IsOpened = true };
        var child1 = new HierarchicalItemModel(root, "Child1") { IsOpened = true };
        var child2 = new HierarchicalItemModel(root, "Child2") { IsOpened = true };
        var grandchild1 = new HierarchicalItemModel(child1, "Grandchild1");
        var grandchild2 = new HierarchicalItemModel(child2, "Grandchild2");

        root.Children.Add(child1);
        root.Children.Add(child2);
        child1.Children.Add(grandchild1);
        child2.Children.Add(grandchild2);

        var treeView = new TreeView(root, _mockThemeService.Object);
        var allItems = treeView.GetAllItems().ToList();

        Assert.AreEqual(5, allItems.Count);
        Assert.IsTrue(allItems.Contains(root));
        Assert.IsTrue(allItems.Contains(child1));
        Assert.IsTrue(allItems.Contains(child2));
        Assert.IsTrue(allItems.Contains(grandchild1));
        Assert.IsTrue(allItems.Contains(grandchild2));
    }

    [TestMethod]
    public void GetFocusedItemReturnsFocusedItem()
    {
        var root = new HierarchicalItemModel(null, "Root") { IsOpened = true };
        var child1 = new HierarchicalItemModel(root, "Child1");
        var child2 = new HierarchicalItemModel(root, "Child2") { IsFocused = true };

        root.Children.Add(child1);
        root.Children.Add(child2);

        var treeView = new TreeView(root, _mockThemeService.Object);
        var focusedItem = treeView.GetFocusedItem();

        Assert.IsNotNull(focusedItem);
        Assert.AreEqual(child2, focusedItem);
    }

    [TestMethod]
    public void GetFocusedItemReturnsNullWhenNoFocus()
    {
        var root = new HierarchicalItemModel(null, "Root") { IsOpened = true };
        var child1 = new HierarchicalItemModel(root, "Child1");
        root.Children.Add(child1);

        var treeView = new TreeView(root, _mockThemeService.Object);
        var focusedItem = treeView.GetFocusedItem();

        Assert.IsNull(focusedItem);
    }

    [TestMethod]
    public void ExpandAllExpandsAllNodes()
    {
        var root = new HierarchicalItemModel(null, "Root") { IsOpened = false };
        var child1 = new HierarchicalItemModel(root, "Child1") { IsOpened = false };
        var child2 = new HierarchicalItemModel(root, "Child2") { IsOpened = false };
        var grandchild1 = new HierarchicalItemModel(child1, "Grandchild1");
        var grandchild2 = new HierarchicalItemModel(child2, "Grandchild2");

        root.Children.Add(child1);
        root.Children.Add(child2);
        child1.Children.Add(grandchild1);
        child2.Children.Add(grandchild2);

        var treeView = new TreeView(root, _mockThemeService.Object);
        treeView.ExpandAll();

        Assert.IsTrue(root.IsOpened);
        Assert.IsTrue(child1.IsOpened);
        Assert.IsTrue(child2.IsOpened);
    }

    [TestMethod]
    public void CollapseAllCollapsesAllNodes()
    {
        var root = new HierarchicalItemModel(null, "Root") { IsOpened = true };
        var child1 = new HierarchicalItemModel(root, "Child1") { IsOpened = true };
        var child2 = new HierarchicalItemModel(root, "Child2") { IsOpened = true };
        var grandchild1 = new HierarchicalItemModel(child1, "Grandchild1");
        var grandchild2 = new HierarchicalItemModel(child2, "Grandchild2");

        root.Children.Add(child1);
        root.Children.Add(child2);
        child1.Children.Add(grandchild1);
        child2.Children.Add(grandchild2);

        var treeView = new TreeView(root, _mockThemeService.Object);
        treeView.CollapseAll();

        Assert.IsFalse(root.IsOpened);
        Assert.IsFalse(child1.IsOpened);
        Assert.IsFalse(child2.IsOpened);
    }

    [TestMethod]
    public void ToggleItemExpandsClosedItem()
    {
        var root = new HierarchicalItemModel(null, "Root");
        var child1 = new HierarchicalItemModel(root, "Child1") { IsOpened = false };
        root.Children.Add(child1);
        child1.Children.Add(new HierarchicalItemModel(child1, "Grandchild1")); // Add children so toggle works

        var treeView = new TreeView(root, _mockThemeService.Object);
        treeView.ToggleItem(child1);

        Assert.IsTrue(child1.IsOpened);
    }

    [TestMethod]
    public void ToggleItemCollapsesOpenItem()
    {
        var root = new HierarchicalItemModel(null, "Root");
        var child1 = new HierarchicalItemModel(root, "Child1") { IsOpened = true };
        root.Children.Add(child1);
        child1.Children.Add(new HierarchicalItemModel(child1, "Grandchild1")); // Add children so toggle works

        var treeView = new TreeView(root, _mockThemeService.Object);
        treeView.ToggleItem(child1);

        Assert.IsFalse(child1.IsOpened);
    }

    [TestMethod]
    public void ToggleItemDoesNotThrowForLeafNode()
    {
        var root = new HierarchicalItemModel(null, "Root");
        var child1 = new HierarchicalItemModel(root, "Child1"); // No children

        var treeView = new TreeView(root, _mockThemeService.Object);
        treeView.ToggleItem(child1);

        Assert.IsFalse(child1.IsOpened);
    }

    [TestMethod]
    public void GetVisibleItemCountReturnsCorrectCount()
    {
        var root = new HierarchicalItemModel(null, "Root") { IsOpened = true };
        var child1 = new HierarchicalItemModel(root, "Child1") { IsOpened = true };
        var child2 = new HierarchicalItemModel(root, "Child2") { IsOpened = false };
        var grandchild1 = new HierarchicalItemModel(child1, "Grandchild1");
        var grandchild2 = new HierarchicalItemModel(child2, "Grandchild2");

        root.Children.Add(child1);
        root.Children.Add(child2);
        child1.Children.Add(grandchild1);
        child2.Children.Add(grandchild2);

        var treeView = new TreeView(root, _mockThemeService.Object);
        var count = treeView.GetVisibleItemCount();

        // Root + Child1 + Child2 + Grandchild1 = 4 (Grandchild2 is hidden)
        Assert.AreEqual(4, count);
    }

    [TestMethod]
    public void GetItemDepthReturnsCorrectDepth()
    {
        var root = new HierarchicalItemModel(null, "Root");
        var child1 = new HierarchicalItemModel(root, "Child1");
        var grandchild1 = new HierarchicalItemModel(child1, "Grandchild1");
        var greatgrandchild1 = new HierarchicalItemModel(grandchild1, "Greatgrandchild1");

        root.Children.Add(child1);
        child1.Children.Add(grandchild1);
        grandchild1.Children.Add(greatgrandchild1);

        var treeView = new TreeView(root, _mockThemeService.Object);

        Assert.AreEqual(0, treeView.GetItemDepth(root));
        Assert.AreEqual(1, treeView.GetItemDepth(child1));
        Assert.AreEqual(2, treeView.GetItemDepth(grandchild1));
        Assert.AreEqual(3, treeView.GetItemDepth(greatgrandchild1));
    }

    [TestMethod]
    public void TreeViewCanFocusIsTrue()
    {
        var root = new HierarchicalItemModel(null, "Root");
        var treeView = new TreeView(root, _mockThemeService.Object);

        Assert.IsTrue(treeView.CanFocus);
    }

    [TestMethod]
    public async Task TreeViewDrawsWithNoVisibleItems()
    {
        var root = new HierarchicalItemModel(null, "Root") { IsOpened = false };
        root.Children.Add(new HierarchicalItemModel(root, "Child1"));

        var treeView = new TreeView(root, _mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 10)
        };

        await treeView.OnInitializeAsync();
        treeView.OnInitializeLayout();
        treeView.Draw(_buffer);

        // Should not throw
        Assert.IsNotNull(_buffer);
    }

    [TestMethod]
    public void GetAllItemsWithEmptyTree()
    {
        var root = new HierarchicalItemModel(null, "Root") { IsOpened = true };
        var treeView = new TreeView(root, _mockThemeService.Object);
        var allItems = treeView.GetAllItems().ToList();

        // Should only contain the root
        Assert.AreEqual(1, allItems.Count);
        Assert.AreEqual(root, allItems[0]);
    }

    [TestMethod]
    public async Task TreeViewDrawsLeafNodeWithoutExpansionIndicator()
    {
        var root = new HierarchicalItemModel(null, "Root") { IsOpened = true };
        var leaf = new HierarchicalItemModel(root, "Leaf"); // No children
        root.Children.Add(leaf);

        var treeView = new TreeView(root, _mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 10)
        };

        await treeView.OnInitializeAsync();
        treeView.OnInitializeLayout();
        treeView.Draw(_buffer);

        var content = _buffer.ContentAsString
            .Split(Environment.NewLine)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();

        // Leaf node should not have expansion indicator
        var leafLine = content.FirstOrDefault(l => l.Contains("Leaf"));
        Assert.IsNotNull(leafLine);
        Assert.IsFalse(leafLine!.Contains(TreeView.ExpandedGlyph) || leafLine.Contains(TreeView.CollapsedGlyph));
    }

    [TestMethod]
    public async Task TreeViewDeepNesting()
    {
        var root = new HierarchicalItemModel(null, "Root") { IsOpened = true };
        var current = root;
        const int depth = 5;

        for (int i = 0; i < depth; i++)
        {
            var child = new HierarchicalItemModel(current, $"Level{i}") { IsOpened = true };
            current.Children.Add(child);
            current = child;
        }

        var treeView = new TreeView(root, _mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 20)
        };

        await treeView.OnInitializeAsync();
        treeView.OnInitializeLayout();
        treeView.Draw(_buffer);

        var allItems = treeView.GetAllItems().ToList();
        Assert.AreEqual(depth + 1, allItems.Count);
    }

    [TestMethod]
    public void ExpandAllWithNoChildren()
    {
        var root = new HierarchicalItemModel(null, "Root");
        var treeView = new TreeView(root, _mockThemeService.Object);

        treeView.ExpandAll();

        Assert.IsFalse(root.IsOpened);
    }

    [TestMethod]
    public void CollapseAllWithNoChildren()
    {
        var root = new HierarchicalItemModel(null, "Root");
        var treeView = new TreeView(root, _mockThemeService.Object);

        treeView.CollapseAll();

        Assert.IsFalse(root.IsOpened);
    }

    [TestMethod]
    public void MultipleItemsFocused()
    {
        var root = new HierarchicalItemModel(null, "Root") { IsOpened = true, IsFocused = true };
        var child1 = new HierarchicalItemModel(root, "Child1") { IsFocused = true };
        var child2 = new HierarchicalItemModel(root, "Child2");

        root.Children.Add(child1);
        root.Children.Add(child2);

        var treeView = new TreeView(root, _mockThemeService.Object);
        var focusedItem = treeView.GetFocusedItem();

        // Should return the first focused item found
        Assert.IsNotNull(focusedItem);
        Assert.IsTrue(focusedItem!.IsFocused);
    }

    [TestMethod]
    public void GetItemDepthOfRootIsZero()
    {
        var root = new HierarchicalItemModel(null, "Root");
        var treeView = new TreeView(root, _mockThemeService.Object);

        Assert.AreEqual(0, treeView.GetItemDepth(root));
    }

    [TestMethod]
    public async Task TreeViewWithMixedNodeTypes()
    {
        var root = new HierarchicalItemModel(null, "Root") { IsOpened = true };
        var folder1 = new HierarchicalItemModel(root, "Folder1") { IsOpened = true };
        var file1 = new HierarchicalItemModel(folder1, "File1"); // Leaf
        var folder2 = new HierarchicalItemModel(root, "Folder2") { IsOpened = false };
        var file2 = new HierarchicalItemModel(folder2, "File2"); // Leaf

        root.Children.Add(folder1);
        root.Children.Add(folder2);
        folder1.Children.Add(file1);
        folder2.Children.Add(file2);

        var treeView = new TreeView(root, _mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(30, 15)
        };

        await treeView.OnInitializeAsync();
        treeView.OnInitializeLayout();
        treeView.Draw(_buffer);

        var allItems = treeView.GetAllItems().ToList();
        // Root + Folder1 + File1 + Folder2 = 4 (File2 is hidden)
        Assert.AreEqual(4, allItems.Count);
    }

    [TestMethod]
    public void TreeViewInitializationWithTreeCommands()
    {
        var root = new HierarchicalItemModel(null, "Root");
        var treeView = new TreeView(root, _mockThemeService.Object);

        // Verify that tree navigation commands are added
        var commands = treeView.Commands.AsSnapshot();
        Assert.IsTrue(commands.Count >= 4, "TreeView should have at least 4 navigation commands");
    }
}
