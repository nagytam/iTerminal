using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Test.Services;

[TestClass]
public class BreakpointStoreTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ToggleSetsThenRemovesABreakpoint()
    {
        var store = new BreakpointStore(new TestDatabase());

        Assert.IsTrue(store.Toggle("/src/a.py", 3));
        Assert.IsTrue(store.Contains("/src/a.py", 3));
        Assert.IsFalse(store.Toggle("/src/a.py", 3));
        Assert.IsFalse(store.Contains("/src/a.py", 3));
        CollectionAssert.AreEqual(Array.Empty<string>(), store.Files().ToArray(), "a file with no breakpoints is dropped");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AllListsEveryBreakpointByFileThenLine()
    {
        var store = new BreakpointStore(new TestDatabase());
        store.Toggle("/src/b.py", 9);
        store.Toggle("/src/a.py", 12);
        store.Toggle("/src/a.py", 2);

        CollectionAssert.AreEqual(new[] { ("/src/a.py", 2), ("/src/a.py", 12), ("/src/b.py", 9) }, store.All().ToArray());
        CollectionAssert.AreEqual(new[] { 2, 12 }, store.LinesIn("/src/a.py").ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALineBelowOneIsRefused()
    {
        var store = new BreakpointStore(new TestDatabase());

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => store.Toggle("/src/a.py", 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BreakpointsAreStoredAndReadBackByANewStore()
    {
        var database = new TestDatabase();
        var first = new BreakpointStore(database);
        first.Toggle("/src/a.py", 2);
        first.Toggle("/src/a.py", 12);
        first.Toggle("/src/b.py", 9);
        await first.PendingWrites;

        var second = new BreakpointStore(database);
        await second.Loaded;

        CollectionAssert.AreEqual(new[] { ("/src/a.py", 2), ("/src/a.py", 12), ("/src/b.py", 9) }, second.All().ToArray(), "the breakpoints survive a restart");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARemovedBreakpointIsNotReadBackAgain()
    {
        var database = new TestDatabase();
        var first = new BreakpointStore(database);
        first.Toggle("/src/a.py", 4);
        first.Toggle("/src/a.py", 4);
        first.Toggle("/src/a.py", 7);
        await first.PendingWrites;

        var second = new BreakpointStore(database);
        await second.Loaded;

        CollectionAssert.AreEqual(new[] { 7 }, second.LinesIn("/src/a.py").ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClearRemovesTheSavedBreakpoints()
    {
        var database = new TestDatabase();
        var first = new BreakpointStore(database);
        first.Toggle("/src/a.py", 4);
        await first.PendingWrites;
        first.Clear();
        await first.PendingWrites;

        var second = new BreakpointStore(database);
        await second.Loaded;

        Assert.AreEqual(0, second.All().Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AChangeMadeBeforeTheSavedRowsAreReadWins()
    {
        var database = new TestDatabase();
        var seeded = new BreakpointStore(database);
        seeded.Toggle("/src/a.py", 4);
        await seeded.PendingWrites;

        // The change is made before the saved rows are read: the row must not undo it when it is read.
        var store = new BreakpointStore(database);
        store.Toggle("/src/a.py", 4);
        await store.Loaded;

        Assert.IsTrue(store.Contains("/src/a.py", 4), "a change made in this session is not undone by the saved row read after it");
    }
}
