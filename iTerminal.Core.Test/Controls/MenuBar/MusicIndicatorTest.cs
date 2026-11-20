using iConsole.Test.Core;
using iConsole.Data;
using iConsole.Tasks;
using iTerminal.Core.Services.Music;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Controls.MenuBar.Test;

[TestClass]
public class MusicIndicatorTest
{
    private static Mock<IMusicPlayerService> Player(bool muted = false)
    {
        var player = new Mock<IMusicPlayerService>();
        player.SetupProperty(p => p.IsMuted, muted);
        return player;
    }

    private static MusicIndicator Create(IMusicPlayerService player, TaskManager taskManager) =>
        new(player, taskManager, TestTheme()) { ViewSize = new Size(MusicIndicator.Width, 1) };

    [Timeout(60_000)]
    [TestMethod]
    public void WhenNotMutedTheRowOffersStopAndMute()
    {
        using var taskManager = new TaskManager();
        var indicator = Create(Player().Object, taskManager);

        var screen = Render(indicator, new Size(MusicIndicator.Width, 1));

        Assert.AreEqual("Stop   Mute    ", screen.Line(0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WhenMutedTheMuteRegionOffersUnmuteInTheSameSpace()
    {
        using var taskManager = new TaskManager();
        var indicator = Create(Player(muted: true).Object, taskManager);

        var screen = Render(indicator, new Size(MusicIndicator.Width, 1));

        Assert.AreEqual("Stop   Unmute  ", screen.Line(0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DrawingAgainAfterTheStateChangesLeavesNoStaleLabel()
    {
        var player = Player();
        using var taskManager = new TaskManager();
        var indicator = Create(player.Object, taskManager);
        var screen = new ScreenBuffer(MusicIndicator.Width, 1);

        indicator.Draw(screen);
        player.Object.IsMuted = true;
        indicator.Draw(screen);

        Assert.AreEqual("Stop   Unmute  ", screen.Line(0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickingTheMuteRegionTogglesMute()
    {
        var player = Player();
        using var taskManager = new TaskManager();
        var indicator = Create(player.Object, taskManager);
        var click = new MouseEvent { LeftClicked = true, Position = new Position(10, 0) };

        Assert.IsTrue(await indicator.HandleMouseEventAsync(click));
        Assert.IsTrue(player.Object.IsMuted);

        Assert.IsTrue(await indicator.HandleMouseEventAsync(click));
        Assert.IsFalse(player.Object.IsMuted);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickingTheStopRegionStopsThePlayerOnABackgroundTask()
    {
        var player = Player();
        var stopEntered = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var stopFinished = new ManualResetEventSlim(false);
        player.Setup(p => p.Stop()).Callback(() =>
        {
            stopEntered.Set();
            release.Wait();
            stopFinished.Set();
        });
        using var taskManager = new TaskManager();
        var indicator = Create(player.Object, taskManager);

        try
        {
            // The device can hold Stop for a while; the click must not wait for it.
            var handled = await indicator.HandleMouseEventAsync(new MouseEvent { LeftClicked = true, Position = new Position(2, 0) })
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsTrue(handled);
            Assert.IsFalse(stopFinished.IsSet, "the click returned while Stop is still blocked");
            await WaitUntilAsync(() => stopEntered.IsSet, "Stop to start on a background task");
        }
        finally
        {
            release.Set();
        }

        await WaitUntilAsync(() => stopFinished.IsSet, "Stop to finish");
        player.Verify(p => p.Stop(), Times.Once);
    }
}
