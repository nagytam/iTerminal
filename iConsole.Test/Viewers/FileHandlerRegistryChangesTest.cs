using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Viewers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Viewers;

/// <summary>The plug-in manager adds and removes handlers while the application runs; the order must stay right and readers must see a whole list.</summary>
[TestClass]
public class FileHandlerRegistryChangesTest
{
    private sealed class FakeViewer(int priority) : IFileViewer
    {
        public int Priority => priority;

        public bool IsCandidate(Url url) => true;

        public Task OpenAsync(Url url, IFileOpenHost host) => Task.CompletedTask;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Add_PlacesTheHandlerByPriority_AfterOthersAtTheSamePriority()
    {
        var existing = new FakeViewer(0);
        var registry = new FileViewerRegistry([existing]);
        var high = new FakeViewer(10);
        var sameLevel = new FakeViewer(0);

        registry.Add(high);
        registry.Add(sameLevel);

        CollectionAssert.AreEqual(new IFileViewer[] { high, existing, sameLevel }, registry.Handlers.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Remove_DropsTheHandler_AndReportsWhetherItWasRegistered()
    {
        var first = new FakeViewer(5);
        var second = new FakeViewer(0);
        var registry = new FileViewerRegistry([first, second]);

        Assert.IsTrue(registry.Remove(first));
        Assert.IsFalse(registry.Remove(first));
        CollectionAssert.AreEqual(new IFileViewer[] { second }, registry.Handlers.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Snapshot_TakenBeforeAChange_DoesNotChange()
    {
        var registry = new FileViewerRegistry([new FakeViewer(0)]);
        IReadOnlyList<IFileViewer> before = registry.Handlers;

        registry.Add(new FakeViewer(0));

        Assert.AreEqual(1, before.Count);
        Assert.AreEqual(2, registry.Handlers.Count);
    }
}
