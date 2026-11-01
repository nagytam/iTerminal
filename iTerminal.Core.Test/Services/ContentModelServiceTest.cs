using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Services;
using Moq;
using Serilog;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class ContentModelServiceTest
{
    private static ContentModelService CreateSut()
    {
        var taskManager = new Mock<TaskManager>().Object;
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(It.IsAny<string>())).Returns(Directory.GetCurrentDirectory());
        var fileSystem = new Mock<IFileSystemService>();
        fileSystem.Setup(f => f.ExistsFolder(It.IsAny<Url>())).Returns(true);
        var memoryDrive = new Mock<IMemoryDriveService>();
        var sshFs = new Mock<ISshFileSystemService>();
        var folderHistory = new Mock<FolderHistoryService>(settings.Object);
        var guardService = new Mock<IFrequentExecutionGuardService>();
        var cache = new Mock<IMemoryCacheService>();
        var logger = Mock.Of<ILogger>();

        return new ContentModelService(
            taskManager,
            settings.Object,
            fileSystem.Object,
            memoryDrive.Object,
            sshFs.Object,
            Mock.Of<ISmbFileSystemService>(),
            Mock.Of<IIPhoneFileSystemService>(),
            Mock.Of<INetworkProfileService>(),
            Mock.Of<IPasswordManager>(),
            Mock.Of<IArchiveFileSystemService>(),
            folderHistory.Object,
            guardService.Object,
            cache.Object,
            logger);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_InitializesContentModels()
    {
        var svc = CreateSut();
        Assert.IsNotNull(svc.ContentModels);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_InitializesContentBrowsers()
    {
        var svc = CreateSut();
        Assert.IsNotNull(svc.ContentBrowsers);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_InitializesCursorPositions()
    {
        var svc = CreateSut();
        Assert.IsNotNull(svc.CursorPositions);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AddContentModel_FileSystem_AddsModel()
    {
        var svc = CreateSut();
        var model = svc.AddContentModel("panel1", new Url(Directory.GetCurrentDirectory()));
        Assert.IsNotNull(model);
        Assert.AreEqual(1, svc.ContentModels.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AddContentModel_SameName_ReplacesModel()
    {
        var svc = CreateSut();
        svc.AddContentModel("panel1", new Url(Directory.GetCurrentDirectory()));
        svc.AddContentModel("panel1", new Url(Directory.GetCurrentDirectory()));
        Assert.AreEqual(1, svc.ContentModels.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetModel_ReturnsCorrectModel()
    {
        var svc = CreateSut();
        svc.AddContentModel("panel1", new Url(Directory.GetCurrentDirectory()));
        var model = svc.GetModel("panel1");
        Assert.IsNotNull(model);
        Assert.AreEqual("panel1", model.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetModel_ReturnsNull_ForUnknownName()
    {
        var svc = CreateSut();
        Assert.IsNull(svc.GetModel("unknown"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetLastContentModel_ReturnsNull_WhenNoLastFocused()
    {
        var svc = CreateSut();
        Assert.IsNull(svc.GetLastContentModel());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetLastContentModel_ReturnsModel_WhenLastFocusedSet()
    {
        var svc = CreateSut();
        svc.AddContentModel("panel1", new Url(Directory.GetCurrentDirectory()));
        svc.LastFocusedPanel = "panel1";
        Assert.IsNotNull(svc.GetLastContentModel());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FocusedPanel_CanBeSetAndRead()
    {
        var svc = CreateSut();
        svc.FocusedPanel = "panel1";
        Assert.AreEqual("panel1", svc.FocusedPanel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ModelChanged_IsRaisedOnAddContentModel()
    {
        var svc = CreateSut();
        bool raised = false;
        svc.ModelChanged += (_, _) => raised = true;

        svc.AddContentModel("panel1", new Url(Directory.GetCurrentDirectory()));

        Assert.IsTrue(raised);
    }

    private static Moq.Mock<iConsole.Interfaces.IContentModel> ModelAt(string url)
    {
        var model = new Moq.Mock<iConsole.Interfaces.IContentModel>();
        model.Setup(m => m.Url).Returns(new iConsole.Data.Url(url));
        return model;
    }

    /// <summary>
    /// Going up lands on the folder that was left. Ctrl+PgUp never did: the position was a row
    /// index keyed by a URL string that its trailing-separator parent URL did not match.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void GoingUpOneLevel_RestoresTheCursorToTheFolderLeft()
    {
        var model = ModelAt("file:///tmp/left/");

        ContentModelService.LandOnTheFolderLeft(model.Object, new iConsole.Data.Url("file:///tmp/left/alpha/"), 3);

        model.Verify(m => m.RestoreNavigation(Moq.It.Is<string>(u => u.TrimEnd('/') == "file:///tmp/left/alpha"), 3, 0), Moq.Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GoingUpSeveralLevels_RestoresTheCursorToTheChildOnTheWay()
    {
        var model = ModelAt("file:///tmp/");

        ContentModelService.LandOnTheFolderLeft(model.Object, new iConsole.Data.Url("file:///tmp/left/alpha/"), 0);

        model.Verify(m => m.RestoreNavigation(Moq.It.Is<string>(u => u.TrimEnd('/') == "file:///tmp/left"), 0, 0), Moq.Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GoingDownOrSideways_LeavesTheCursorAlone()
    {
        var model = ModelAt("file:///tmp/left/alpha/");

        ContentModelService.LandOnTheFolderLeft(model.Object, new iConsole.Data.Url("file:///tmp/left/"), 0);
        ContentModelService.LandOnTheFolderLeft(model.Object, new iConsole.Data.Url("file:///tmp/left/alphabet/"), 0);
        ContentModelService.LandOnTheFolderLeft(model.Object, null, 0);

        model.Verify(m => m.RestoreNavigation(Moq.It.IsAny<string>(), Moq.It.IsAny<long>(), Moq.It.IsAny<long>()), Moq.Times.Never);
    }

    /// <summary>A panel model whose URL follows SetUrl and whose listing is empty, as it is mid-navigation.</summary>
    private static Mock<IContentModel> NavigatingModel(string url)
    {
        var current = new iConsole.Data.Url(url);
        var model = new Mock<IContentModel>();
        model.Setup(m => m.Name).Returns("LeftPanel");
        model.Setup(m => m.Url).Returns(() => current);
        model.Setup(m => m.SetUrl(Moq.It.IsAny<iConsole.Data.Url>())).Callback<iConsole.Data.Url>(u => current = u);
        model.SetupProperty(m => m.CursorPosition, new Position(0, 0));
        model.SetupProperty(m => m.ScrollableSize, new Size(0, 0));
        return model;
    }

    /// <summary>
    /// The remembered row used to be written into the cursor as <c>ScrollableSize.Height - 1</c>:
    /// -1 while the listing is momentarily empty, which tripped Position's assertion and ended the
    /// key handler before the restore to the folder left was asked for — Ctrl+PgUp then stayed on "..".
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public async Task GoingUpWithAnEmptyListingStillLandsOnTheFolderLeft()
    {
        var svc = CreateSut();
        var model = NavigatingModel("file:///tmp/left/beta/");
        svc.ContentModels.Add(model.Object);
        svc.CursorPositions["file:///tmp/left"] = 2;
        svc.CursorPositions["file:///tmp/left/"] = 2;

        await svc.SetUrlAsync("LeftPanel", new iConsole.Data.Url("file:///tmp/left/"));

        Assert.AreEqual(0, model.Object.CursorPosition.Y, "the cursor is not written before the listing is there");
        model.Verify(m => m.RestoreNavigation(Moq.It.Is<string>(u => u.TrimEnd('/') == "file:///tmp/left/beta"), 2, 0), Moq.Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARememberedRowIsHandedToTheModelNotWrittenIntoTheCursor()
    {
        var svc = CreateSut();
        var model = NavigatingModel("file:///tmp/left/");
        svc.ContentModels.Add(model.Object);
        var other = new iConsole.Data.Url("file:///tmp/other/");
        svc.CursorPositions[other.FullUrl] = 4;

        await svc.SetUrlAsync("LeftPanel", other);

        Assert.AreEqual(0, model.Object.CursorPosition.Y);
        model.Verify(m => m.RestoreNavigation(null, 4, 0), Moq.Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFolderWithAndWithoutItsTrailingSeparatorIsTheSameLocation()
    {
        Assert.IsTrue(iTerminal.Core.Model.ContentModels.ContentModelBase.IsSameLocation("file://C:\\a\\b\\", "file://C:\\a\\b"));
        Assert.IsFalse(iTerminal.Core.Model.ContentModels.ContentModelBase.IsSameLocation("file://C:\\a\\b", "file://C:\\a\\bc"));
    }
}
