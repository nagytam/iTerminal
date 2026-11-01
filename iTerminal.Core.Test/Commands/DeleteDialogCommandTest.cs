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
public class DeleteDialogCommandTest
{
    private static DeleteDialog CreateDialog(IThemeService themeService)
    {
        return new DeleteDialog(
            contentModelService: Mock.Of<IContentModelService>(),
            beepService: Mock.Of<IBeepService>(),
            dateTimeProviderService: new DateTimeProviderService(),
            themeService: themeService,
            taskManager: new Mock<TaskManager>().Object,
            progressWindowFactory: new ProgressWindowFactory(themeService),
            errorPromptService: Mock.Of<IErrorPromptService>(),
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

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_SetsF8Key()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);
        var contentModelService = Mock.Of<IContentModelService>();

        var command = new DeleteDialogCommand(dialog, contentModelService);

        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.F8, command.Key!.ConsoleKey);
        Assert.IsFalse(command.Key.Shift);
        Assert.IsFalse(command.Key.Control);
        Assert.IsFalse(command.Key.Alt);
    }

    // ── CanExecute ───────────────────────────────────────────────────────────

    [Timeout(60_000)]
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

        var command = new DeleteDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [Timeout(60_000)]
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

        var command = new DeleteDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_WhenNoFocusedModel_ReturnsFalse()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns((IContentModel?)null);

        var command = new DeleteDialogCommand(dialog, contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }

    [Timeout(60_000)]
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

        var command = new DeleteDialogCommand(dialog, contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }

    [Timeout(60_000)]
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

        var command = new DeleteDialogCommand(dialog, contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }

    // ── ExecuteAsync ─────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_WhenContentModelServiceIsNull_DoesNotThrow()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var command = new DeleteDialogCommand(dialog, contentModelService: null!);

        // No Control attached, so ShowDialogAsync won't be called — must not throw.
        await command.ExecuteAsync(null);
    }

    // ── ContentBrowser integration ───────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void ContentBrowser_WithDeleteDialog_RegistersDeleteDialogCommand()
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
            deleteDialog: dialog);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.SetupProperty(m => m.ScrollPosition, new Position(0, 0));
        contentModel.SetupProperty(m => m.CursorPosition, new Position(0, 0));
        contentModel.SetupProperty(m => m.ViewSize, new iConsole.Data.Size(0, 0));
        contentModel.SetupProperty(m => m.ContentBrowser, null!);
        browser.ContentModel = contentModel.Object;

        browser.OnInitializeCommands();

        var hasCommand = browser.Commands.Any(c => c is DeleteDialogCommand);
        Assert.IsTrue(hasCommand, "ContentBrowser should register DeleteDialogCommand when a DeleteDialog is supplied.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ContentBrowser_WithoutDeleteDialog_DoesNotRegisterDeleteDialogCommand()
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

        var hasCommand = browser.Commands.Any(c => c is DeleteDialogCommand);
        Assert.IsFalse(hasCommand, "ContentBrowser should NOT register DeleteDialogCommand when no DeleteDialog is supplied.");
    }

    // ── FilePanelFactory integration ─────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void FilePanelFactory_WithDeleteDialog_CreatesFilePanel()
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
            deleteDialog: dialog);

        var panel = factory.Create("PanelWithDeleteDialog");

        Assert.IsNotNull(panel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FilePanelFactory_WithoutDeleteDialog_CreatesFilePanel()
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

        var panel = factory.Create("PanelWithoutDeleteDialog");

        Assert.IsNotNull(panel);
    }

    /// <summary>
    /// The ".." row's URL is the parent folder and the folder exists, so F8 on it used to offer to
    /// delete the folder the user was standing in — with "Recursively delete folder" ticked.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_OnTheParentRow_ReturnsFalse()
    {
        var model = FocusedEntryTest.ModelFocusedOn("..", "file:///tmp/", isFolder: true);
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(model.Object);

        var command = new DeleteDialogCommand(CreateDialog(CreateThemeService()), contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }
}
