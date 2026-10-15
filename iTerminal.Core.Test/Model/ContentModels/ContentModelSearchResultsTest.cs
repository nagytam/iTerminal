using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Search;
using Moq;

namespace iTerminal.Core.Test.Model.ContentModels;

/// <summary>search://: the last search's hits as real file rows, following a search still running.</summary>
[TestClass]
public class ContentModelSearchResultsTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateTree()
    {
        _root = Path.Combine(Path.GetTempPath(), "searchpanel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "deep"));
        File.WriteAllText(Path.Combine(_root, "one.log"), "x");
        File.WriteAllText(Path.Combine(_root, "deep", "two.log"), "xy");
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

    private static IFrequentExecutionGuardService ImmediateGuard()
    {
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());
        return guard.Object;
    }

    private async Task<(ContentModelSearchResults Model, FileSearchService Service)> SearchedAsync(string names)
    {
        var service = new FileSearchService();
        await service.StartAsync(new FileSearchQuery(_root, names));
        var model = new ContentModelSearchResults("LeftPanel", new SettingsService(), service, new FileSystemService(), null, ImmediateGuard());
        model.SetUrl(new Url(Container.SEARCH_RESULTS));
        await model.ListingTask;
        return (model, service);
    }

    [TestMethod]
    public async Task HitsAreRealFileRowsNamedRelativeToTheSearch()
    {
        var (model, _) = await SearchedAsync("*.log");

        var rows = model.Entries.AsSnapshot();
        CollectionAssert.AreEqual(new[] { "..", "one.log", Path.Combine("deep", "two.log") }, rows.Select(r => r.DisplayName).ToArray());
        Assert.AreEqual(ContainerType.FileSystem, rows[2].Url.ContainerType, "a hit is the file itself, so F3/F5/F8 work on it");
        Assert.AreEqual(Path.Combine(_root, "deep", "two.log"), rows[2].Url.FullName);
        Assert.AreEqual(_root, rows[0].Url.FullName.TrimEnd(Path.DirectorySeparatorChar), ".. goes back to where the search started");
    }

    [TestMethod]
    public async Task FileOperationsAcceptAHit()
    {
        var (model, _) = await SearchedAsync("*.log");
        model.CursorPosition.Y = 1;

        Assert.IsNotNull(FocusedEntry.ActionableUrl(model), "F5 and F8 are offered on a hit");
        Assert.IsTrue(model.IsReadOnly, "but nothing can be created in the list itself");
    }

    [TestMethod]
    public async Task AHitDeletedSinceTheSearchLeavesTheList()
    {
        var (model, _) = await SearchedAsync("*.log");
        File.Delete(Path.Combine(_root, "one.log"));

        model.Refresh(forceRefresh: true);
        await model.ListingTask;

        CollectionAssert.AreEqual(new[] { "..", Path.Combine("deep", "two.log") }, model.Entries.AsSnapshot().Select(r => r.DisplayName).ToArray());
    }

    [TestMethod]
    public async Task ANewSearchRelistsThePanel()
    {
        var (model, service) = await SearchedAsync("one*");

        await service.StartAsync(new FileSearchQuery(_root, "two*"));
        await WaitAsync(() => model.Entries.AsSnapshot().Any(e => e.DisplayName.EndsWith("two.log")));

        Assert.IsFalse(model.Entries.AsSnapshot().Any(e => e.DisplayName == "one.log"));
        StringAssert.Contains(model.GetHeader(300), "two*");
    }

    [TestMethod]
    public async Task EnterOnAHitGoesToItsFolderWithTheCursorOnIt()
    {
        var (model, _) = await SearchedAsync("two*");
        model.CursorPosition.Y = 1;
        var fileModel = new Mock<IContentModel>();
        fileModel.SetupGet(m => m.Name).Returns("LeftPanel");
        var contentModelService = new Mock<IContentModelService>();
        Url? navigated = null;
        contentModelService.Setup(s => s.SetUrlAsync("LeftPanel", It.IsAny<Url>()))
            .Callback<string, Url>((_, url) => navigated = url)
            .Returns(Task.CompletedTask);
        contentModelService.SetupGet(s => s.ContentModels).Returns([fileModel.Object]);
        var command = new NavigateToFolderCommand(model, contentModelService.Object);

        Assert.IsTrue(command.CanExecute(), "Enter on a hit is not 'run it'");
        await command.ExecuteAsync(null);

        Assert.AreEqual(Path.Combine(_root, "deep"), navigated!.FullName.TrimEnd(Path.DirectorySeparatorChar));
        fileModel.Verify(m => m.RestoreNavigation(It.Is<string>(u => u.EndsWith("two.log")), 0, 0));
    }

    [TestMethod]
    public async Task EnterOnAHitLandsTheRealFilePanelsCursorOnIt()
    {
        var settings = new SettingsService();
        var searchService = new FileSearchService();
        var service = new ContentModelService(
            new iConsole.Tasks.TaskManager(),
            settings,
            new FileSystemService(),
            new MemoryDriveService(),
            Mock.Of<ISshFileSystemService>(),
            Mock.Of<ISmbFileSystemService>(),
            Mock.Of<IIPhoneFileSystemService>(),
            Mock.Of<INetworkProfileService>(),
            Mock.Of<IPasswordManager>(),
            Mock.Of<IArchiveFileSystemService>(),
            new FolderHistoryService(settings),
            ImmediateGuard(),
            new MemoryCacheService(),
            Mock.Of<Serilog.ILogger>(),
            fileSearchService: searchService);
        var browser = new Mock<IContentBrowser>();
        browser.SetupGet(b => b.Name).Returns("LeftPanel");
        browser.Setup(b => b.ChangeModelAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        service.AddContentBrowser("LeftPanel", browser.Object);
        File.WriteAllText(Path.Combine(_root, "deep", "a_first.log"), "listed above the hit");

        await searchService.StartAsync(new FileSearchQuery(_root, "two*"));
        var model = (ContentModelSearchResults)service.AddContentModel("LeftPanel", new Url(Container.SEARCH_RESULTS))!;
        await model.ListingTask;
        model.CursorPosition.Y = 1;

        await new NavigateToFolderCommand(model, service).ExecuteAsync(null);

        var panel = service.ContentModels.Single();
        await WaitAsync(() => panel.GetFocusedEntry()?.Name == "two.log");
        Assert.AreEqual(Path.Combine(_root, "deep"), panel.Url.FullName.TrimEnd(Path.DirectorySeparatorChar));
    }

    [TestMethod]
    public void BeforeAnySearchTheTitleSaysHowToStartOne()
    {
        var model = new ContentModelSearchResults("LeftPanel", new SettingsService(), new FileSearchService(), new FileSystemService(), null, ImmediateGuard());
        model.SetUrl(new Url(Container.SEARCH_RESULTS));

        StringAssert.Contains(model.GetHeader(200), "F7");
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("timed out");
            }

            await Task.Delay(20);
        }
    }
}
