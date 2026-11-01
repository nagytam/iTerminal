using iTerminal.Core.Services.Search;

namespace iTerminal.Core.Test.Services.Search;

/// <summary>The search holder the dialog and the search:// panel share.</summary>
[TestClass]
public class FileSearchServiceTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateTree()
    {
        _root = Path.Combine(Path.GetTempPath(), "searchsvc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        for (var i = 0; i < 5; i++)
        {
            File.WriteAllText(Path.Combine(_root, $"file{i}.txt"), "x");
        }
    }

    [TestCleanup]
    public void DeleteTree()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASearchCollectsItsHitsAndSaysHowItWent()
    {
        var service = new FileSearchService();
        var changes = 0;
        service.ResultsChanged += (_, _) => Interlocked.Increment(ref changes);

        await service.StartAsync(new FileSearchQuery(_root, "*.txt"));

        Assert.AreEqual(5, service.Hits.Count);
        Assert.IsFalse(service.IsRunning);
        Assert.AreEqual("Found 5 in 5 files, 1 folder.", service.StatusMessage);
        Assert.IsTrue(changes >= 2, "once at the start and once at the end, at least");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ANewSearchReplacesTheOldResults()
    {
        var service = new FileSearchService();
        await service.StartAsync(new FileSearchQuery(_root, "*.txt"));

        await service.StartAsync(new FileSearchQuery(_root, "file3*"));

        Assert.AreEqual("file3.txt", Path.GetFileName(service.Hits.Single().Path));
        Assert.AreEqual("file3*", service.Query!.NamePatterns);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedSearchIsReportedNotThrown()
    {
        var service = new FileSearchService();

        await service.StartAsync(new FileSearchQuery(Path.Combine(_root, "missing"), "*"));

        StringAssert.StartsWith(service.StatusMessage, "Search failed");
        Assert.IsFalse(service.IsRunning);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NothingFoundIsSaidPlainly()
    {
        Assert.AreEqual("Nothing found in 12 files, 3 folders.", FileSearchService.Describe(new FileSearchProgress(3, 12, 0, string.Empty)));
    }
}
