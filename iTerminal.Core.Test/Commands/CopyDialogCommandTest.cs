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
public class CopyDialogCommandTest
{
    private static CopyDialog CreateDialog(IThemeService themeService)
    {
        return new CopyDialog(
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
    public void Constructor_SetsF5Key()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);
        var contentModelService = Mock.Of<IContentModelService>();

        var command = new CopyDialogCommand(dialog, contentModelService);

        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.F5, command.Key!.ConsoleKey);
        Assert.IsFalse(command.Key.Shift);
        Assert.IsFalse(command.Key.Control);
        Assert.IsFalse(command.Key.Alt);
    }

    // ── CanExecute ───────────────────────────────────────────────────────────

    [TestMethod]
    public void CanExecute_WhenFocusedEntryIsFile_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/test.txt");
        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(new Mock<IEntry>().Object);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(fileUrl);

        // Simulate ExistsFile returning true for the focused url
        var focusedEntry = new Mock<IEntry>();
        focusedEntry.Setup(e => e.Url).Returns(fileUrl);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(focusedEntry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var command = new CopyDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenNoFocusedModel_ReturnsFalse()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns((IContentModel?)null);

        var command = new CopyDialogCommand(dialog, contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenFocusedEntryUrlIsNull_ReturnsFalse()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns((Url)null!);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new CopyDialogCommand(dialog, contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }

    // ── ExecuteAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_WhenContentModelServiceIsNull_DoesNotThrow()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var command = new CopyDialogCommand(dialog, contentModelService: null!);

        // No Control attached, so ShowDialogAsync won't be called — must not throw.
        await command.ExecuteAsync(null);
    }

    // ── ContentBrowser integration ───────────────────────────────────────────

    [TestMethod]
    public void ContentBrowser_WithCopyDialog_RegistersCopyDialogCommand()
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
            copyDialog: dialog);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.SetupProperty(m => m.ScrollPosition, new Position(0, 0));
        contentModel.SetupProperty(m => m.CursorPosition, new Position(0, 0));
        contentModel.SetupProperty(m => m.ViewSize, new iConsole.Data.Size(0, 0));
        contentModel.SetupProperty(m => m.ContentBrowser, null!);
        browser.ContentModel = contentModel.Object;

        browser.OnInitializeCommands();

        var hasCommand = browser.Commands.Any(c => c is CopyDialogCommand);
        Assert.IsTrue(hasCommand, "ContentBrowser should register CopyDialogCommand when a CopyDialog is supplied.");
    }

    [TestMethod]
    public void ContentBrowser_WithoutCopyDialog_DoesNotRegisterCopyDialogCommand()
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

        var hasCommand = browser.Commands.Any(c => c is CopyDialogCommand);
        Assert.IsFalse(hasCommand, "ContentBrowser should NOT register CopyDialogCommand when no CopyDialog is supplied.");
    }

    // ── FilePanelFactory integration ─────────────────────────────────────────

    [TestMethod]
    public void FilePanelFactory_WithCopyDialog_CreatesFilePanel()
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
            copyDialog: dialog);

        var panel = factory.Create("PanelWithCopyDialog");

        Assert.IsNotNull(panel);
    }

    [TestMethod]
    public void FilePanelFactory_WithoutCopyDialog_CreatesFilePanel()
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

        var panel = factory.Create("PanelWithoutCopyDialog");

        Assert.IsNotNull(panel);
    }

    /// <summary>F5 used to accept files only, so a folder could not be copied with any key.</summary>
    [TestMethod]
    public void CanExecute_WhenFocusedEntryIsFolder_ReturnsTrue()
    {
        var model = FocusedEntryTest.ModelFocusedOn("alpha", "file:///tmp/alpha/", isFolder: true);
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(model.Object);

        var command = new CopyDialogCommand(CreateDialog(CreateThemeService()), contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_OnTheParentRow_ReturnsFalse()
    {
        var model = FocusedEntryTest.ModelFocusedOn("..", "file:///tmp/", isFolder: true);
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(model.Object);

        var command = new CopyDialogCommand(CreateDialog(CreateThemeService()), contentModelService.Object);

        Assert.IsFalse(command.CanExecute(), "copying .. would copy the whole parent folder");
    }
}
