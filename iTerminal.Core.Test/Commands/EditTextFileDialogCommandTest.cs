using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Factories;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Serilog;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class EditTextFileDialogCommandTest
{
    private static TextFileEditorDialog CreateDialog(IThemeService themeService)
    {
        return new TextFileEditorDialog(
            contentModelService: Mock.Of<IContentModelService>(),
            fileToTextConverterService: Mock.Of<IFileToTextConverterService>(),
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
    public void Constructor_SetsF4Key()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);
        var contentModelService = Mock.Of<IContentModelService>();

        var command = new EditTextFileDialogCommand(dialog, contentModelService);

        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.F4, command.Key!.ConsoleKey);
        Assert.IsFalse(command.Key.Shift);
        Assert.IsFalse(command.Key.Alt);
        Assert.IsFalse(command.Key.Control);
    }

    // ── CanExecute ───────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_WhenFocusedFileIsTxt_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/readme.txt");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new EditTextFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_WhenFocusedFileIsJson_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/config.json");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new EditTextFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_WhenFocusedFileIsCs_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/Program.cs");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new EditTextFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_WhenFocusedFileIsYaml_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/pipeline.yaml");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new EditTextFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_WhenFocusedFileIsXml_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/settings.xml");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new EditTextFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_WhenFocusedFileHasNoExtension_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/Makefile");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        // Simulate empty extension
        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new EditTextFileDialogCommand(dialog, contentModelService.Object);

        // Url.Extension for a file with no extension returns "" which IS supported
        Assert.IsTrue(command.CanExecute());
    }

    /// <summary>F4 on these did nothing but beep: the list stopped at a few .NET and web formats.</summary>
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("README.md")]
    [DataRow("settings.ini")]
    [DataRow("build.ps1")]
    [DataRow("query.sql")]
    [DataRow("script.py")]
    [DataRow("Directory.Build.props")]
    [DataRow("app.JS")]
    public void CanExecute_WhenFocusedFileIsCommonText_ReturnsTrue(string fileName)
    {
        var dialog = CreateDialog(CreateThemeService());
        var fileUrl = new Url("file:///tmp/" + fileName);
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);
        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new EditTextFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute(), fileName);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_WhenFocusedFileIsPng_ReturnsFalse()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/image.png");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new EditTextFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_WhenFocusedFileIsMp3_ReturnsFalse()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/track.mp3");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new EditTextFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_WhenFocusedModelIsNull_ReturnsFalse()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns((IContentModel?)null);

        var command = new EditTextFileDialogCommand(dialog, contentModelService.Object);

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

        var command = new EditTextFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_WhenFileDoesNotExistInModel_ReturnsFalse()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/readme.txt");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(false);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new EditTextFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }

    // ── ExecuteAsync ─────────────────────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_WhenContentModelServiceIsNull_DoesNotThrow()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var command = new EditTextFileDialogCommand(dialog, contentModelService: null!);

        // No Control attached — must not throw.
        await command.ExecuteAsync(null);
    }

    // ── ContentBrowser integration ───────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void ContentBrowser_WithTextFileEditorDialog_RegistersEditTextFileDialogCommand()
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
            textFileEditorDialog: dialog);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.SetupProperty(m => m.ScrollPosition, new Position(0, 0));
        contentModel.SetupProperty(m => m.CursorPosition, new Position(0, 0));
        contentModel.SetupProperty(m => m.ViewSize, new iConsole.Data.Size(0, 0));
        contentModel.SetupProperty(m => m.ContentBrowser, null!);
        browser.ContentModel = contentModel.Object;

        browser.OnInitializeCommands();

        var hasCommand = browser.Commands.Any(c => c is EditTextFileDialogCommand);
        Assert.IsTrue(hasCommand, "ContentBrowser should register EditTextFileDialogCommand when a TextFileEditorDialog is supplied.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ContentBrowser_WithoutTextFileEditorDialog_DoesNotRegisterEditTextFileDialogCommand()
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

        var hasCommand = browser.Commands.Any(c => c is EditTextFileDialogCommand);
        Assert.IsFalse(hasCommand, "ContentBrowser should NOT register EditTextFileDialogCommand when no TextFileEditorDialog is supplied.");
    }

    // ── FilePanelFactory integration ─────────────────────────────────────────

    [Timeout(60_000)]
    [TestMethod]
    public void FilePanelFactory_WithTextFileEditorDialog_CreatesFilePanel()
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
            textFileEditorDialog: dialog);

        var panel = factory.Create("PanelWithEditorDialog");

        Assert.IsNotNull(panel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FilePanelFactory_WithoutTextFileEditorDialog_CreatesFilePanel()
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

        var panel = factory.Create("PanelWithoutEditorDialog");

        Assert.IsNotNull(panel);
    }
}
