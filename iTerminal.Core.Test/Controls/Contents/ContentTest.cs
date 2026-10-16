#pragma warning disable MSTEST0032 // These tests pin a constant's value on purpose.
using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.AI;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Controls.Terminals;
using iTerminal.Core.Factories;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Serilog;

namespace iTerminal.Core.Test.Controls.Contents;

[TestClass]
public class ContentTest
{
    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(t => t.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private sealed class ContentTestSetup
    {
        public Mock<IContentModelService> ContentModelService { get; init; } = new();
        public FilePanelFactory Factory { get; init; } = null!;
        public AiPromptBar AiPromptBar { get; init; } = null!;
        public TerminalPanel TerminalPanel { get; init; } = null!;
        public IThemeService ThemeService { get; init; } = null!;
        public Content Sut { get; init; } = null!;
    }

    private static ContentTestSetup CreateSetup(int barHeight = 5)
    {
        var themeService = CreateThemeService();
        var taskManager = new Mock<TaskManager>(MockBehavior.Loose).Object;
        var mockService = new Mock<IContentModelService>();
        var terminalModel = new TerminalModel(taskManager, mockService.Object);
        var memoryCacheService = new Mock<IMemoryCacheService>();
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);

        var factory = new FilePanelFactory(
            taskManager,
            mockService.Object,
            terminalModel,
            memoryCacheService.Object,
            settingsService.Object,
            Mock.Of<ILogger>(),
            themeService);

        var aiModel = new AiChatModel();
        var aiService = new Mock<IAiChatService>();
        var aiModelDialog = new iTerminal.Core.Dialogs.AiModelDialog(aiModel, Options.Create(new AiOptions()), themeService);
        var aiPromptBar = new AiPromptBar(aiModel, aiService.Object, aiModelDialog, Mock.Of<IDateTimeProviderService>(), themeService);
        var terminalPanel = new TerminalPanel(terminalModel, mockService.Object, themeService);
        var aiOptions = Options.Create(new AiOptions { BarHeight = barHeight });

        var sut = new Content(factory, aiPromptBar, terminalPanel, mockService.Object, themeService, aiOptions);

        return new ContentTestSetup
        {
            ContentModelService = mockService,
            Factory = factory,
            AiPromptBar = aiPromptBar,
            TerminalPanel = terminalPanel,
            ThemeService = themeService,
            Sut = sut,
        };
    }

    [TestMethod]
    public void Constructor_DoesNotThrow()
    {
        var setup = CreateSetup();
        Assert.IsNotNull(setup.Sut);
    }

    [TestMethod]
    public void Constructor_NameIsContent()
    {
        var setup = CreateSetup();
        Assert.AreEqual("Content", setup.Sut.Name);
    }

    [TestMethod]
    public void Constructor_HasFourControls()
    {
        var setup = CreateSetup();
        // Left panel, right panel, AI prompt bar, terminal panel.
        Assert.AreEqual(4, setup.Sut.Controls.Count);
    }

    [TestMethod]
    public void Constructor_LeftPanelHasSwitchPanelsCommand()
    {
        var setup = CreateSetup();
        var commands = setup.Sut.Commands;
        Assert.IsTrue(commands.Any(c => c is iTerminal.Core.Commands.SwitchPanelsCommand),
            "Expected SwitchPanelsCommand to be registered on Content.");
    }

    [TestMethod]
    public void OnInitializeLayout_EvenWidth_SplitsPanelsEvenly()
    {
        var setup = CreateSetup();
        setup.Sut.ViewSize = new Size(80, 24);
        setup.Sut.OnInitializeLayout();

        var left = setup.Sut.Controls.AsSnapshot()
            .OfType<FilePanel>()
            .First(p => p.Name == Content.Left);
        var right = setup.Sut.Controls.AsSnapshot()
            .OfType<FilePanel>()
            .First(p => p.Name == Content.Right);

        Assert.AreEqual(40u, left.ViewSize.Width);
        Assert.AreEqual(40u, right.ViewSize.Width);
    }

    [TestMethod]
    public void OnInitializeLayout_PanelHeight_IsViewHeightMinusBarHeight()
    {
        const int barHeight = 5;
        var setup = CreateSetup(barHeight);
        setup.Sut.ViewSize = new Size(80, 24);
        setup.Sut.OnInitializeLayout();

        var left = setup.Sut.Controls.AsSnapshot()
            .OfType<FilePanel>()
            .First(p => p.Name == Content.Left);

        Assert.AreEqual((uint)(24 - barHeight), left.ViewSize.Height);
    }

    [TestMethod]
    public void OnInitializeLayout_AiPromptBar_HasBarHeight()
    {
        const int barHeight = 5;
        var setup = CreateSetup(barHeight);
        setup.Sut.ViewSize = new Size(80, 24);
        setup.Sut.OnInitializeLayout();

        Assert.AreEqual((uint)barHeight, setup.AiPromptBar.ViewSize.Height);
    }

    [TestMethod]
    public void OnInitializeLayout_AiPromptBar_IsAtBottomRows()
    {
        const int barHeight = 5;
        var setup = CreateSetup(barHeight);
        setup.Sut.ViewSize = new Size(80, 24);
        setup.Sut.OnInitializeLayout();

        Assert.AreEqual(24 - barHeight, setup.AiPromptBar.Position.Y);
    }

    [TestMethod]
    public void OnInitializeLayout_MinimumSize_DoesNotThrow()
    {
        var setup = CreateSetup();
        setup.Sut.ViewSize = new Size(2, 2);
        setup.Sut.OnInitializeLayout();
    }

    [TestMethod]
    public void Content_Left_ConstantValue()
    {
        Assert.AreEqual("LeftPanel", Content.Left);
    }

    [TestMethod]
    public void Content_Right_ConstantValue()
    {
        Assert.AreEqual("RightPanel", Content.Right);
    }

    [TestMethod]
    public void Constructor_ImplementsIContent()
    {
        var setup = CreateSetup();
        Assert.IsInstanceOfType<IContent>(setup.Sut);
    }
}
