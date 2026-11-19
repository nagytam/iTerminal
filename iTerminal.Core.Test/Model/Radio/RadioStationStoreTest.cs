using iTerminal.Core.Audio;
using iTerminal.Core.Model.Radio;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Model.Radio;

[TestClass]
public class RadioStationStoreTest
{
    private static RadioStation Make(string name, string url = "", string genre = "", bool favourite = false) =>
        new(Guid.NewGuid(), name, url == string.Empty ? $"https://{name.Replace(' ', '-').ToLowerInvariant()}.example/stream" : url, genre, favourite);

    [Timeout(60_000)]
    [TestMethod]
    public void FavouritesAreListedFirst_ThenByName()
    {
        var store = new RadioStationStore(new Mock<ISettingsService>().Object);
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
        var store = new RadioStationStore(new Mock<ISettingsService>().Object);
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
        var store = new RadioStationStore(new Mock<ISettingsService>().Object);
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
        var store = new RadioStationStore(new Mock<ISettingsService>().Object);
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
        var store = new RadioStationStore(new Mock<ISettingsService>().Object);
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
