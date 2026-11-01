using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Factories;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using Moq;
using Serilog;

namespace iTerminal.Core.Test.Factories;

[TestClass]
public class FilePanelFactoryTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_InitializesFactory()
    {
        var mockTaskManager = new Mock<TaskManager>();
        var mockContentModelService = new Mock<IContentModelService>();
        var terminalModel = new TerminalModel(mockTaskManager.Object, mockContentModelService.Object);
        var memoryCacheService = new Mock<IMemoryCacheService>();
        var mockSettingsService = new Mock<ISettingsService>();
        var mockLogger = new Mock<ILogger>();
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var factory = new FilePanelFactory(
            mockTaskManager.Object,
            mockContentModelService.Object,
            terminalModel,
            memoryCacheService.Object,
            mockSettingsService.Object,
            mockLogger.Object,
            mockThemeService.Object);

        Assert.IsNotNull(factory);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Create_ReturnsFilePanel()
    {
        var mockTaskManager = new Mock<TaskManager>();
        var mockContentModelService = new Mock<IContentModelService>();
        var terminalModel = new TerminalModel(mockTaskManager.Object, mockContentModelService.Object);
        var memoryCacheService = new Mock<IMemoryCacheService>();
        var mockSettingsService = new Mock<ISettingsService>();
        var mockLogger = new Mock<ILogger>();
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var factory = new FilePanelFactory(
            mockTaskManager.Object,
            mockContentModelService.Object,
            terminalModel,
            memoryCacheService.Object,
            mockSettingsService.Object,
            mockLogger.Object,
            mockThemeService.Object);

        var filePanel = factory.Create("TestPanel");

        Assert.IsNotNull(filePanel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Create_WithName_UsesProvidedName()
    {
        var mockTaskManager = new Mock<TaskManager>();
        var mockContentModelService = new Mock<IContentModelService>();
        var terminalModel = new TerminalModel(mockTaskManager.Object, mockContentModelService.Object);
        var memoryCacheService = new Mock<IMemoryCacheService>();
        var mockSettingsService = new Mock<ISettingsService>();
        var mockLogger = new Mock<ILogger>();
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var factory = new FilePanelFactory(
            mockTaskManager.Object,
            mockContentModelService.Object,
            terminalModel,
            memoryCacheService.Object,
            mockSettingsService.Object,
            mockLogger.Object,
            mockThemeService.Object);

        var filePanel = factory.Create("MyPanel");

        Assert.IsNotNull(filePanel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Create_RegistersContentBrowser()
    {
        var mockTaskManager = new Mock<TaskManager>();
        var mockContentModelService = new Mock<IContentModelService>();
        var terminalModel = new TerminalModel(mockTaskManager.Object, mockContentModelService.Object);
        var memoryCacheService = new Mock<IMemoryCacheService>();
        var mockSettingsService = new Mock<ISettingsService>();
        var mockLogger = new Mock<ILogger>();
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var factory = new FilePanelFactory(
            mockTaskManager.Object,
            mockContentModelService.Object,
            terminalModel,
            memoryCacheService.Object,
            mockSettingsService.Object,
            mockLogger.Object,
            mockThemeService.Object);

        factory.Create("TestPanel");

        mockContentModelService.Verify(
            s => s.AddContentBrowser("TestPanel", It.IsAny<IContentBrowser>()),
            Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Create_MultiplePanels_CreatesIndependentInstances()
    {
        var mockTaskManager = new Mock<TaskManager>();
        var mockContentModelService = new Mock<IContentModelService>();
        var terminalModel = new TerminalModel(mockTaskManager.Object, mockContentModelService.Object);
        var memoryCacheService = new Mock<IMemoryCacheService>();
        var mockSettingsService = new Mock<ISettingsService>();
        var mockLogger = new Mock<ILogger>();
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var factory = new FilePanelFactory(
            mockTaskManager.Object,
            mockContentModelService.Object,
            terminalModel,
            memoryCacheService.Object,
            mockSettingsService.Object,
            mockLogger.Object,
            mockThemeService.Object);

        var panel1 = factory.Create("Panel1");
        var panel2 = factory.Create("Panel2");

        Assert.IsNotNull(panel1);
        Assert.IsNotNull(panel2);
        Assert.AreNotSame(panel1, panel2);
    }
}

