using iTerminal.Core.Extensions;
using iTerminal.Widgets.Controls.MenuBar;
using iTerminal.Widgets.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Controls.MenuBar;
using iTerminal.Core.Data;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;
using iTerminal.Core.Test.Dialogs;

namespace iTerminal.Core.Controls.MenuBar.Test;

/// <summary>The top row: the menus on the left, the indicators and the clock on the right.</summary>
[TestClass]
public class TerminalMenuBarTest
{
    private static (TerminalMenuBar Bar, SettingsService Settings, TaskManager TaskManager) Create(int width = 200, InternetConnectivityService? connectivity = null, IMusicPlayerService? player = null)
    {
        var settings = new SettingsService();
        var taskManager = new TaskManager();
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());

        var bar = new TerminalMenuBar(
            TestTheme(), Mock.Of<IContentModelService>(), settings, taskManager,
            null!, null!, null!, null!, null!,
            null!, null!, null!,
            null!, new DriveInfoModel(), null!, Mock.Of<IBeepService>(), new DateTimeProviderService(), guard.Object, Mock.Of<INetworkDiscoveryService>(), Mock.Of<INetworkProfileService>(), iTerminal.Core.Test.Dialogs.FolderSelectorTestHarness.EmptyRemoteConnections(), null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, connectivityService: connectivity, musicPlayerService: player,
            indicators: [new QuoteMenuBarIndicator(new OnlineQuoteService(), settings, TestTheme()), new WeatherMenuBarIndicator(new WeatherService(), settings, TestTheme())]);

        var parent = new Control(TestTheme()) { ViewSize = new Size(width, 40) };
        parent.AddControl(bar);
        bar.ViewSize = new Size(width, 1);
        return (bar, settings, taskManager);
    }

    private static bool Shown<TIndicator>(TerminalMenuBar bar) where TIndicator : IMenuBarIndicator =>
        bar.IndicatorsForTest.OfType<TIndicator>().Single().Parts.All(part => part.Control.IsVisible);

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnAWideScreenEveryIndicatorIsShown()
    {
        var (bar, _, taskManager) = Create(width: 200);
        using var _ = taskManager;
        await bar.OnInitializeAsync();

        bar.OnInitializeLayout();

        Assert.IsTrue(Shown<QuoteMenuBarIndicator>(bar));
        Assert.IsTrue(Shown<WeatherMenuBarIndicator>(bar));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnANarrowScreenTheOptionalIndicatorsGiveWayToTheMenus()
    {
        var (bar, _, taskManager) = Create(width: 70);
        using var _ = taskManager;
        await bar.OnInitializeAsync();

        bar.OnInitializeLayout();

        Assert.IsFalse(Shown<QuoteMenuBarIndicator>(bar));
        Assert.IsFalse(Shown<WeatherMenuBarIndicator>(bar));
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

        Assert.IsFalse(Shown<QuoteMenuBarIndicator>(bar));
        Assert.IsTrue(Shown<WeatherMenuBarIndicator>(bar));
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
        Assert.IsTrue(Shown<WeatherMenuBarIndicator>(bar));

        settings.Set(Settings.IsWeatherIndicatorEnabled, bool.FalseString);
        bar.OnIdle();

        Assert.IsFalse(Shown<WeatherMenuBarIndicator>(bar));

        settings.Set(Settings.IsWeatherIndicatorEnabled, bool.TrueString);
        bar.OnIdle();

        Assert.IsTrue(Shown<WeatherMenuBarIndicator>(bar));
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

    private static Mock<IMusicPlayerService> MusicPlayer(Func<PlaybackState> state)
    {
        var player = new Mock<IMusicPlayerService>();
        player.Setup(p => p.State).Returns(state);
        return player;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WhileMusicPlaysTheMusicWidgetIsShown()
    {
        var player = MusicPlayer(() => PlaybackState.Playing);
        var (bar, _, taskManager) = Create(width: 200, player: player.Object);
        using var _ = taskManager;
        await bar.OnInitializeAsync();

        bar.OnInitializeLayout();

        Assert.IsTrue(bar.IsMusicIndicatorShownForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WhileStoppedTheMusicWidgetIsHidden()
    {
        var player = MusicPlayer(() => PlaybackState.Stopped);
        var (bar, _, taskManager) = Create(width: 200, player: player.Object);
        using var _ = taskManager;
        await bar.OnInitializeAsync();

        bar.OnInitializeLayout();

        Assert.IsFalse(bar.IsMusicIndicatorShownForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task StartingAndStoppingPlaybackLaysTheBarOutAgain()
    {
        var state = PlaybackState.Stopped;
        var player = MusicPlayer(() => state);
        var (bar, _, taskManager) = Create(width: 200, player: player.Object);
        using var _ = taskManager;
        await bar.OnInitializeAsync();
        bar.OnInitializeLayout();
        Assert.IsFalse(bar.IsMusicIndicatorShownForTest);

        state = PlaybackState.Playing;
        bar.OnIdle();
        Assert.IsTrue(bar.IsMusicIndicatorShownForTest, "the idle pass sees playback start and lays the bar out");

        state = PlaybackState.Stopped;
        bar.OnIdle();
        Assert.IsFalse(bar.IsMusicIndicatorShownForTest, "and sees it stop");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WhilePlayingTheWidgetTakesRoomFromTheQuotesFirst()
    {
        // The narrowest bar that shows the quotes while stopped: music needs its room from them.
        int width = 0;
        for (int candidate = 40; candidate <= 260 && width == 0; candidate++)
        {
            var (bar, _, taskManager) = Create(candidate, player: MusicPlayer(() => PlaybackState.Stopped).Object);
            using var _ = taskManager;
            bar.OnInitializeLayout();
            if (Shown<QuoteMenuBarIndicator>(bar))
            {
                width = candidate;
            }
        }
        Assert.AreNotEqual(0, width, "some bar width shows the quotes while stopped");

        var (playing, _, playingTasks) = Create(width, player: MusicPlayer(() => PlaybackState.Playing).Object);
        using var __ = playingTasks;
        playing.OnInitializeLayout();

        Assert.IsFalse(Shown<QuoteMenuBarIndicator>(playing), "the quotes give way to the music widget");
    }
}
