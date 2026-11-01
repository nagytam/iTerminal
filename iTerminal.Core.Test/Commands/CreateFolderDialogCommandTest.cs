using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Serilog;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class CreateFolderDialogCommandTest
{
    private static CreateFolderDialog CreateDialog(IThemeService themeService)
    {
        return new CreateFolderDialog(
            contentModelService: Mock.Of<IContentModelService>(),
            beepService: Mock.Of<IBeepService>(),
            dateTimeProviderService: new DateTimeProviderService(),
            themeService: themeService);
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
    public void Constructor_SetsShiftF7Key()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);
        var contentModelService = Mock.Of<IContentModelService>();

        var command = new CreateFolderDialogCommand(dialog, contentModelService);

        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.F7, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Shift, "plain F7 is Find files");
        Assert.IsFalse(command.Key.Control);
        Assert.IsFalse(command.Key.Alt);
    }

    // ── ExecuteAsync ─────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_WhenContentModelServiceIsNull_DoesNotThrow()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var command = new CreateFolderDialogCommand(dialog, contentModelService: null!);

        // No Control attached, so ShowDialogAsync won't be called — must not throw.
        await command.ExecuteAsync(null);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_SetsBaseUrlFromContentModelService()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);
        var expectedUrl = new Url("file:///tmp/");

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.Url).Returns(expectedUrl);

        var control = new Mock<IControl>();
        control.Setup(c => c.ShowDialogAsync(It.IsAny<iConsole.Dialogs.Dialog>(), It.IsAny<IControl?>()))
            .Returns(Task.CompletedTask);

        var command = new CreateFolderDialogCommand(dialog, contentModelService.Object)
        {
            Control = control.Object
        };

        await command.ExecuteAsync(null);

        Assert.AreEqual(expectedUrl, dialog.BaseUrl);
        control.Verify(c => c.ShowDialogAsync(dialog, It.IsAny<IControl?>()), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_WithoutControl_DoesNotSeedDialogOrThrow()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.Url).Returns(new Url("file:///tmp/"));

        var command = new CreateFolderDialogCommand(dialog, contentModelService.Object);

        // No Control attached: the command is a no-op (previously this threw
        // NullReferenceException from `await Control?.ShowDialogAsync(...)`).
        await command.ExecuteAsync(null);

        Assert.IsNull(dialog.BaseUrl);
    }

    // ── ContentBrowser integration ───────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task ContentBrowser_WithCreateFolderDialog_RegistersCreateFolderDialogCommand()
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
            createFolderDialog: dialog);

        // Provide a ContentModel so OnInitializeCommands doesn't throw.
        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.SetupProperty(m => m.ScrollPosition, new Position(0, 0));
        contentModel.SetupProperty(m => m.CursorPosition, new Position(0, 0));
        contentModel.SetupProperty(m => m.ViewSize, new iConsole.Data.Size(0, 0));
        contentModel.SetupProperty(m => m.ContentBrowser, null!);
        browser.ContentModel = contentModel.Object;

        browser.OnInitializeCommands();

        var hasCommand = browser.Commands.Any(c => c is CreateFolderDialogCommand);
        Assert.IsTrue(hasCommand, "ContentBrowser should register CreateFolderDialogCommand when a CreateFolderDialog is supplied.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ContentBrowser_WithoutCreateFolderDialog_DoesNotRegisterCreateFolderDialogCommand()
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

        var hasCommand = browser.Commands.Any(c => c is CreateFolderDialogCommand);
        Assert.IsFalse(hasCommand, "ContentBrowser should NOT register CreateFolderDialogCommand when no dialog is supplied.");
    }

    // ── FilePanelFactory integration ─────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void FilePanelFactory_WithCreateFolderDialog_CreatesFilePanel()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var taskManager = new Mock<TaskManager>();
        var contentModelService = new Mock<IContentModelService>(MockBehavior.Loose);
        var terminalModel = new TerminalModel(taskManager.Object, contentModelService.Object);
        var memoryCacheService = new Mock<IMemoryCacheService>();
        var settingsService = new Mock<ISettingsService>();
        var logger = Mock.Of<ILogger>();

        var factory = new iTerminal.Core.Factories.FilePanelFactory(
            taskManager.Object,
            contentModelService.Object,
            terminalModel,
            memoryCacheService.Object,
            settingsService.Object,
            logger,
            themeService,
            dialog);

        var panel = factory.Create("PanelWithDialog");

        Assert.IsNotNull(panel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FilePanelFactory_WithoutCreateFolderDialog_CreatesFilePanel()
    {
        var themeService = CreateThemeService();

        var taskManager = new Mock<TaskManager>();
        var contentModelService = new Mock<IContentModelService>(MockBehavior.Loose);
        var terminalModel = new TerminalModel(taskManager.Object, contentModelService.Object);
        var memoryCacheService = new Mock<IMemoryCacheService>();
        var settingsService = new Mock<ISettingsService>();
        var logger = Mock.Of<ILogger>();

        var factory = new iTerminal.Core.Factories.FilePanelFactory(
            taskManager.Object,
            contentModelService.Object,
            terminalModel,
            memoryCacheService.Object,
            settingsService.Object,
            logger,
            themeService);

        var panel = factory.Create("PanelWithoutDialog");

        Assert.IsNotNull(panel);
    }
}
