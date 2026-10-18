using iConsole.Data;
using iConsole.Model;

namespace iConsole.Test.Data;

[TestClass]
public class ConcurrentListReplaceAllTest
{
    [TestMethod]
    public void ReplaceAll_SwapsTheContent()
    {
        ConcurrentList<string> list = new();
        list.Add("a");
        list.Add("b");

        list.ReplaceAll(["x", "y", "z"]);

        CollectionAssert.AreEqual(new[] { "x", "y", "z" }, list.AsSnapshot().ToArray());
    }

    [TestMethod]
    public void ReplaceAll_WithItsOwnItems_Works()
    {
        ConcurrentList<string> list = new();
        list.Add("a");

        list.ReplaceAll(list.AsSnapshot().Concat(["b"]));

        CollectionAssert.AreEqual(new[] { "a", "b" }, list.AsSnapshot().ToArray());
    }

    [TestMethod]
    public async Task AReaderNeverSeesAnEmptyOrPartialList()
    {
        string[] full = [.. Enumerable.Range(0, 50).Select(i => "item" + i)];
        ConcurrentList<string> list = new();
        list.ReplaceAll(full);
        using CancellationTokenSource stop = new();

        Task writer = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                list.ReplaceAll(full);
            }
        });

        int broken = 0;
        for (int i = 0; i < 20000; i++)
        {
            if (list.AsSnapshot().Count != full.Length)
            {
                broken++;
            }
        }

        stop.Cancel();
        await writer;
        Assert.AreEqual(0, broken);
    }

    [TestMethod]
    public async Task ListBoxModel_RefreshedFromAWorker_NeverShowsAnEmptyList()
    {
        ConcurrentObservableCollection<string> items = new(Enumerable.Range(0, 30).Select(i => "row" + i));
        ListBoxModel<string> model = new(items);
        model.UpdateListItems();
        using CancellationTokenSource stop = new();

        Task writer = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                model.Invalidate();
                model.UpdateListItems();
            }
        });

        int broken = 0;
        for (int i = 0; i < 20000; i++)
        {
            if (model.ListItems.Count != 30)
            {
                broken++;
            }
        }

        stop.Cancel();
        await writer;
        Assert.AreEqual(0, broken);
    }
}
