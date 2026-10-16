using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Factories;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Serilog;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class MoveDialogCommandTest
{
    private static MoveDialog CreateDialog(IThemeService themeService)
    {
        return new MoveDialog(
            taskManager: Mock.Of<TaskManager>(),
            contentModelService: Mock.Of<IContentModelService>(),
            progressWindowFactory: new ProgressWindowFactory(themeService),
            errorPromptService: Mock.Of<IErrorPromptService>(),
            beepService: Mock.Of<IBeepService>(),
            dateTimeProviderService: new DateTimeProviderService(),
            themeService: themeService,
            fileSystemService: Mock.Of<IFileSystemService>(),
            memoryDriveService: Mock.Of<IMemoryDriveService>(),
            sshFileSystemService: Mock.Of<ISshFileSystemService>(),
            archiveFileSystemService: Mock.Of<IArchiveFileSystemService>());
    }

    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    // ── constructor ──────────────────────────────────────────────────────────

    [TestMethod]
    public void Constructor_SetsShiftF6Key()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);
        var contentModelService = Mock.Of<IContentModelService>();

        var command = new MoveDialogCommand(dialog, contentModelService);

        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.F6, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Shift);
        Assert.IsFalse(command.Key.Alt);
        Assert.IsFalse(command.Key.Control);
    }

    // ── CanExecute ───────────────────────────────────────────────────────────

    [TestMethod]
    public void CanExecute_WhenFocusedEntryIsFile_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/test.txt");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);
        contentModel.Setup(m => m.ExistsFolder(fileUrl)).Returns(false);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new MoveDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenFocusedEntryIsFolder_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var folderUrl = new Url("file:///tmp/myfolder/");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(folderUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(folderUrl)).Returns(false);
        contentModel.Setup(m => m.ExistsFolder(folderUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new MoveDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenNoFocusedModel_ReturnsFalse()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns((IContentModel?)null);

        var command = new MoveDialogCommand(dialog, contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenFocusedEntryUrlIsNull_ReturnsFalse()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns((Url)null!);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new MoveDialogCommand(dialog, contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenEntryExistsNeitherAsFileNorFolder_ReturnsFalse()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var url = new Url("file:///tmp/ghost");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(url);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(url)).Returns(false);
        contentModel.Setup(m => m.ExistsFolder(url)).Returns(false);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new MoveDialogCommand(dialog, contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }

    // ── ExecuteAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_WhenContentModelServiceIsNull_DoesNotThrow()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var command = new MoveDialogCommand(dialog, contentModelService: null!);

        // No Control attached, so ShowDialogAsync won't be called — must not throw.
        await command.ExecuteAsync(null);
    }

    // ── ContentBrowser integration ───────────────────────────────────────────

    [TestMethod]
    public void ContentBrowser_WithMoveDialog_RegistersMoveDialogCommand()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var contentModelService = new Mock<IContentModelService>(MockBehavior.Loose);
        var taskManager = new Mock<TaskManager>();
        var terminalModel = new TerminalModel(taskManager.Object, contentModelService.Object);

        var browser = new ContentBrowser(
            name: "TestBrowser",
            contentModelService: contentModelService.Object,
            terminalModel: terminalModel,
            taskManager: taskManager.Object,
            themeService: themeService,
            moveDialog: dialog);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.SetupProperty(m => m.ScrollPosition, new Position(0, 0));
        contentModel.SetupProperty(m => m.CursorPosition, new Position(0, 0));
        contentModel.SetupProperty(m => m.ViewSize, new iConsole.Data.Size(0, 0));
        contentModel.SetupProperty(m => m.ContentBrowser, null!);
        browser.ContentModel = contentModel.Object;

        browser.OnInitializeCommands();

        var hasCommand = browser.Commands.Any(c => c is MoveDialogCommand);
        Assert.IsTrue(hasCommand, "ContentBrowser should register MoveDialogCommand when a MoveDialog is supplied.");
    }

    [TestMethod]
    public void ContentBrowser_WithoutMoveDialog_DoesNotRegisterMoveDialogCommand()
    {
        var themeService = CreateThemeService();

        var contentModelService = new Mock<IContentModelService>(MockBehavior.Loose);
        var taskManager = new Mock<TaskManager>();
        var terminalModel = new TerminalModel(taskManager.Object, contentModelService.Object);

        var browser = new ContentBrowser(
            name: "TestBrowser",
            contentModelService: contentModelService.Object,
            terminalModel: terminalModel,
            taskManager: taskManager.Object,
            themeService: themeService);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.SetupProperty(m => m.ScrollPosition, new Position(0, 0));
        contentModel.SetupProperty(m => m.CursorPosition, new Position(0, 0));
        contentModel.SetupProperty(m => m.ViewSize, new iConsole.Data.Size(0, 0));
        contentModel.SetupProperty(m => m.ContentBrowser, null!);
        browser.ContentModel = contentModel.Object;

        browser.OnInitializeCommands();

        var hasCommand = browser.Commands.Any(c => c is MoveDialogCommand);
        Assert.IsFalse(hasCommand, "ContentBrowser should NOT register MoveDialogCommand when no MoveDialog is supplied.");
    }

    // ── FilePanelFactory integration ─────────────────────────────────────────

    [TestMethod]
    public void FilePanelFactory_WithMoveDialog_CreatesFilePanel()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var taskManager = new Mock<TaskManager>();
        var contentModelService = new Mock<IContentModelService>(MockBehavior.Loose);
        var terminalModel = new TerminalModel(taskManager.Object, contentModelService.Object);
        var memoryCacheService = new Mock<IMemoryCacheService>();
        var settingsService = new Mock<ISettingsService>();
        var logger = Mock.Of<ILogger>();

        var factory = new FilePanelFactory(
            taskManager.Object,
            contentModelService.Object,
            terminalModel,
            memoryCacheService.Object,
            settingsService.Object,
            logger,
            themeService,
            moveDialog: dialog);

        var panel = factory.Create("PanelWithMoveDialog");

        Assert.IsNotNull(panel);
    }

    [TestMethod]
    public void FilePanelFactory_WithoutMoveDialog_CreatesFilePanel()
    {
        var themeService = CreateThemeService();

        var taskManager = new Mock<TaskManager>();
        var contentModelService = new Mock<IContentModelService>(MockBehavior.Loose);
        var terminalModel = new TerminalModel(taskManager.Object, contentModelService.Object);
        var memoryCacheService = new Mock<IMemoryCacheService>();
        var settingsService = new Mock<ISettingsService>();
        var logger = Mock.Of<ILogger>();

        var factory = new FilePanelFactory(
            taskManager.Object,
            contentModelService.Object,
            terminalModel,
            memoryCacheService.Object,
            settingsService.Object,
            logger,
            themeService);

        var panel = factory.Create("PanelWithoutMoveDialog");

        Assert.IsNotNull(panel);
    }
}
