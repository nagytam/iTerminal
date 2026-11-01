using iConsole.Data;
using iConsole.Helper;

namespace iConsole.Test.Helper;

[TestClass]
public class ConcurrentListExtensionsTest
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
    public void FromList()
    {
        var list = new List<TestItem>
        {
            new TestItem("item1"),
            new TestItem("item2"),
            new TestItem("item3")
        };

        var concurrentList = new ConcurrentList<TestItem>();
        concurrentList.FromList(list);

        Assert.AreEqual(3, concurrentList.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FromListClearsExistingItems()
    {
        var concurrentList = new ConcurrentList<TestItem>();
        concurrentList.Add(new TestItem("old"));

        var list = new List<TestItem>
        {
            new TestItem("new1"),
            new TestItem("new2")
        };

        concurrentList.FromList(list);

        Assert.AreEqual(2, concurrentList.Count);
        Assert.AreEqual("new1", concurrentList[0].Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FromListEmpty()
    {
        var concurrentList = new ConcurrentList<TestItem>();
        concurrentList.Add(new TestItem("item"));

        var emptyList = new List<TestItem>();
        concurrentList.FromList(emptyList);

        Assert.AreEqual(0, concurrentList.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FromListPreservesOrder()
    {
        var list = new List<TestItem>
        {
            new TestItem("first"),
            new TestItem("second"),
            new TestItem("third")
        };

        var concurrentList = new ConcurrentList<TestItem>();
        concurrentList.FromList(list);

        Assert.AreEqual("first", concurrentList[0].Name);
        Assert.AreEqual("second", concurrentList[1].Name);
        Assert.AreEqual("third", concurrentList[2].Name);
    }
}
