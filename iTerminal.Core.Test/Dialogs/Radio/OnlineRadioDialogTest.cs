using iTerminal.Core.Data.Music;
using iTerminal.Core.Dialogs.Radio;
using iTerminal.Core.Model.Radio;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Radio;

[TestClass]
public class OnlineRadioDialogTest
{
    private const string Jazz = "https://jazz.example.com/stream";
    private const string Rock = "https://rock.example.com/live";

    /// <summary>Settings that hold an empty list, so the store starts without the preseeded stations.</summary>
    private static ISettingsService EmptySettings()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(RadioStationStore.SettingsKey)).Returns("[]");
        return settings.Object;
    }

    private static (OnlineRadioDialog Dialog, RadioStationStore Store, Mock<IMusicPlayerService> Player) Create()
    {
        var store = new RadioStationStore(EmptySettings());
        var player = new Mock<IMusicPlayerService>();
        return (new OnlineRadioDialog(store, player.Object, TestTheme()), store, player);
    }

    private static RadioStation Add(RadioStationStore store, string name, string url, bool favourite = false)
    {
        var station = new RadioStation(Guid.NewGuid(), name, url, "", favourite);
        store.Add(station);
        return station;
    }

    private static async Task TypeAsync(OnlineRadioDialog dialog, string text)
    {
        foreach (var c in text)
        {
            await dialog.HandleKeyEventAsync(Char(c));
        }
    }

    private static Task EnterAsync(OnlineRadioDialog dialog) => dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

    private static Task SpaceAsync(OnlineRadioDialog dialog) => dialog.HandleKeyEventAsync(Key(ConsoleKey.Spacebar, ' '));

    [Timeout(60_000)]
    [TestMethod]
    public async Task AStationTypedFieldByFieldIsAddedAndHighlighted()
    {
        var (dialog, store, _) = Create();
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(dialog, "Jazz FM");
        await EnterAsync(dialog);
        await TypeAsync(dialog, "Jazz");
        await EnterAsync(dialog);
        await TypeAsync(dialog, Jazz);
        await EnterAsync(dialog);

        var saved = store.Find().Single();
        Assert.AreEqual(("Jazz FM", "Jazz", Jazz, false), (saved.Name, saved.Genre, saved.Url, saved.IsFavourite));
        Assert.AreEqual(saved.Id, dialog.SelectedForTest!.Id);
        Assert.IsFalse(dialog.IsFormOpenForTest);
        AssertScreenContains(Render(dialog), Jazz);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnAddressThatIsNotHttpKeepsTheFormOpenWithAMessage()
    {
        var (dialog, store, _) = Create();
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(dialog, "No Scheme");
        await EnterAsync(dialog);
        await EnterAsync(dialog);
        await TypeAsync(dialog, "jazz.example.com/stream");
        await EnterAsync(dialog);

        Assert.IsTrue(dialog.IsFormOpenForTest);
        StringAssert.Contains(dialog.ErrorForTest!, "http");
        Assert.AreEqual(0, store.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AStationAlreadyInTheListIsRefused()
    {
        var (dialog, store, _) = Create();
        Add(store, "Jazz FM", Jazz);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(dialog, "Copy");
        await EnterAsync(dialog);
        await EnterAsync(dialog);
        await TypeAsync(dialog, Jazz);
        await EnterAsync(dialog);

        Assert.IsTrue(dialog.IsFormOpenForTest);
        StringAssert.Contains(dialog.ErrorForTest!, "already");
        Assert.AreEqual(1, store.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task FMarksAFavouriteAndTheFavouriteMovesToTheTop()
    {
        var (dialog, store, _) = Create();
        Add(store, "Alpha Jazz", Jazz);
        Add(store, "Beta Rock", Rock);
        await ShowAsync(dialog);
        Assert.AreEqual("Alpha Jazz", dialog.SelectedForTest!.Name);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await dialog.HandleKeyEventAsync(Char('f'));

        Assert.IsTrue(store.Find().First().IsFavourite);
        Assert.AreEqual("Beta Rock", store.Find().First().Name);
        Assert.AreEqual("Beta Rock", dialog.SelectedForTest!.Name, "the highlight stays on the station it was on");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task VShowsTheFavouritesAloneAndAnEmptyListSaysSo()
    {
        var (dialog, store, _) = Create();
        Add(store, "Alpha Jazz", Jazz);
        Add(store, "Beta Rock", Rock, favourite: true);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('v'));

        Assert.IsTrue(dialog.FavouritesOnlyForTest);
        Assert.AreEqual("Beta Rock", dialog.SelectedForTest!.Name);
        AssertScreenDoesNotContain(Render(dialog), "Alpha Jazz");

        await dialog.HandleKeyEventAsync(Char('f'));
        AssertScreenContains(Render(dialog), "No favourite stations");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterPlaysTheStationThroughThePlayerAsTheOnlyTrack()
    {
        var (dialog, store, player) = Create();
        Add(store, "Rock Live", Rock);
        await ShowAsync(dialog);
        IEnumerable<MusicTrack>? queued = null;
        player.Setup(p => p.ReplacePlaylist(It.IsAny<IEnumerable<MusicTrack>>(), It.IsAny<int>()))
            .Callback<IEnumerable<MusicTrack>, int>((tracks, _) => queued = tracks);

        await EnterAsync(dialog);

        var track = queued!.Single();
        Assert.AreEqual(Rock, track.Path);
        Assert.AreEqual("Rock Live", track.DisplayTitle);
        player.Verify(p => p.PlayIndex(0), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SpacePausesTheStationOnAirAndStartsAnyOtherOne()
    {
        var (dialog, store, player) = Create();
        Add(store, "Rock Live", Rock);
        await ShowAsync(dialog);
        player.Setup(p => p.CurrentTrack).Returns(new MusicTrack(Rock) { Title = "Rock Live" });
        player.Setup(p => p.State).Returns(PlaybackState.Playing);

        await SpaceAsync(dialog);
        player.Verify(p => p.Pause(), Times.Once);

        player.Setup(p => p.State).Returns(PlaybackState.Paused);
        await SpaceAsync(dialog);
        player.Verify(p => p.Resume(), Times.Once);

        player.Setup(p => p.CurrentTrack).Returns((MusicTrack?)null);
        player.Setup(p => p.State).Returns(PlaybackState.Stopped);
        await SpaceAsync(dialog);
        player.Verify(p => p.PlayIndex(0), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SAndTheStatusLineFollowThePlayer()
    {
        var (dialog, store, player) = Create();
        Add(store, "Rock Live", Rock);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('s'));
        player.Verify(p => p.Stop(), Times.Once);

        player.Setup(p => p.CurrentTrack).Returns(new MusicTrack(Rock) { Title = "Rock Live" });
        player.Setup(p => p.State).Returns(PlaybackState.Playing);
        AssertScreenContains(Render(dialog), "On air: Rock Live");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DeleteAsksFirstAndOnlyAYDeletes()
    {
        var (dialog, store, _) = Create();
        Add(store, "Rock Live", Rock);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('d'));
        Assert.IsTrue(dialog.ConfirmingDeleteForTest);
        await dialog.HandleKeyEventAsync(Char('n'));
        Assert.AreEqual(1, store.Count);

        await dialog.HandleKeyEventAsync(Char('d'));
        await dialog.HandleKeyEventAsync(Char('y'));
        Assert.AreEqual(0, store.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SlashFiltersByNameGenreOrAddress()
    {
        var (dialog, store, _) = Create();
        Add(store, "Alpha Jazz", Jazz);
        Add(store, "Rock Live", Rock);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('/'));
        Assert.IsTrue(dialog.IsFilteringForTest);
        await TypeAsync(dialog, "rock");
        await EnterAsync(dialog);

        Assert.IsFalse(dialog.IsFilteringForTest);
        Assert.AreEqual("Rock Live", dialog.SelectedForTest!.Name);
        AssertScreenDoesNotContain(Render(dialog), "Alpha Jazz");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheListShowsTheAddressOfTheHighlightedStation()
    {
        var (dialog, store, _) = Create();
        Add(store, "Rock Live", Rock);
        await ShowAsync(dialog);

        AssertScreenContains(Render(dialog), "Rock Live");
        AssertScreenContains(Render(dialog), Rock);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RShowsTheStationsLastPlayedAndVGoesBackToTheFullList()
    {
        var (dialog, store, player) = Create();
        Add(store, "Alpha Jazz", Jazz);
        var rock = Add(store, "Beta Rock", Rock);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('r'));
        Assert.IsTrue(dialog.RecentOnlyForTest);
        AssertScreenContains(Render(dialog), "Nothing played yet");
        await dialog.HandleKeyEventAsync(Char('r'));
        Assert.IsFalse(dialog.RecentOnlyForTest);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await EnterAsync(dialog);
        player.Verify(p => p.PlayIndex(0), Times.Once);

        await dialog.HandleKeyEventAsync(Char('r'));
        Assert.IsTrue(dialog.RecentOnlyForTest);
        Assert.AreEqual(rock.Id, dialog.SelectedForTest!.Id);
        AssertScreenDoesNotContain(Render(dialog), "Alpha Jazz");

        await dialog.HandleKeyEventAsync(Char('v'));
        Assert.IsTrue(dialog.FavouritesOnlyForTest);
        Assert.IsFalse(dialog.RecentOnlyForTest);
        await dialog.HandleKeyEventAsync(Char('v'));
        Assert.IsFalse(dialog.FavouritesOnlyForTest);
        AssertScreenContains(Render(dialog), "Alpha Jazz");
    }
}
