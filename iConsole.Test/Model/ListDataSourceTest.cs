using iConsole.Model;
using System.Collections.ObjectModel;

namespace iConsole.Test.Model;

[TestClass]
public class ListDataSourceTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void RowsAreReadByIndex()
    {
        ListDataSource<string> source = new(["a", "b", "c"]);

        Assert.AreEqual(3, source.Count);
        Assert.IsTrue(source.TryGet(1, out string? item));
        Assert.AreEqual("b", item);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARowOutOfRangeIsMissing()
    {
        ListDataSource<string> source = new(["a"]);

        Assert.IsFalse(source.TryGet(1, out _));
        Assert.IsFalse(source.TryGet(-1, out _));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RefreshRaisesChanged()
    {
        ListDataSource<string> source = new(["a"]);
        int changed = 0;
        source.Changed += (_, _) => changed++;

        source.Refresh();

        Assert.AreEqual(1, changed);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AListThatNotifiesRaisesChangedByItself()
    {
        ObservableCollection<string> list = ["a"];
        ListDataSource<string> source = new(list);
        int changed = 0;
        source.Changed += (_, _) => changed++;

        list.Add("b");

        Assert.AreEqual(1, changed);
        Assert.AreEqual(2, source.Count);
    }
}
