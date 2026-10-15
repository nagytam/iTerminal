using iConsole.Model;

namespace iConsole.Test.Model;

[TestClass]
public class ListItemTest
{
    [TestMethod]
    public void ListItemConstructor()
    {
        var item = new ListItem<string>("test");
        Assert.AreEqual("test", item.Item);
        Assert.AreEqual("test", item.Value);
        Assert.IsFalse(item.IsEnabled);
        Assert.IsFalse(item.IsSelected);
    }

    [TestMethod]
    public void ListItemSetValue()
    {
        var item = new ListItem<string>("test");
        item.Value = "new value";
        Assert.AreEqual("new value", item.Value);
    }

    [TestMethod]
    public void ListItemSetEnabled()
    {
        var item = new ListItem<string>("test");
        item.IsEnabled = true;
        Assert.IsTrue(item.IsEnabled);
    }

    [TestMethod]
    public void ListItemSetSelected()
    {
        var item = new ListItem<string>("test");
        item.IsSelected = true;
        Assert.IsTrue(item.IsSelected);
    }

    [TestMethod]
    public void ListItemWithCustomObject()
    {
        var customObj = new { Name = "Test", Value = 42 };
        var item = new ListItem<object>(customObj);
        Assert.AreEqual(customObj, item.Item);
    }

    [TestMethod]
    public void ListItemWithInteger()
    {
        var item = new ListItem<int>(42);
        Assert.AreEqual(42, item.Item);
        Assert.AreEqual("42", item.Value);
    }

    [TestMethod]
    public void ListItemToString()
    {
        var item = new ListItem<string>("test");
        var str = item.ToString();
        Assert.IsNotNull(str);
    }

    [TestMethod]
    public void ListItemInterface()
    {
        var item = new ListItem<string>("test");
        Assert.IsInstanceOfType(item, typeof(IListItem<string>));
    }
}
