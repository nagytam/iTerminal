using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Services;
using iTerminal.Core.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Serilog;

namespace iTerminal.Core.Test.Controls.Contents;

[TestClass]
public class ItemInformationTest
{
    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(t => t.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private static ItemInformation CreateSut(
        IContentModelService? contentModelService = null,
        IThemeService? themeService = null)
    {
        return new ItemInformation(
            contentModelService ?? new Mock<IContentModelService>().Object,
            new Mock<TaskManager>(MockBehavior.Loose).Object,
            new MemoryCacheService(),
            themeService ?? CreateThemeService(),
            Mock.Of<ILogger>());
    }

    [TestMethod]
    public void Constructor_DoesNotThrow()
    {
        var sut = CreateSut();
        Assert.IsNotNull(sut);
    }

    [TestMethod]
    public void Constructor_CanFocus_IsFalse()
    {
        var sut = CreateSut();
        Assert.IsFalse(sut.CanFocus);
    }

    [TestMethod]
    public void Constructor_IsVisible_IsTrue()
    {
        var sut = CreateSut();
        Assert.IsTrue(sut.IsVisible);
    }

    [TestMethod]
    public void Constructor_ViewSize_IsSingleRow()
    {
        var sut = CreateSut();
        Assert.AreEqual(1u, sut.ViewSize.Height);
    }

    [TestMethod]
    public void Constructor_Text1_IsEmpty()
    {
        var sut = CreateSut();
        Assert.AreEqual(string.Empty, sut.Text1);
    }

    [TestMethod]
    public void Constructor_Text2_IsEmpty()
    {
        var sut = CreateSut();
        Assert.AreEqual(string.Empty, sut.Text2);
    }

    [TestMethod]
    public void Constructor_ContentModel_IsNull()
    {
        var sut = CreateSut();
        Assert.IsNull(sut.ContentModel);
    }

    [TestMethod]
    public void ContentModel_CanBeAssigned()
    {
        var sut = CreateSut();
        var model = new Mock<IContentModel>().Object;

        sut.ContentModel = model;

        Assert.AreSame(model, sut.ContentModel);
    }

    [TestMethod]
    public void ContentModel_AssignSameValue_DoesNotChange()
    {
        var sut = CreateSut();
        var model = new Mock<IContentModel>().Object;
        sut.ContentModel = model;

        // Assign same value a second time – should not throw or change anything.
        sut.ContentModel = model;

        Assert.AreSame(model, sut.ContentModel);
    }

    [TestMethod]
    public void ContentModel_CanBeReassigned_ToDifferentModel()
    {
        var sut = CreateSut();
        var model1 = new Mock<IContentModel>().Object;
        var model2 = new Mock<IContentModel>().Object;

        sut.ContentModel = model1;
        sut.ContentModel = model2;

        Assert.AreSame(model2, sut.ContentModel);
    }

    [TestMethod]
    public void ModelChanged_WhenFocusedEntryIsNull_ClearsTexts()
    {
        var mockService = new Mock<IContentModelService>();
        var themeService = CreateThemeService();
        var sut = new ItemInformation(
            mockService.Object,
            new Mock<TaskManager>(MockBehavior.Loose).Object,
            new MemoryCacheService(),
            themeService,
            Mock.Of<ILogger>());

        var model = new Mock<IContentModel>(MockBehavior.Loose);
        model.Setup(m => m.GetFocusedEntry()).Returns((IEntry?)null);
        sut.ContentModel = model.Object;

        // Seed non-empty values so we can verify they get cleared.
        sut.Text1 = "Something";
        sut.Text2 = "More";

        mockService.Raise(s => s.ModelChanged += null, EventArgs.Empty);

        Assert.AreEqual(string.Empty, sut.Text1);
        Assert.AreEqual(string.Empty, sut.Text2);
    }

    [TestMethod]
    public void ModelChanged_WhenFocusedEntryIsFile_SetsTexts()
    {
        var mockService = new Mock<IContentModelService>();
        var themeService = CreateThemeService();
        var sut = new ItemInformation(
            mockService.Object,
            new Mock<TaskManager>(MockBehavior.Loose).Object,
            new MemoryCacheService(),
            themeService,
            Mock.Of<ILogger>());

        var entry = new Mock<IEntry>(MockBehavior.Loose);
        var fileUrl = new Url(Path.Combine(Path.GetTempPath(), "readme.txt"));
        entry.Setup(e => e.Url).Returns(fileUrl);
        entry.Setup(e => e.GetDisplayName(It.IsAny<int>())).Returns("readme.txt");
        entry.Setup(e => e.GetDisplayFileSize(It.IsAny<int>())).Returns("1 KB");

        var model = new Mock<IContentModel>(MockBehavior.Loose);
        model.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        sut.ContentModel = model.Object;

        mockService.Raise(s => s.ModelChanged += null, EventArgs.Empty);

        Assert.AreEqual("readme.txt", sut.Text1);
        Assert.AreEqual("1 KB", sut.Text2);
    }

    [TestMethod]
    public void ModelChanged_SetsHasStateChanged()
    {
        var mockService = new Mock<IContentModelService>();
        var sut = new ItemInformation(
            mockService.Object,
            new Mock<TaskManager>(MockBehavior.Loose).Object,
            new MemoryCacheService(),
            CreateThemeService(),
            Mock.Of<ILogger>());

        var model = new Mock<IContentModel>(MockBehavior.Loose);
        model.Setup(m => m.GetFocusedEntry()).Returns((IEntry?)null);
        sut.ContentModel = model.Object;

        sut.HasStateChanged = false;

        mockService.Raise(s => s.ModelChanged += null, EventArgs.Empty);

        Assert.IsTrue(sut.HasStateChanged);
    }

    [TestMethod]
    public async Task CursorMove_ToFolder_DebouncesFileInformationRefreshTask()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folderPath);
        var taskManager = new TaskManager();

        try
        {
            var sut = new ItemInformation(
                new Mock<IContentModelService>().Object,
                taskManager,
                new MemoryCacheService(),
                CreateThemeService(),
                Mock.Of<ILogger>())
            {
                ViewSize = new Size(80, 1)
            };

            var entry = new Mock<IEntry>(MockBehavior.Loose);
            entry.SetupGet(e => e.Url).Returns(new Url(folderPath));
            entry.SetupProperty(e => e.NumberOfFiles, null);
            entry.Setup(e => e.GetDisplayName(It.IsAny<int>())).Returns("Folder: temp");
            entry.Setup(e => e.GetDisplayFileSize(It.IsAny<int>())).Returns("Number of files: -");

            var model = new Mock<IContentModel>(MockBehavior.Loose);
            model.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
            sut.ContentModel = model.Object;
            sut.OnInitializeCommands();

            model.Raise(
                m => m.PropertyChanged += null,
                new System.ComponentModel.PropertyChangedEventArgs(nameof(IScrollableModel.CursorPosition)));

            await Task.Delay(100);

            Assert.IsFalse(
                taskManager.Tasks.AsSnapshot().OfType<FileInformationRefreshTask>().Any(),
                "Moving the cursor while scrolling must not immediately start recursive folder information refresh.");
        }
        finally
        {
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, recursive: true);
            }
        }
    }
}
