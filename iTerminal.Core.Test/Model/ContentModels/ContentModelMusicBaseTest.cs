using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Services.Music;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Model.ContentModels.Test;

/// <summary>The transport and mode keys a music panel binds: each one changes the player the way its status line says.</summary>
[TestClass]
public class ContentModelMusicBaseTest
{
    private sealed class TestMusicModel(IMusicPlayerService player, IFrequentExecutionGuardService guard) : ContentModelMusicBase("Music", null, player, null, guard)
    {
        protected override string UrlScheme => "musicplaylist://";
        public override void Activate(IEntry entry) { }
        public override string GetFooter() => string.Empty;
        public override string GetHeader(int width) => string.Empty;
        public override void ReadUrl() { }
        public override void Refresh(bool forceRefresh = false) { }
        public override void SetUrl(Url path) { }
    }

    private static (TestMusicModel Model, Mock<IMusicPlayerService> Player) Model(RepeatMode repeat = RepeatMode.Off, bool shuffled = false, int volume = 50)
    {
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());
        var player = new Mock<IMusicPlayerService>();
        player.SetupProperty(p => p.RepeatMode, repeat);
        player.SetupProperty(p => p.IsShuffled, shuffled);
        player.SetupProperty(p => p.Volume, volume);
        return (new TestMusicModel(player.Object, guard.Object), player);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CycleRepeat_GoesOffThenAllThenOneThenOff()
    {
        var (model, player) = Model(RepeatMode.Off);

        model.Perform(MusicAction.CycleRepeat);
        Assert.AreEqual(RepeatMode.All, player.Object.RepeatMode);
        Assert.AreEqual("Repeat all", model.StatusMessage);

        model.Perform(MusicAction.CycleRepeat);
        Assert.AreEqual(RepeatMode.One, player.Object.RepeatMode);

        model.Perform(MusicAction.CycleRepeat);
        Assert.AreEqual(RepeatMode.Off, player.Object.RepeatMode);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToggleShuffle_FlipsItAndSaysSo()
    {
        var (model, player) = Model(shuffled: false);

        model.Perform(MusicAction.ToggleShuffle);

        Assert.IsTrue(player.Object.IsShuffled);
        Assert.AreEqual("Shuffle on", model.StatusMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void VolumeUp_StepsByFiveAndShowsTheLevel()
    {
        var (model, player) = Model(volume: 50);

        model.Perform(MusicAction.VolumeUp);

        Assert.AreEqual(55, player.Object.Volume);
        Assert.AreEqual("Volume 55%", model.StatusMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void VolumeDown_StepsByFiveAndShowsTheLevel()
    {
        var (model, player) = Model(volume: 50);

        model.Perform(MusicAction.VolumeDown);

        Assert.AreEqual(45, player.Object.Volume);
        Assert.AreEqual("Volume 45%", model.StatusMessage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SeekForward_MovesFiveSecondsOnTheCurrentTrack()
    {
        var (model, player) = Model();

        model.Perform(MusicAction.SeekForward);

        player.Verify(p => p.SeekRelative(TimeSpan.FromSeconds(5)), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SeekBackward_MovesFiveSecondsBack()
    {
        var (model, player) = Model();

        model.Perform(MusicAction.SeekBackward);

        player.Verify(p => p.SeekRelative(TimeSpan.FromSeconds(-5)), Times.Once);
    }
}
