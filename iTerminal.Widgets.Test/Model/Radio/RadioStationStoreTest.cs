using iConsole.Media.Audio;
using iTerminal.Widgets.Model.Radio;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Widgets.Test.Model.Radio;

[TestClass]
public class RadioStationStoreTest
{
    private static RadioStation Make(string name, string url = "", string genre = "", bool favourite = false) =>
        new(Guid.NewGuid(), name, url == string.Empty ? $"https://{name.Replace(' ', '-').ToLowerInvariant()}.example/stream" : url, genre, favourite);

    /// <summary>Settings that hold an empty list, so the store starts without the preseeded stations.</summary>
    private static ISettingsService EmptySettings()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(RadioStationStore.SettingsKey)).Returns("[]");
        return settings.Object;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AStoreThatWasNeverSavedStartsWithThePreseededStations()
    {
        var store = new RadioStationStore(new Mock<ISettingsService>().Object);

        Assert.AreEqual(DefaultRadioStations.All.Count, store.Count);
        Assert.IsTrue(store.Find().All(s => s.Validate(out _)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EmptyingTheListIsSavedAndNotRefilledAtTheNextStart()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(RadioStationStore.SettingsKey)).Returns("[]");

        Assert.AreEqual(0, new RadioStationStore(settings.Object).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThePreseededStationsAreDistinctAndPlayable()
    {
        var urls = DefaultRadioStations.All.Select(s => s.Url).ToList();
        var ids = DefaultRadioStations.All.Select(s => s.Id).ToList();

        Assert.AreEqual(urls.Count, urls.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "a stream address is listed twice");
        Assert.AreEqual(ids.Count, ids.Distinct().Count());
        Assert.IsTrue(DefaultRadioStations.All.All(s => s.Validate(out _)));
        Assert.IsTrue(DefaultRadioStations.All.All(s => !s.IsFavourite));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FavouritesAreListedFirst_ThenByName()
    {
        var store = new RadioStationStore(EmptySettings());
        store.Add(Make("Zebra FM"));
        store.Add(Make("Beta Radio", favourite: true));
        store.Add(Make("Alpha Jazz"));
        store.Add(Make("Gamma Rock", favourite: true));

        CollectionAssert.AreEqual(
            new[] { "Beta Radio", "Gamma Rock", "Alpha Jazz", "Zebra FM" },
            store.Find().Select(s => s.Name).ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FavouritesOnly_CutsTheListToTheFavourites()
    {
        var store = new RadioStationStore(EmptySettings());
        store.Add(Make("Alpha Jazz"));
        store.Add(Make("Beta Radio", favourite: true));

        var favourites = store.Find(favouritesOnly: true);

        Assert.AreEqual(1, favourites.Count);
        Assert.AreEqual("Beta Radio", favourites[0].Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheFilterMatchesNameGenreAndAddress()
    {
        var store = new RadioStationStore(EmptySettings());
        store.Add(new RadioStation(Guid.NewGuid(), "Groove Salad", "https://ice.somafm.com/groovesalad", "Ambient", false));
        store.Add(new RadioStation(Guid.NewGuid(), "Radio Paradise", "https://stream.radioparadise.com/rock", "Rock", false));

        Assert.AreEqual("Groove Salad", store.Find("ambient").Single().Name);
        Assert.AreEqual("Radio Paradise", store.Find("paradise").Single().Name);
        Assert.AreEqual("Groove Salad", store.Find("somafm").Single().Name);
        Assert.AreEqual(0, store.Find("ambient rock").Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HasUrl_FindsADuplicateAddressIgnoringTheStationBeingEdited()
    {
        var store = new RadioStationStore(EmptySettings());
        var station = Make("One", "https://example.com/one");
        store.Add(station);

        Assert.IsTrue(store.HasUrl("https://EXAMPLE.com/one"));
        Assert.IsFalse(store.HasUrl("https://example.com/one", station.Id));
        Assert.IsFalse(store.HasUrl("https://example.com/two"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UpdateReplacesTheStationWithTheSameId_AndRemoveDropsIt()
    {
        var store = new RadioStationStore(EmptySettings());
        var station = Make("Old Name", "https://example.com/a");
        store.Add(station);

        store.Update(station with { IsFavourite = true, Name = "New Name" });
        Assert.IsTrue(store.Get(station.Id)!.IsFavourite);
        Assert.AreEqual("New Name", store.Get(station.Id)!.Name);

        store.Remove(station.Id);
        Assert.AreEqual(0, store.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryChangeIsWrittenBackAsOneJsonListUnderItsSettingsKey()
    {
        var settings = new Mock<ISettingsService>();
        string? written = null;
        settings.Setup(s => s.Set(RadioStationStore.SettingsKey, It.IsAny<string>()))
            .Callback<string, string>((_, value) => written = value);
        var store = new RadioStationStore(settings.Object);

        store.Add(Make("Persisted FM", "https://example.com/p", "Pop", favourite: true));

        Assert.IsNotNull(written);
        StringAssert.Contains(written, "Persisted FM");
        settings.Verify(s => s.Write(), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AStoredListIsReadBackAndADamagedOneIsIgnored()
    {
        var saved = new Mock<ISettingsService>();
        saved.Setup(s => s.Get(RadioStationStore.SettingsKey))
            .Returns("[{\"Id\":\"6f1c2b1e-0000-4000-8000-000000000001\",\"Name\":\"Kept\",\"Url\":\"https://example.com/k\",\"Genre\":\"Jazz\",\"IsFavourite\":true},null]");
        var store = new RadioStationStore(saved.Object);
        Assert.AreEqual("Kept", store.Find().Single().Name);

        var damaged = new Mock<ISettingsService>();
        damaged.Setup(s => s.Get(RadioStationStore.SettingsKey)).Returns("{not json");
        Assert.AreEqual(0, new RadioStationStore(damaged.Object).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("https://ice.example.com/stream", true)]
    [DataRow("http://radio.example.com:8000/live.mp3", true)]
    [DataRow("ftp://example.com/stream", false)]
    [DataRow("/home/me/music/track.mp3", false)]
    [DataRow("relative/stream", false)]
    [DataRow("", false)]
    public void OnlyHttpAddressesAreStreams(string path, bool expected) =>
        Assert.AreEqual(expected, AudioDecoderFactory.IsStreamUrl(path));

    [Timeout(60_000)]
    [TestMethod]
    public void RecentListKeepsTheLastFiveStartedMostRecentFirst()
    {
        var store = new RadioStationStore(new Mock<ISettingsService>().Object);
        var stations = Enumerable.Range(1, 6).Select(i => Make($"Station {i}")).ToList();
        stations.ForEach(store.Add);

        foreach (var station in stations)
        {
            store.RecordPlayed(station.Id);
        }

        CollectionAssert.AreEqual(
            new[] { "Station 6", "Station 5", "Station 4", "Station 3", "Station 2" },
            store.Recent().Select(s => s.Name).ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PlayingAStationAgainMovesItToTheTopWithoutDuplicating()
    {
        var store = new RadioStationStore(new Mock<ISettingsService>().Object);
        var first = Make("First");
        var second = Make("Second");
        store.Add(first);
        store.Add(second);
        store.RecordPlayed(first.Id);
        store.RecordPlayed(second.Id);

        store.RecordPlayed(first.Id);

        CollectionAssert.AreEqual(new[] { "First", "Second" }, store.Recent().Select(s => s.Name).ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RemovingAStationDropsItFromTheRecentList()
    {
        var store = new RadioStationStore(new Mock<ISettingsService>().Object);
        var station = Make("Gone");
        store.Add(station);
        store.RecordPlayed(station.Id);

        store.Remove(station.Id);

        Assert.AreEqual(0, store.Recent().Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RecentListIsSavedUnderItsOwnKeyAndReadBack()
    {
        var settings = new Mock<ISettingsService>();
        var values = new Dictionary<string, string>();
        settings.Setup(s => s.Set(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((key, value) => values[key] = value);
        settings.Setup(s => s.Get(It.IsAny<string>()))
            .Returns<string>(key => values.TryGetValue(key, out var value) ? value : null);
        var store = new RadioStationStore(settings.Object);
        var station = Make("Remembered");
        store.Add(station);
        store.RecordPlayed(station.Id);

        var reopened = new RadioStationStore(settings.Object);

        Assert.AreEqual("Remembered", reopened.Recent().Single().Name);
        Assert.IsTrue(values.ContainsKey(RadioStationStore.RecentSettingsKey));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARecentListThatIsDamagedStartsEmptyAndKeepsTheStations()
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(RadioStationStore.SettingsKey))
            .Returns("[{\"Id\":\"6f1c2b1e-0000-4000-8000-000000000002\",\"Name\":\"Kept\",\"Url\":\"https://example.com/k\",\"Genre\":\"\",\"IsFavourite\":false}]");
        settings.Setup(s => s.Get(RadioStationStore.RecentSettingsKey)).Returns("{not json");

        var store = new RadioStationStore(settings.Object);

        Assert.AreEqual("Kept", store.Find().Single().Name);
        Assert.AreEqual(0, store.Recent().Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AStationNeedsANameAndAStreamAddress()
    {
        Assert.IsFalse(new RadioStation(Guid.NewGuid(), "  ", "https://example.com/s", "", false).Validate(out var noName));
        StringAssert.Contains(noName, "name");

        Assert.IsFalse(new RadioStation(Guid.NewGuid(), "Radio", "example.com/s", "", false).Validate(out var noScheme));
        StringAssert.Contains(noScheme, "http");

        Assert.IsTrue(new RadioStation(Guid.NewGuid(), "Radio", "https://example.com/s", "", false).Validate(out var ok));
        Assert.AreEqual(string.Empty, ok);
    }
}
