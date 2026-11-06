using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iConsole.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Model.ContentModels.Test;

/// <summary>A file panel asked for a folder that has gone opens its nearest parent that is still there, not the working directory.</summary>
[TestClass]
public class ContentModelFileSystemTest
{
    private string _existing = string.Empty;

    [TestInitialize]
    public void Initialize() =>
        _existing = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

    private ContentModelFileSystem NewModel(Func<Url, bool> folderExists)
    {
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());
        var files = new Mock<IFileSystemService>();
        files.Setup(f => f.ExistsFolder(It.IsAny<Url>())).Returns(folderExists);
        var settings = new Mock<ISettingsService>();
        return new ContentModelFileSystem(
            "Test",
            new TaskManager(),
            settings.Object,
            new FolderHistoryService(settings.Object),
            files.Object,
            guard.Object,
            Mock.Of<IMemoryCacheService>(),
            Serilog.Log.Logger);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AGoneFolder_OpensItsNearestExistingParent()
    {
        var gone = Path.Combine(_existing, "no-such-folder-" + Guid.NewGuid().ToString("N")[..8], "deeper");
        var model = NewModel(url => string.Equals(url.Path.TrimEnd(Path.DirectorySeparatorChar), _existing, StringComparison.Ordinal));

        model.SetUrl(new Url(gone));

        Assert.AreEqual(_existing, model.Url.Path.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFolderThatExists_IsOpenedAsAsked()
    {
        var model = NewModel(_ => true);

        model.SetUrl(new Url(_existing));

        Assert.AreEqual(_existing, model.Url.Path.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFolderWithThousandsOfSubfolders_WarmsTheCacheForOnlyTheFirstFew()
    {
        var root = Path.Combine(Path.GetTempPath(), "prefetch_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            for (int i = 0; i < ContentModelFileSystem.MaxPrefetchedChildren * 3; i++)
            {
                Directory.CreateDirectory(Path.Combine(root, "sub" + i));
            }

            var guard = new Mock<IFrequentExecutionGuardService>();
            guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());
            var files = new Mock<IFileSystemService>();
            files.Setup(f => f.ExistsFolder(It.IsAny<Url>())).Returns(true);
            var settings = new Mock<ISettingsService>();
            var tasks = new TaskManager();
            int queued = 0;
            tasks.Tasks.CollectionChanged += (_, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    Interlocked.Add(ref queued, e.NewItems?.Count ?? 0);
                }
            };
            var model = new ContentModelFileSystem(
                "Test", tasks, settings.Object, new FolderHistoryService(settings.Object), files.Object,
                guard.Object, Mock.Of<IMemoryCacheService>(), Serilog.Log.Logger);

            model.SetUrl(new Url(root));
            Thread.Sleep(1_000);

            // The listing itself, the parent, and at most the cap of children.
            Assert.IsTrue(Volatile.Read(ref queued) <= ContentModelFileSystem.MaxPrefetchedChildren + 2,
                $"{queued} listings were queued for a folder with {ContentModelFileSystem.MaxPrefetchedChildren * 3} subfolders.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
