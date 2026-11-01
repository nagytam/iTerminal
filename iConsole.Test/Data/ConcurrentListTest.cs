using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class ConcurrentListTest
{
    private class TestItem
    {
        public string Name { get; set; } = "";

        public TestItem(string name)
        {
            Name = name;
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConcurrentListConstructor()
    {
        var list = new ConcurrentList<TestItem>();
        Assert.AreEqual(0, list.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConcurrentListAdd()
    {
        var list = new ConcurrentList<TestItem>();
        var item = new TestItem("item1");
        list.Add(item);
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(item, list[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConcurrentListAddMultiple()
    {
        var list = new ConcurrentList<TestItem>();
        list.Add(new TestItem("item1"));
        list.Add(new TestItem("item2"));
        list.Add(new TestItem("item3"));
        Assert.AreEqual(3, list.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConcurrentListRemove()
    {
        var list = new ConcurrentList<TestItem>();
        var item1 = new TestItem("item1");
        var item2 = new TestItem("item2");
        list.Add(item1);
        list.Add(item2);
        list.Remove(item1);
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual(item2, list[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConcurrentListClear()
    {
        var list = new ConcurrentList<TestItem>();
        list.Add(new TestItem("item1"));
        list.Add(new TestItem("item2"));
        list.Clear();
        Assert.AreEqual(0, list.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConcurrentListContains()
    {
        var list = new ConcurrentList<TestItem>();
        var item1 = new TestItem("item1");
        var item2 = new TestItem("item2");
        list.Add(item1);
        list.Add(item2);
        Assert.IsTrue(list.Contains(item1));
        Assert.IsFalse(list.Contains(new TestItem("item3")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConcurrentListIndexOf()
    {
        var list = new ConcurrentList<TestItem>();
        var item1 = new TestItem("item1");
        var item2 = new TestItem("item2");
        list.Add(item1);
        list.Add(item2);
        Assert.AreEqual(0, list.IndexOf(item1));
        Assert.AreEqual(1, list.IndexOf(item2));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConcurrentListInsert()
    {
        var list = new ConcurrentList<TestItem>();
        var item1 = new TestItem("item1");
        var item2 = new TestItem("item2");
        var item3 = new TestItem("item3");
        list.Add(item1);
        list.Add(item3);
        list.Insert(1, item2);
        Assert.AreEqual(3, list.Count);
        Assert.AreEqual(item2, list[1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConcurrentListInsertAt()
    {
        var list = new ConcurrentList<TestItem>();
        var item1 = new TestItem("item1");
        var item2 = new TestItem("item2");
        var item3 = new TestItem("item3");
        list.Add(item1);
        list.Add(item3);
        list.InsertAt(1, item2);
        Assert.AreEqual(3, list.Count);
        Assert.AreEqual(item2, list[1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConcurrentListRemoveAt()
    {
        var list = new ConcurrentList<TestItem>();
        list.Add(new TestItem("item1"));
        list.Add(new TestItem("item2"));
        list.Add(new TestItem("item3"));
        list.RemoveAt(1);
        Assert.AreEqual(2, list.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConcurrentListAsSnapshot()
    {
        var list = new ConcurrentList<TestItem>();
        var item1 = new TestItem("item1");
        var item2 = new TestItem("item2");
        list.Add(item1);
        list.Add(item2);
        var snapshot = list.AsSnapshot();
        Assert.AreEqual(2, snapshot.Count);
        Assert.AreEqual(item1, snapshot[0]);
        Assert.AreEqual(item2, snapshot[1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConcurrentListToList()
    {
        var list = new ConcurrentList<TestItem>();
        var item1 = new TestItem("item1");
        var item2 = new TestItem("item2");
        list.Add(item1);
        list.Add(item2);
        var toList = list.ToList();
        Assert.AreEqual(2, toList.Count);
        Assert.AreEqual(item1, toList[0]);
        Assert.AreEqual(item2, toList[1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConcurrentListForEach()
    {
        var list = new ConcurrentList<TestItem>();
        list.Add(new TestItem("item1"));
        list.Add(new TestItem("item2"));
        int count = 0;
        list.ForEach(item => count++);
        Assert.AreEqual(2, count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConcurrentListCopyTo()
    {
        var list = new ConcurrentList<TestItem>();
        var item1 = new TestItem("item1");
        var item2 = new TestItem("item2");
        list.Add(item1);
        list.Add(item2);
        var array = new TestItem[2];
        list.CopyTo(array, 0);
        Assert.AreEqual(item1, array[0]);
        Assert.AreEqual(item2, array[1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConcurrentListIsReadOnly()
    {
        var list = new ConcurrentList<TestItem>();
        Assert.IsFalse(list.IsReadOnly);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConcurrentListGetEnumerator()
    {
        var list = new ConcurrentList<TestItem>();
        var item1 = new TestItem("item1");
        var item2 = new TestItem("item2");
        list.Add(item1);
        list.Add(item2);
        int count = 0;
        foreach (var item in list)
        {
            count++;
        }
        Assert.AreEqual(2, count);
    }
}
