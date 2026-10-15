using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class ConcurrentObservableCollectionTest
{
    [TestMethod]
    public void ConcurrentObservableCollectionConstructor()
    {
        var collection = new ConcurrentObservableCollection<string>();
        Assert.AreEqual(0, collection.Count);
    }

    [TestMethod]
    public void ConcurrentObservableCollectionConstructorWithItems()
    {
        var items = new[] { "item1", "item2", "item3" };
        var collection = new ConcurrentObservableCollection<string>(items);
        Assert.AreEqual(3, collection.Count);
        Assert.AreEqual("item1", collection[0]);
        Assert.AreEqual("item2", collection[1]);
        Assert.AreEqual("item3", collection[2]);
    }

    [TestMethod]
    public void ConcurrentObservableCollectionAdd()
    {
        var collection = new ConcurrentObservableCollection<string>();
        collection.Add("item1");
        Assert.AreEqual(1, collection.Count);
        Assert.AreEqual("item1", collection[0]);
    }

    [TestMethod]
    public void ConcurrentObservableCollectionAddRaisesEvent()
    {
        var collection = new ConcurrentObservableCollection<string>();
        bool collectionChanged = false;

        collection.CollectionChanged += (s, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                collectionChanged = true;
        };

        collection.Add("item1");
        Assert.IsTrue(collectionChanged);
    }

    [TestMethod]
    public void ConcurrentObservableCollectionRemove()
    {
        var collection = new ConcurrentObservableCollection<string>();
        collection.Add("item1");
        collection.Add("item2");
        collection.Remove("item1");
        Assert.AreEqual(1, collection.Count);
        Assert.AreEqual("item2", collection[0]);
    }

    [TestMethod]
    public void ConcurrentObservableCollectionClear()
    {
        var collection = new ConcurrentObservableCollection<string>();
        collection.Add("item1");
        collection.Add("item2");
        collection.Clear();
        Assert.AreEqual(0, collection.Count);
    }

    [TestMethod]
    public void ConcurrentObservableCollectionInsert()
    {
        var collection = new ConcurrentObservableCollection<string>();
        collection.Add("item1");
        collection.Add("item3");
        collection.Insert(1, "item2");
        Assert.AreEqual(3, collection.Count);
        Assert.AreEqual("item2", collection[1]);
    }

    [TestMethod]
    public void ConcurrentObservableCollectionMove()
    {
        var collection = new ConcurrentObservableCollection<string>();
        collection.Add("item1");
        collection.Add("item2");
        collection.Add("item3");
        collection.Move(0, 2);
        Assert.AreEqual("item2", collection[0]);
        Assert.AreEqual("item3", collection[1]);
        Assert.AreEqual("item1", collection[2]);
    }

    [TestMethod]
    public void ConcurrentObservableCollectionIndexOf()
    {
        var collection = new ConcurrentObservableCollection<string>();
        collection.Add("item1");
        collection.Add("item2");
        Assert.AreEqual(0, collection.IndexOf("item1"));
        Assert.AreEqual(1, collection.IndexOf("item2"));
    }

    [TestMethod]
    public void ConcurrentObservableCollectionContains()
    {
        var collection = new ConcurrentObservableCollection<string>();
        collection.Add("item1");
        Assert.IsTrue(collection.Contains("item1"));
        Assert.IsFalse(collection.Contains("item2"));
    }
}
