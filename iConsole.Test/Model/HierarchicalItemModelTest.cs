using iConsole.Data;
using iConsole.Model;

namespace iConsole.Test.Model;

[TestClass]
public class HierarchicalItemModelTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_InitializesProperties()
    {
        var model = new HierarchicalItemModel(null, "Root");

        Assert.AreEqual("Root", model.Name);
        Assert.IsNull(model.Parent);
        Assert.IsNotNull(model.Children);
        Assert.AreEqual(0, model.Children.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_WithParent_SetsParentCorrectly()
    {
        var parent = new HierarchicalItemModel(null, "Parent");
        var child = new HierarchicalItemModel(parent, "Child");

        Assert.AreEqual("Child", child.Name);
        Assert.AreSame(parent, child.Parent);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Layer_RootItem_ReturnsZero()
    {
        var model = new HierarchicalItemModel(null, "Root");

        Assert.AreEqual(0, model.Layer);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Layer_ChildItem_ReturnsParentCountPlusOne()
    {
        var parent = new HierarchicalItemModel(null, "Parent");
        var child = new HierarchicalItemModel(parent, "Child");

        Assert.AreEqual(1, child.Layer);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsOpened_DefaultValue_IsFalse()
    {
        var model = new HierarchicalItemModel(null, "Item");

        Assert.IsFalse(model.IsOpened);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsOpened_SetValue_RaisesPropertyChanged()
    {
        var model = new HierarchicalItemModel(null, "Item");
        var propertyChangedRaised = false;
        model.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(model.IsOpened)) propertyChangedRaised = true; };

        model.IsOpened = true;

        Assert.IsTrue(propertyChangedRaised);
        Assert.IsTrue(model.IsOpened);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsFocused_DefaultValue_IsFalse()
    {
        var model = new HierarchicalItemModel(null, "Item");

        Assert.IsFalse(model.IsFocused);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsFocused_SetValue_RaisesPropertyChanged()
    {
        var model = new HierarchicalItemModel(null, "Item");
        var propertyChangedRaised = false;
        model.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(model.IsFocused)) propertyChangedRaised = true; };

        model.IsFocused = true;

        Assert.IsTrue(propertyChangedRaised);
        Assert.IsTrue(model.IsFocused);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Count_NoChildren_ReturnsZero()
    {
        var model = new HierarchicalItemModel(null, "Item");

        Assert.AreEqual(0, model.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Count_WithChildren_ReturnsCorrectCount()
    {
        var parent = new HierarchicalItemModel(null, "Parent");
        var child1 = new HierarchicalItemModel(parent, "Child1");
        var child2 = new HierarchicalItemModel(parent, "Child2");
        parent.Children.Add(child1);
        parent.Children.Add(child2);

        Assert.AreEqual(2, parent.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Count_WithNestedChildren_ReturnsRecursiveCount()
    {
        var root = new HierarchicalItemModel(null, "Root");
        var child = new HierarchicalItemModel(root, "Child");
        var grandchild = new HierarchicalItemModel(child, "Grandchild");
        root.Children.Add(child);
        child.Children.Add(grandchild);

        Assert.AreEqual(2, root.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetFocusedItem_WhenFocused_ReturnsItself()
    {
        var model = new HierarchicalItemModel(null, "Item");
        model.IsFocused = true;

        var focused = model.GetFocusedItem();

        Assert.AreSame(model, focused);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetFocusedItem_WhenNotFocused_ReturnsNull()
    {
        var model = new HierarchicalItemModel(null, "Item");

        var focused = model.GetFocusedItem();

        Assert.IsNull(focused);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetFocusedItem_WithFocusedChild_ReturnsChild()
    {
        var parent = new HierarchicalItemModel(null, "Parent");
        var child = new HierarchicalItemModel(parent, "Child");
        parent.Children.Add(child);
        child.IsFocused = true;

        var focused = parent.GetFocusedItem();

        Assert.AreSame(child, focused);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetNextItem_WithNextSibling_ReturnsNextSibling()
    {
        var parent = new HierarchicalItemModel(null, "Parent");
        var child1 = new HierarchicalItemModel(parent, "Child1");
        var child2 = new HierarchicalItemModel(parent, "Child2");
        parent.Children.Add(child1);
        parent.Children.Add(child2);

        var next = child1.GetNextItem();

        Assert.AreSame(child2, next);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetNextItem_LastSibling_ReturnsItself()
    {
        var parent = new HierarchicalItemModel(null, "Parent");
        var child = new HierarchicalItemModel(parent, "Child");
        parent.Children.Add(child);

        var next = child.GetNextItem();

        Assert.AreSame(child, next);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetPreviousItem_WithPreviousSibling_ReturnsPreviousSibling()
    {
        var parent = new HierarchicalItemModel(null, "Parent");
        var child1 = new HierarchicalItemModel(parent, "Child1");
        var child2 = new HierarchicalItemModel(parent, "Child2");
        parent.Children.Add(child1);
        parent.Children.Add(child2);

        var previous = child2.GetPreviousItem();

        Assert.AreSame(child1, previous);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetPreviousItem_FirstSibling_ReturnsParent()
    {
        var parent = new HierarchicalItemModel(null, "Parent");
        var child = new HierarchicalItemModel(parent, "Child");
        parent.Children.Add(child);

        var previous = child.GetPreviousItem();

        Assert.AreSame(parent, previous);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Position_CanBeSetAndGet()
    {
        var model = new HierarchicalItemModel(null, "Item");
        var position = new Position(10, 20);

        model.Position = position;

        Assert.AreEqual(10, model.Position.X);
        Assert.AreEqual(20, model.Position.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Size_CanBeSetAndGet()
    {
        var model = new HierarchicalItemModel(null, "Item");
        var size = new Size(80, 24);

        model.Size = size;

        Assert.AreEqual(80, model.Size.Width);
        Assert.AreEqual(24, model.Size.Height);
    }
}
