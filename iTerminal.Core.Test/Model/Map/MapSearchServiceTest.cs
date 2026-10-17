using System.Text;
using iTerminal.Core.Services.Map;
using iTerminal.Core.Services.Web;

namespace iTerminal.Core.Test.Model.Map;

[TestClass]
public class MapSearchServiceTest
{
    private const string Answer = """
        [{"lat":"48.8588897","lon":"2.3200410","display_name":"Paris, France"},
         {"lat":"bad","lon":"1","display_name":"Broken"},
         {"lat":"33.66","lon":"-95.55"}]
        """;

    [TestMethod]
    public async Task AskedNominatimWithTheEscapedQuery()
    {
        var fetcher = new FakeTileFetcher(_ => Encoding.UTF8.GetBytes(Answer));
        var service = new MapSearchService(fetcher);

        var places = await service.SearchAsync("Rue de Rivoli, Paris");

        Assert.AreEqual(MapSearchService.Endpoint + "Rue%20de%20Rivoli%2C%20Paris", fetcher.Urls.Single());
        Assert.AreEqual(2, places.Count);
        Assert.AreEqual("Paris, France", places[0].Name);
        Assert.AreEqual(48.8588897, places[0].Latitude, 1e-9);
        Assert.AreEqual(-95.55, places[1].Longitude, 1e-9);
    }

    [TestMethod]
    public async Task CoordinatesNeedNoNetwork()
    {
        var fetcher = new FakeTileFetcher(_ => null);

        var places = await new MapSearchService(fetcher).SearchAsync("47.5, 19.04");

        Assert.AreEqual(0, fetcher.Requests);
        Assert.AreEqual(47.5, places.Single().Latitude);
    }

    [TestMethod]
    public async Task AnAnswerThatIsNotJsonIsNoResults()
    {
        var service = new MapSearchService(new FakeTileFetcher(_ => Encoding.UTF8.GetBytes("<html>busy</html>")));

        Assert.AreEqual(0, (await service.SearchAsync("anything")).Count);
    }

    [TestMethod]
    public async Task ABlankQueryAsksNothing()
    {
        var fetcher = new FakeTileFetcher(_ => null);

        Assert.AreEqual(0, (await new MapSearchService(fetcher).SearchAsync("  ")).Count);
        Assert.AreEqual(0, fetcher.Requests);
    }

    [TestMethod]
    public async Task ANetworkFailureSurfacesAsAReadableError()
    {
        var service = new MapSearchService(new FakeTileFetcher(_ => null));

        var exception = await Assert.ThrowsExactlyAsync<WebFetchException>(() => service.SearchAsync("Vienna"));
        StringAssert.Contains(exception.Message, "No such host");
    }

    [TestMethod]
    public void TheStoreRemembersPlacesAndSurvivesCorruptSettings()
    {
        var settings = new MemorySettings();
        var store = new MapStore(settings);
        store.AddPlace(new MapPlace("A", 1, 2));
        store.AddPlace(new MapPlace("B", 3, 4));
        store.AddPlace(new MapPlace("A", 5, 6));

        Assert.AreEqual("A,B", string.Join(',', new MapStore(settings).Places.Select(p => p.Name)));
        Assert.AreEqual(5, store.Places[0].Latitude);

        settings.Set(MapStore.PlacesKey, "{not json");
        settings.Set(MapStore.ViewKey, "garbage");
        var broken = new MapStore(settings);
        Assert.AreEqual(0, broken.Places.Count);
        Assert.AreEqual(MapStore.HomeZoom, broken.LoadView().Zoom);
    }
}
