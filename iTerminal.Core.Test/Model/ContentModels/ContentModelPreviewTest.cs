using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands.Drives;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Preview;
using iTerminal.Core.Test.Helper;
using Moq;

namespace iTerminal.Core.Test.Model.ContentModels;

/// <summary>
/// preview:// against a real <see cref="ContentModelService"/>: the preview follows the other
/// panel's cursor, and Ctrl+F3 opens it in the other panel and gives that panel its folder back.
/// </summary>
[TestClass]
public class ContentModelPreviewTest
{
    private string _root = string.Empty;
    private string _home = string.Empty;

    [TestInitialize]
    public void CreateTree()
    {
        _root = Path.Combine(Path.GetTempPath(), "previewpanel_" + Guid.NewGuid().ToString("N"));
        _home = Path.Combine(_root, "home");
        Directory.CreateDirectory(_home);
        File.WriteAllText(Path.Combine(_root, "first.txt"), "first file body");
        File.WriteAllText(Path.Combine(_root, "second.txt"), "second file body");
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

    private static ContentModelService CreateService(SettingsService settings) => new(
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
        filePreviewService: new FilePreviewService());

    private static void AddBrowser(ContentModelService service, string name)
    {
        var browser = new Mock<IContentBrowser>();
        browser.SetupGet(b => b.Name).Returns(name);
        browser.Setup(b => b.ChangeModelAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        service.AddContentBrowser(name, browser.Object);
    }

    private static async Task WaitAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("timed out: " + because);
            }

            await Task.Delay(20);
        }
    }

    private static string Body(IContentModel model) =>
        string.Join("\n", model.Entries.AsSnapshot().OfType<PreviewLineEntry>().Select(e => e.Text));

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlF3PreviewsInTheOtherPanelAndFollowsTheCursor()
    {
        var settings = new SettingsService();
        var service = CreateService(settings);
        AddBrowser(service, "LeftPanel");
        AddBrowser(service, "RightPanel");
        var left = service.AddContentModel("LeftPanel", new Url(_root + Path.DirectorySeparatorChar))!;
        service.AddContentModel("RightPanel", new Url(_home + Path.DirectorySeparatorChar));
        service.FocusedPanel = "LeftPanel";
        await WaitAsync(() => left.Entries.AsSnapshot().Any(e => e.Name == "second.txt"), "the left folder is listed");

        left.CursorPosition.Y = left.Entries.AsSnapshot().ToList().FindIndex(e => e.Name == "first.txt");
        await new TogglePreviewCommand(service).ExecuteAsync(null);

        var preview = service.ContentModels.Single(m => m.Name == "RightPanel");
        Assert.IsInstanceOfType<ContentModelPreview>(preview);
        await WaitAsync(() => Body(preview).Contains("first file body"), "the file under the cursor is previewed");
        StringAssert.Contains(preview.GetHeader(100), "first.txt");

        left.CursorPosition.Y = left.Entries.AsSnapshot().ToList().FindIndex(e => e.Name == "second.txt");
        await WaitAsync(() => Body(preview).Contains("second file body"), "moving the cursor moves the preview");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlF3AgainGivesThePanelItsFolderBack()
    {
        var settings = new SettingsService();
        var service = CreateService(settings);
        AddBrowser(service, "LeftPanel");
        AddBrowser(service, "RightPanel");
        service.AddContentModel("LeftPanel", new Url(_root + Path.DirectorySeparatorChar));
        var homeUrl = new Url(_home + Path.DirectorySeparatorChar);
        service.AddContentModel("RightPanel", homeUrl);
        service.FocusedPanel = "LeftPanel";
        var command = new TogglePreviewCommand(service);

        await command.ExecuteAsync(null);
        var preview = (ContentModelPreview)service.ContentModels.Single(m => m.Name == "RightPanel");
        await command.ExecuteAsync(null);

        var restored = service.ContentModels.Single(m => m.Name == "RightPanel");
        Assert.IsInstanceOfType<ContentModelFileSystem>(restored);
        Assert.AreEqual(homeUrl.FullUrl, restored.Url.FullUrl);

        // The replaced preview no longer listens: a cursor move must not rebuild it.
        var before = preview.BuildTask;
        service.ContentModels.Single(m => m.Name == "LeftPanel").CursorPosition.Y = 1;
        Assert.AreSame(before, preview.BuildTask);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NothingInAPreviewIsAFileToActOn()
    {
        var preview = new ContentModelPreview("RightPanel", new SettingsService(), Mock.Of<IContentModelService>(), new FilePreviewService(), ImmediateGuard());
        preview.Entries.Add(new PreviewLineEntry("text"));

        Assert.IsNull(iTerminal.Core.Commands.FocusedEntry.ActionableUrl(preview));
        Assert.IsTrue(preview.IsReadOnly);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APreviewRowIsDrawnAcrossThePanelWithoutColumns()
    {
        var row = new PreviewLineEntry("hello").FormatItemName(12, cursor: false, UiTestHelper.TestTheme().CurrentColorScheme, VisualState.Normal);

        Assert.AreEqual("hello     ", row.Text, "padded to the row inside the borders");
    }
}
