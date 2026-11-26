using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Test.Services;

[TestClass]
public class BreakpointStoreTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ToggleSetsThenRemovesABreakpoint()
    {
        var store = new BreakpointStore();

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
        var store = new BreakpointStore();
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
        var store = new BreakpointStore();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => store.Toggle("/src/a.py", 0));
    }
}
