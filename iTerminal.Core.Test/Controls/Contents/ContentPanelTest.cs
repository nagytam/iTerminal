using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Controls.Contents;

[TestClass]
public class ContentPanelTest
{
    private sealed class TestContentBrowser(IThemeService themeService) : ContentBrowser(
        name: "TestBrowser",
        contentModelService: Mock.Of<IContentModelService>(),
        terminalModel: new Mock<iTerminal.Core.Model.TerminalModel>(
            MockBehavior.Loose,
            Mock.Of<TaskManager>(),
            Mock.Of<IContentModelService>()).Object,
        taskManager: Mock.Of<TaskManager>(),
        themeService: themeService)
    {
        public new void SetFocus() => base.SetFocus();
        public new void ResetFocus() => base.ResetFocus();
    }

    private sealed class TestItemInformation(IThemeService themeService) : ItemInformation(
        contentModelService: Mock.Of<IContentModelService>(),
        taskManager: Mock.Of<TaskManager>(),
        memoryCacheService: new MemoryCacheService(),
        themeService: themeService,
        logger: Mock.Of<Serilog.ILogger>())
    {
    }

    private sealed class TestContentPanel : ContentPanel
    {
        public TestContentPanel(
            ISettingsService settingsService,
            ContentBrowser contentBrowser,
            ItemInformation itemInformation,
            IContentModelService contentModelService,
            IThemeService themeService)
            : base(settingsService, contentBrowser, itemInformation, contentModelService, themeService)
        {
        }

        public IContentModel? Model => ContentModel;
    }

    private static Mock<IContentModel> CreateContentModelMock()
    {
        Mock<IContentModel> model = new(MockBehavior.Loose);
        model.SetupProperty(contentModel => contentModel.ViewSize, new Size(0, 0));
        model.SetupProperty(contentModel => contentModel.ContentBrowser, null!);
        return model;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnInitializeAsync_WithoutName_ThrowsInvalidOperationException()
    {
        TestContentPanel panel = new(
            NullSettingsService.Instance,
            new TestContentBrowser(new ThemeService()),
            new TestItemInformation(new ThemeService()),
            Mock.Of<IContentModelService>(),
            new ThemeService())
        {
            Name = string.Empty,
            ViewSize = new Size(80, 20),
        };

        try
        {
            await panel.OnInitializeAsync();
            Assert.Fail("Expected InvalidOperationException.");
        }
        catch (InvalidOperationException)
        {
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnInitializeAsync_AssignsCreatedContentModelToBrowserAndFooter()
    {
        ThemeService themeService = new();
        TestContentBrowser browser = new(themeService);
        TestItemInformation itemInformation = new(themeService);
        Mock<ISettingsService> settingsService = new();
        settingsService.Setup(service => service.Get("Panel")).Returns("file:///");
        Mock<IContentModelService> contentModelService = new();
        Mock<IContentModel> contentModel = CreateContentModelMock();
        contentModelService.Setup(service => service.AddContentModel("Panel", It.IsAny<Url>())).Returns(contentModel.Object);
        TestContentPanel panel = new(settingsService.Object, browser, itemInformation, contentModelService.Object, themeService)
        {
            Name = "Panel",
            ViewSize = new Size(80, 20),
        };

        await panel.OnInitializeAsync();

        Assert.AreSame(contentModel.Object, panel.Model);
        Assert.AreSame(browser, contentModel.Object.ContentBrowser);
        Assert.AreEqual(new Size(browser.ViewSize.Width - 2, browser.ViewSize.Height - 2), contentModel.Object.ViewSize);
        Assert.AreSame(contentModel.Object, browser.ContentModel);
        Assert.AreSame(contentModel.Object, itemInformation.ContentModel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetFocus_UpdatesFocusedPanelTracking()
    {
        ThemeService themeService = new();
        Mock<IContentModelService> contentModelService = new();
        TestContentPanel panel = new(
            NullSettingsService.Instance,
            new TestContentBrowser(themeService),
            new TestItemInformation(themeService),
            contentModelService.Object,
            themeService)
        {
            Name = "Panel",
        };
        contentModelService.SetupProperty(service => service.FocusedPanel, string.Empty);
        contentModelService.SetupProperty(service => service.LastFocusedPanel, string.Empty);

        panel.SetFocus();

        Assert.AreEqual("Panel", contentModelService.Object.FocusedPanel);
        Assert.AreEqual("Panel", contentModelService.Object.LastFocusedPanel);
    }
}
