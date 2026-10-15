using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class ObservableCollectionTest
{
    [TestMethod]
    public void ObservableCollectionConstructor()
    {
        var collection = new ObservableCollection<string>();
        Assert.AreEqual(0, collection.Count);
    }

    [TestMethod]
    public void ObservableCollectionAdd()
    {
        var collection = new ObservableCollection<string>();
        collection.Add("item1");
        Assert.AreEqual(1, collection.Count);
        Assert.AreEqual("item1", collection[0]);
    }

    [TestMethod]
    public void ObservableCollectionAddMultiple()
    {
        var collection = new ObservableCollection<string>();
        collection.Add("item1");
        collection.Add("item2");
        collection.Add("item3");
        Assert.AreEqual(3, collection.Count);
    }

    [TestMethod]
    public void ObservableCollectionReplaceAt()
    {
        var collection = new ObservableCollection<string>();
        collection.Add("item1");
        collection.Add("item2");
        collection.ReplaceAt(0, "replaced");
        Assert.AreEqual("replaced", collection[0]);
        Assert.AreEqual("item2", collection[1]);
    }

    [TestMethod]
    public void ObservableCollectionReplaceAtRaisesEvent()
    {
        var collection = new ObservableCollection<string>();
        collection.Add("item1");
        bool itemChanged = false;

        collection.CollectionChanged += (s, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
                itemChanged = true;
        };

        collection.ReplaceAt(0, "replaced");
        Assert.IsTrue(itemChanged);
    }

    [TestMethod]
    public void ObservableCollectionRemove()
    {
        var collection = new ObservableCollection<string>();
        collection.Add("item1");
        collection.Add("item2");
        collection.Remove("item1");
        Assert.AreEqual(1, collection.Count);
        Assert.AreEqual("item2", collection[0]);
    }

    [TestMethod]
    public void ObservableCollectionClear()
    {
        var collection = new ObservableCollection<string>();
        collection.Add("item1");
        collection.Add("item2");
        collection.Clear();
        Assert.AreEqual(0, collection.Count);
    }

    [TestMethod]
    public void ObservableCollectionContains()
    {
        var collection = new ObservableCollection<string>();
        collection.Add("item1");
        collection.Add("item2");
        Assert.IsTrue(collection.Contains("item1"));
        Assert.IsFalse(collection.Contains("item3"));
    }

    [TestMethod]
    public void ObservableCollectionIndexOf()
    {
        var collection = new ObservableCollection<string>();
        collection.Add("item1");
        collection.Add("item2");
        collection.Add("item3");
        Assert.AreEqual(0, collection.IndexOf("item1"));
        Assert.AreEqual(1, collection.IndexOf("item2"));
        Assert.AreEqual(-1, collection.IndexOf("nonexistent"));
    }

    [TestMethod]
    public void ObservableCollectionInsert()
    {
        var collection = new ObservableCollection<string>();
        collection.Add("item1");
        collection.Add("item3");
        collection.Insert(1, "item2");
        Assert.AreEqual(3, collection.Count);
        Assert.AreEqual("item2", collection[1]);
    }
}
