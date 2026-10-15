using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>
/// The services the folder selector and its pages need, wired once. Everything the dialog does that
/// is worth asserting ends at <see cref="IContentModelService.SetUrlAsync(Url)"/>, so both overloads
/// are recorded into <see cref="Navigations"/> and the tests read that rather than reaching into a
/// content model that does not exist here.
/// </summary>
internal sealed class FolderSelectorTestHarness
{
    /// <summary>The name the fake "last content model" answers to — the left panel, as in the app.</summary>
    public const string PanelName = "LeftPanel";

    public FolderSelectorTestHarness(ISettingsService? settingsService = null)
    {
        ThemeService = CreateThemeService();
        SettingsService = settingsService ?? new SettingsService();
        FolderHistoryService = new FolderHistoryService(SettingsService);
        DriveInfoModel = new DriveInfoModel();
        FolderIndexModel = new FolderIndexModel();

        var contentModel = new Mock<IContentModel>();
        contentModel.SetupGet(m => m.Name).Returns(PanelName);
        // A panel is always showing *something*; "Add to favourites" reads this off the active
        // model, and a fake without it throws where the real thing cannot.
        contentModel.SetupGet(m => m.Url).Returns(new Url(Path.GetTempPath()));
        ContentModel = contentModel.Object;

        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(m => m.GetLastContentModel()).Returns(ContentModel);
        contentModelService.Setup(m => m.GetFocusedModel()).Returns(ContentModel);
        contentModelService.SetupGet(m => m.Url).Returns(new Url(Path.GetTempPath()));
        contentModelService
            .Setup(m => m.SetUrlAsync(It.IsAny<Url>()))
            .Callback<Url>(url => Navigations.Add(url.FullUrl))
            .Returns(Task.CompletedTask);
        contentModelService
            .Setup(m => m.SetUrlAsync(It.IsAny<string>(), It.IsAny<Url>()))
            .Callback<string, Url>((_, url) => Navigations.Add(url.FullUrl))
            .Returns(Task.CompletedTask);
        ContentModelServiceMock = contentModelService;

        var beepService = new Mock<IBeepService>();
        BeepService = beepService.Object;

        var dateTimeProviderService = new Mock<IDateTimeProviderService>();
        dateTimeProviderService.SetupGet(m => m.Now).Returns(() => DateTime.Now);
        dateTimeProviderService.SetupGet(m => m.UtcNow).Returns(() => DateTime.UtcNow);
        DateTimeProviderService = dateTimeProviderService.Object;

        var frequentExecutionGuardService = new Mock<IFrequentExecutionGuardService>();
        frequentExecutionGuardService.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());
        FrequentExecutionGuardService = frequentExecutionGuardService.Object;
    }

    public IThemeService ThemeService { get; }
    public ISettingsService SettingsService { get; }
    public FolderHistoryService FolderHistoryService { get; }
    public DriveInfoModel DriveInfoModel { get; }
    public FolderIndexModel FolderIndexModel { get; }
    public IContentModel ContentModel { get; }
    public Mock<IContentModelService> ContentModelServiceMock { get; }
    public IContentModelService ContentModelService => ContentModelServiceMock.Object;
    public IBeepService BeepService { get; }
    public IDateTimeProviderService DateTimeProviderService { get; }
    public IFrequentExecutionGuardService FrequentExecutionGuardService { get; }

    /// <summary>Every URL the selector asked a panel to go to, in order.</summary>
    public List<string> Navigations { get; } = [];

    public static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    public static KeyEvent Key(ConsoleKey consoleKey, bool control = false, bool shift = false, bool alt = false) =>
        new('\0', consoleKey, shift, alt, control);

    /// <summary>
    /// Stands in for the screen a dialog is really shown on. Dialogs and pop-ups both attach
    /// themselves to the root rather than to whoever opened them, so a test that shows the selector
    /// on a bare control cannot see either of the two things worth asserting: which control the
    /// keys are routed to, and what is left behind once the dialog closes.
    /// </summary>
    internal sealed class TestRoot : iConsole.Controls.Control
    {
        public TestRoot(IThemeService themeService) : base(themeService)
        {
            IsVisible = true;
            IsEnabled = true;
            ViewSize = new Size(120, 40);
        }
    }

    /// <summary>
    /// Presses a key the way <c>KeyboardEventConsumerJob</c> does — into the topmost modal control,
    /// not into the dialog the test happens to hold a reference to.
    /// </summary>
    public static async Task<bool> PressAsync(TestRoot root, KeyEvent keyEvent)
    {
        var modalControl = root.GetModalChildControl() ?? root;
        return await modalControl.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>Left-clicks a screen cell, the way <c>TerminalInputConsumerJob</c> does.</summary>
    public static async Task<bool> ClickAsync(TestRoot root, int x, int y)
    {
        var modalControl = root.GetModalChildControl() ?? root;
        return await modalControl.HandleMouseEventAsync(
            new MouseEvent { Position = new Position(x, y), LeftClicked = true });
    }
}
