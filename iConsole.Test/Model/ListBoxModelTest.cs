using iConsole.Data;
using iConsole.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Model;

[TestClass]
public class ListBoxModelTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Initialize_PopulatesListItems_WhenUpdated()
    {
        var items = new ConcurrentObservableCollection<string> { "item-1", "item-2" };
        var model = new ListBoxModel<string>(items);

        model.UpdateListItems();

        Assert.AreEqual(2, model.ListItems.Count);
        Assert.AreEqual("item-1", model.ListItems[0].Item);
        Assert.AreEqual("item-2", model.ListItems[1].Item);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CollectionChanged_DefersRefreshUntilUpdateListItems()
    {
        var items = new ConcurrentObservableCollection<string> { "item-1" };
        IListBoxModel<string> model = new ListBoxModel<string>(items);
        model.UpdateListItems();

        items.Add("item-2");

        Assert.IsTrue(model.HasPendingUpdate);
        Assert.AreEqual(1, model.ListItems.Count);

        model.UpdateListItems();

        Assert.IsFalse(model.HasPendingUpdate);
        Assert.AreEqual(2, model.ListItems.Count);
    }
}
