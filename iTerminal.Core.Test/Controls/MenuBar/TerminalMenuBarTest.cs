using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Controls.MenuBar;
using iTerminal.Core.Data;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;
using iTerminal.Core.Test.Dialogs;

namespace iTerminal.Core.Controls.MenuBar.Test;

/// <summary>The top row: the menus on the left, the indicators and the clock on the right.</summary>
[TestClass]
public class TerminalMenuBarTest
{
    private static (TerminalMenuBar Bar, SettingsService Settings, TaskManager TaskManager) Create(int width = 200, InternetConnectivityService? connectivity = null)
    {
        var settings = new SettingsService();
        var taskManager = new TaskManager();
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());

        var bar = new TerminalMenuBar(
            TestTheme(), Mock.Of<IContentModelService>(), settings, new WeatherService(), new OnlineQuoteService(), taskManager,
            null!, null!, null!, null!, null!,
            null!, null!, null!,
            null!, new DriveInfoModel(), null!, Mock.Of<IBeepService>(), new DateTimeProviderService(), guard.Object, Mock.Of<INetworkDiscoveryService>(), Mock.Of<INetworkProfileService>(), iTerminal.Core.Test.Dialogs.FolderSelectorTestHarness.EmptyRemoteConnections(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, connectivityService: connectivity);

        var parent = new Control(TestTheme()) { ViewSize = new Size(width, 40) };
        parent.AddControl(bar);
        bar.ViewSize = new Size(width, 1);
        return (bar, settings, taskManager);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnAWideScreenEveryIndicatorIsShown()
    {
        var (bar, _, taskManager) = Create(width: 200);
        using var _ = taskManager;
        await bar.OnInitializeAsync();

        bar.OnInitializeLayout();

        Assert.IsTrue(bar.IsQuoteIndicatorShownForTest);
        Assert.IsTrue(bar.IsWeatherIndicatorShownForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnANarrowScreenTheOptionalIndicatorsGiveWayToTheMenus()
    {
        var (bar, _, taskManager) = Create(width: 70);
        using var _ = taskManager;
        await bar.OnInitializeAsync();

        bar.OnInitializeLayout();

        Assert.IsFalse(bar.IsQuoteIndicatorShownForTest);
        Assert.IsFalse(bar.IsWeatherIndicatorShownForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task IndicatorsSwitchedOffInSettingsAreNotShown()
    {
        var (bar, settings, taskManager) = Create();
        using var _ = taskManager;
        settings.Set(Settings.IsQuotesIndicatorEnabled, bool.FalseString);
        await bar.OnInitializeAsync();

        bar.OnInitializeLayout();

        Assert.IsFalse(bar.IsQuoteIndicatorShownForTest);
        Assert.IsTrue(bar.IsWeatherIndicatorShownForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASwitchChangedWhileRunningTakesEffectWithoutARestart()
    {
        // The switches were read once at start-up; turning one off in Settings changed nothing
        // until the app was restarted.
        var (bar, settings, taskManager) = Create();
        using var _ = taskManager;
        await bar.OnInitializeAsync();
        bar.OnInitializeLayout();
        Assert.IsTrue(bar.IsWeatherIndicatorShownForTest);

        settings.Set(Settings.IsWeatherIndicatorEnabled, bool.FalseString);
        bar.OnIdle();

        Assert.IsFalse(bar.IsWeatherIndicatorShownForTest);

        settings.Set(Settings.IsWeatherIndicatorEnabled, bool.TrueString);
        bar.OnIdle();

        Assert.IsTrue(bar.IsWeatherIndicatorShownForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheOnlineIndicatorIsShownOnAWideScreenAndFollowsTheSwitch()
    {
        var connectivity = new InternetConnectivityService();
        connectivity.Report(false);
        var (bar, settings, taskManager) = Create(width: 200, connectivity);
        using var _ = taskManager;
        await bar.OnInitializeAsync();

        bar.OnInitializeLayout();
        Assert.IsTrue(bar.IsOnlineIndicatorShownForTest);

        settings.Set(Settings.IsOnlineIndicatorEnabled, bool.FalseString);
        bar.OnIdle();
        Assert.IsFalse(bar.IsOnlineIndicatorShownForTest);

        settings.Set(Settings.IsOnlineIndicatorEnabled, bool.TrueString);
        bar.OnIdle();
        Assert.IsTrue(bar.IsOnlineIndicatorShownForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheOnlineIndicatorGivesWayOnANarrowScreen()
    {
        var (bar, _, taskManager) = Create(width: 40, new InternetConnectivityService());
        using var _ = taskManager;
        await bar.OnInitializeAsync();

        bar.OnInitializeLayout();

        Assert.IsFalse(bar.IsOnlineIndicatorShownForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheMenusAreFileEditNavigateToolsHelp()
    {
        var (bar, _, taskManager) = Create();
        using var _ = taskManager;

        var menus = bar.Controls.AsSnapshot().OfType<MenuItem>().Select(m => m.Text).ToList();

        CollectionAssert.AreEqual(new[] { "File", "Edit", "Navigate", "Tools", "Help" }, menus);
    }
}
