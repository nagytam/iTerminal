using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Csv;
using iConsole.Json;
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
public class ViewFileDialogCommandTest
{
    private static FileViewerDialog CreateDialog(IThemeService themeService)
    {
        return new FileViewerDialog(
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

    [TestMethod]
    public void Constructor_SetsF3Key()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);
        var contentModelService = Mock.Of<IContentModelService>();

        var command = new ViewFileDialogCommand(dialog, contentModelService);

        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.F3, command.Key!.ConsoleKey);
        Assert.IsFalse(command.Key.Shift);
        Assert.IsFalse(command.Key.Alt);
        Assert.IsFalse(command.Key.Control);
    }

    // ── CanExecute ───────────────────────────────────────────────────────────

    [TestMethod]
    public void CanExecute_WhenFocusedFileIsTextFile_ReturnsTrue()
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
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(fileUrl);
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new ViewFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

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
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(fileUrl);
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new ViewFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenFocusedFileIsHtml_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/index.html");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(fileUrl);
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new ViewFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenFocusedFileIsPdf_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/document.pdf");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(fileUrl);
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new ViewFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenFocusedFileIsPng_ReturnsTrue()
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
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(fileUrl);
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new ViewFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenFocusedFileIsMp3_ReturnsFalse()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/song.mp3");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(fileUrl);
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new ViewFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenFocusedFileIsJpeg_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/photo.jpeg");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(fileUrl);
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new ViewFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenFocusedFileIsBmp_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/image.bmp");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(fileUrl);
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new ViewFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenFocusedFileIsJpg_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/photo.jpg");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(fileUrl);
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new ViewFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenFocusedFileIsGif_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/anim.gif");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(fileUrl);
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new ViewFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenFocusedFileIsWebp_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/image.webp");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(fileUrl);
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new ViewFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenFocusedFileIsTiff_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/scan.tiff");
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.Url).Returns(fileUrl);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        contentModel.Setup(m => m.ExistsFile(fileUrl)).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(fileUrl);
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new ViewFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenFocusedFileUrlIsEmpty_ReturnsFalse()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(new Url(string.Empty));

        var command = new ViewFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_WhenFocusedModelIsNull_ReturnsFalse()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var fileUrl = new Url("file:///tmp/readme.txt");

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(fileUrl);
        contentModelService.Setup(s => s.GetFocusedModel()).Returns((IContentModel?)null);

        var command = new ViewFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }

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
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(fileUrl);
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);

        var command = new ViewFileDialogCommand(dialog, contentModelService.Object);

        Assert.IsFalse(command.CanExecute());
    }

    // ── ExecuteAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_WhenContentModelServiceIsNull_DoesNotThrow()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var command = new ViewFileDialogCommand(dialog, contentModelService: null!);

        // No Control attached — must not throw.
        await command.ExecuteAsync(null);
    }

    // ── Music files ──────────────────────────────────────────────────────────

    private static iTerminal.Core.Dialogs.Music.MusicFileViewerDialog CreateMusicDialog(IThemeService themeService)
    {
        return new iTerminal.Core.Dialogs.Music.MusicFileViewerDialog(
            player: Mock.Of<iTerminal.Core.Services.Music.IMusicPlayerService>(),
            deviceService: Mock.Of<iTerminal.Core.Services.Music.IAudioDeviceService>(),
            metadataService: new iTerminal.Core.Services.Music.MusicMetadataService(),
            beepService: Mock.Of<iConsole.Services.IBeepService>(),
            dateTimeProviderService: Mock.Of<iConsole.Services.IDateTimeProviderService>(),
            themeService: themeService);
    }

    /// <summary>Wires up a focused model whose selected file is <paramref name="path"/>, which must exist on disk.</summary>
    private static IContentModelService CreateServiceForFile(string path)
    {
        var fileUrl = new Url($"file://{path}");
        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.Setup(m => m.ExistsFile(It.IsAny<Url>())).Returns(true);

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.FocusedFileUrl).Returns(fileUrl);
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(contentModel.Object);
        return contentModelService.Object;
    }

    [TestMethod]
    public void CanExecute_WhenFocusedFileIsMp3AndTheMusicViewerIsAvailable_ReturnsTrue()
    {
        var themeService = CreateThemeService();
        var path = Path.Combine(Path.GetTempPath(), $"ViewCommandTest_{Guid.NewGuid():N}.mp3");
        File.WriteAllBytes(path, new byte[64]);

        try
        {
            var command = new ViewFileDialogCommand(
                CreateDialog(themeService),
                CreateServiceForFile(path),
                solutionViewerDialog: null,
                musicFileViewerDialog: CreateMusicDialog(themeService));

            Assert.IsTrue(command.CanExecute(), "F3 must open an audio file when the music viewer is wired up.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void CanExecute_WhenTheAudioFileIsNotLocal_StaysFalse()
    {
        var themeService = CreateThemeService();

        // Nothing at this path: the player decodes from disk, so a remote track has nothing to open.
        var command = new ViewFileDialogCommand(
            CreateDialog(themeService),
            CreateServiceForFile(Path.Combine(Path.GetTempPath(), $"MissingTrack_{Guid.NewGuid():N}.mp3")),
            solutionViewerDialog: null,
            musicFileViewerDialog: CreateMusicDialog(themeService));

        Assert.IsFalse(command.CanExecute());
    }

    [TestMethod]
    public void ExecuteAsync_WithAnAudioFile_PointsTheMusicViewerAtIt()
    {
        var themeService = CreateThemeService();
        var musicDialog = CreateMusicDialog(themeService);
        var path = Path.Combine(Path.GetTempPath(), $"ViewCommandTest_{Guid.NewGuid():N}.mp3");
        File.WriteAllBytes(path, new byte[64]);

        try
        {
            var command = new ViewFileDialogCommand(
                CreateDialog(themeService),
                CreateServiceForFile(path),
                solutionViewerDialog: null,
                musicFileViewerDialog: musicDialog);

            // Control is null here, so the dialog is not actually shown — the routing decision is what matters.
            command.ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.AreEqual(path, musicDialog.FilePath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ExecuteAsync_WithAnXmlFile_PointsTheXmlViewerAtIt()
    {
        var themeService = CreateThemeService();
        var xmlDialog = new XmlViewerDialog(
            Mock.Of<IContentModelService>(),
            Mock.Of<IFileToTextConverterService>(),
            Mock.Of<IBeepService>(),
            new DateTimeProviderService(),
            themeService);
        var path = Path.Combine(Path.GetTempPath(), $"ViewCommandTest_{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, "<root/>");

        try
        {
            var command = new ViewFileDialogCommand(
                CreateDialog(themeService),
                CreateServiceForFile(path),
                solutionViewerDialog: null,
                xmlViewerDialog: xmlDialog);

            command.ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.AreEqual(path, xmlDialog.Url?.Path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ExecuteAsync_WithAJsonFile_PointsTheJsonViewerAtIt()
    {
        var themeService = CreateThemeService();
        var jsonDialog = new JsonViewerDialog(
            Mock.Of<IContentModelService>(),
            Mock.Of<IFileToTextConverterService>(),
            themeService);
        var path = Path.Combine(Path.GetTempPath(), $"ViewCommandTest_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}");

        try
        {
            var command = new ViewFileDialogCommand(
                CreateDialog(themeService),
                CreateServiceForFile(path),
                solutionViewerDialog: null,
                jsonViewerDialog: jsonDialog);

            command.ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.AreEqual(path, jsonDialog.Url?.Path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [DataTestMethod]
    [DataRow(".csv")]
    [DataRow(".tsv")]
    public void ExecuteAsync_WithACsvOrTsvFile_PointsTheCsvViewerAtIt(string extension)
    {
        var themeService = CreateThemeService();
        var csvDialog = new CsvViewerDialog(
            Mock.Of<IContentModelService>(),
            Mock.Of<IFileToTextConverterService>(),
            Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(),
            themeService);
        var path = Path.Combine(Path.GetTempPath(), $"ViewCommandTest_{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, "a,b\n1,2\n");

        try
        {
            var command = new ViewFileDialogCommand(
                CreateDialog(themeService),
                CreateServiceForFile(path),
                solutionViewerDialog: null,
                csvViewerDialog: csvDialog);

            command.ExecuteAsync(null).GetAwaiter().GetResult();

            Assert.AreEqual(path, csvDialog.Url?.Path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── ContentBrowser integration ───────────────────────────────────────────

    [TestMethod]
    public void ContentBrowser_WithFileViewerDialog_RegistersViewFileDialogCommand()
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
            fileViewerDialog: dialog);

        var contentModel = new Mock<IContentModel>(MockBehavior.Loose);
        contentModel.SetupProperty(m => m.ScrollPosition, new Position(0, 0));
        contentModel.SetupProperty(m => m.CursorPosition, new Position(0, 0));
        contentModel.SetupProperty(m => m.ViewSize, new iConsole.Data.Size(0, 0));
        contentModel.SetupProperty(m => m.ContentBrowser, null!);
        browser.ContentModel = contentModel.Object;

        browser.OnInitializeCommands();

        var hasCommand = browser.Commands.Any(c => c is ViewFileDialogCommand);
        Assert.IsTrue(hasCommand, "ContentBrowser should register ViewFileDialogCommand when a FileViewerDialog is supplied.");
    }

    [TestMethod]
    public void ContentBrowser_WithoutFileViewerDialog_DoesNotRegisterViewFileDialogCommand()
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

        var hasCommand = browser.Commands.Any(c => c is ViewFileDialogCommand);
        Assert.IsFalse(hasCommand, "ContentBrowser should NOT register ViewFileDialogCommand when no FileViewerDialog is supplied.");
    }

    // ── FilePanelFactory integration ─────────────────────────────────────────

    [TestMethod]
    public void FilePanelFactory_WithFileViewerDialog_CreatesFilePanel()
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
            fileViewerDialog: dialog);

        var panel = factory.Create("PanelWithViewerDialog");

        Assert.IsNotNull(panel);
    }

    [TestMethod]
    public void FilePanelFactory_WithoutFileViewerDialog_CreatesFilePanel()
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

        var panel = factory.Create("PanelWithoutViewerDialog");

        Assert.IsNotNull(panel);
    }
}
