using iConsole.Display;
using iTerminal.Core.Model.Map;
using iTerminal.Core.Services.Map;

namespace iTerminal.Core.Test.Model.Map;

[TestClass]
public class MapModelTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup() => _folder = Path.Combine(Path.GetTempPath(), "iterminal-mapmodel-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        MapTestSupport.DeleteFolder(_folder);
    }

    private (MapModel Model, FakeTileFetcher Fetcher, MapTileService Tiles, MemorySettings Settings) Create(Func<string, byte[]?>? answer = null)
    {
        var settings = new MemorySettings();
        var fetcher = new FakeTileFetcher(answer ?? (_ => MapTestSupport.Png(MapTestSupport.Water)));
        var store = new MapStore(settings);
        var tiles = new MapTileService(fetcher, store, _folder);
        return (new MapModel(tiles, store), fetcher, tiles, settings);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(condition(), "timed out waiting");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StartsAtTheSavedViewOrAtHome()
    {
        var (model, _, _, settings) = Create();
        Assert.AreEqual(MapStore.Home.Latitude, model.Latitude, 1e-9);
        Assert.AreEqual(MapStore.HomeZoom, model.Zoom);

        model.CenterOn(10.5, 20.25, 7);
        var reopened = new MapModel(model.Tiles, new MapStore(settings));

        Assert.AreEqual(10.5, reopened.Latitude, 1e-6);
        Assert.AreEqual(20.25, reopened.Longitude, 1e-6);
        Assert.AreEqual(7, reopened.Zoom);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PanningMovesTheCentreEastAndSouth()
    {
        var (model, _, _, _) = Create();
        var (latitude, longitude) = (model.Latitude, model.Longitude);

        model.Pan(10, 0);
        Assert.IsTrue(model.Longitude > longitude);
        Assert.AreEqual(latitude, model.Latitude, 1e-9);

        model.Pan(0, 5);
        Assert.IsTrue(model.Latitude < latitude);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PanningByTheWidthOfACellMovesFourPixels()
    {
        var (model, _, _, _) = Create();
        var (x0, _) = MapProjection.ToPixel(model.Latitude, model.Longitude, model.Zoom);

        model.Pan(3, 0);

        var (x1, _) = MapProjection.ToPixel(model.Latitude, model.Longitude, model.Zoom);
        Assert.AreEqual(3 * MapAsciiRenderer.CellWidth, x1 - x0, 1e-6);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PanningPastTheDateLineWraps()
    {
        var (model, _, _, _) = Create();
        model.CenterOn(0, 179.99, 2);

        model.Pan(10, 0);

        Assert.IsTrue(model.Longitude is < -160 and > -170, $"longitude was {model.Longitude}");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PanningStopsAtThePoles()
    {
        var (model, _, _, _) = Create();
        model.CenterOn(80, 0, 3);

        model.Pan(0, -10000);

        Assert.IsTrue(model.Latitude > 84);
        Assert.IsTrue(model.Latitude <= MapProjection.MaxLatitude);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ZoomIsLimitedToTheTileServersRange()
    {
        var (model, _, _, _) = Create();
        model.CenterOn(0, 0, MapProjection.MaxZoom);
        Assert.IsFalse(model.ZoomBy(1));
        Assert.AreEqual(MapProjection.MaxZoom, model.Zoom);

        model.CenterOn(0, 0, 0);
        Assert.IsFalse(model.ZoomBy(-1));
        Assert.AreEqual(0, model.Zoom);

        Assert.IsTrue(model.ZoomBy(1));
        Assert.AreEqual(1, model.Zoom);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ZoomingAroundACellKeepsThatSpotUnderThePointer()
    {
        var (model, _, _, _) = Create();
        model.CenterOn(47.5, 19.04, 12);
        var before = model.CellToCoordinate(30, 5, 100, 30);

        model.ZoomAround(1, 30, 5, 100, 30);
        var after = model.CellToCoordinate(30, 5, 100, 30);

        Assert.AreEqual(13, model.Zoom);
        // Within a character of the spot it was on.
        var metersPerCell = MapProjection.MetersPerPixel(47.5, 13) * MapAsciiRenderer.CellHeight;
        var meters = Math.Abs(before.Latitude - after.Latitude) * 111_000;
        Assert.IsTrue(meters < metersPerCell * 2, $"moved {meters} m, a cell is {metersPerCell} m");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheCentreIsMarkedAndOffsetCellsConvertBackToCoordinates()
    {
        var (model, _, _, _) = Create();
        model.CenterOn(47.5, 19.04, 12);

        var frame = model.BuildFrame(81, 31);
        var (latitude, longitude) = model.CellToCoordinate(frame.CenterColumn, frame.CenterRow, 81, 31);

        Assert.AreEqual('X', frame[frame.CenterColumn, frame.CenterRow].Char);
        Assert.AreEqual(47.5, latitude, 0.002);
        Assert.AreEqual(19.04, longitude, 0.002);
        Assert.AreEqual(frame.CenterColumn, model.CoordinateToCell(47.5, 19.04, 81, 31)!.Value.Column);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TilesArriveInTheBackgroundAndTheFrameFillsIn()
    {
        var (model, fetcher, _, _) = Create();
        model.CenterOn(0, 0, 3);

        var first = model.BuildFrame(60, 20);
        Assert.IsTrue(first.Loading > 0);
        Assert.IsTrue(first[0, 0].Char is '.' or ' ' or '~', "a tile on its way is drawn as dots");

        await WaitForAsync(() => model.BuildFrame(60, 20).Loading == 0);
        var done = model.BuildFrame(60, 20);

        Assert.IsTrue(fetcher.Requests > 0);
        Assert.AreEqual(0, done.Unavailable);
        Assert.AreEqual('~', done[0, 0].Char);
        Assert.IsTrue(model.Version > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OfflineDrawsHatchingForTilesItDoesNotHaveAndAsksForNone()
    {
        var (model, fetcher, _, _) = Create();
        model.CenterOn(0, 0, 3);
        model.SetOnline(false);

        await WaitForAsync(() => model.BuildFrame(60, 20).Loading == 0);
        var frame = model.BuildFrame(60, 20);

        Assert.AreEqual(0, fetcher.Requests);
        Assert.IsTrue(frame.Unavailable > 0);
        Assert.IsTrue(Enumerable.Range(0, 60).Any(c => frame[c, 0].Char == '/'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GoingOnlineFetchesWhatOfflineCouldNotFind()
    {
        var (model, fetcher, _, _) = Create();
        model.CenterOn(0, 0, 3);
        model.SetOnline(false);
        await WaitForAsync(() => model.BuildFrame(60, 20).Loading == 0);

        model.SetOnline(true);
        await WaitForAsync(() => model.BuildFrame(60, 20).Loading == 0);

        Assert.IsTrue(fetcher.Requests > 0);
        Assert.AreEqual(0, model.BuildFrame(60, 20).Unavailable);
        Assert.IsTrue(model.Store.Online);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedTileIsNotRequestedInALoopUntilRetried()
    {
        var (model, fetcher, _, _) = Create(_ => null);
        model.CenterOn(0, 0, 1);

        model.BuildFrame(40, 10);
        await WaitForAsync(() => model.BuildFrame(40, 10).Loading == 0);
        var requests = fetcher.Requests;
        for (var i = 0; i < 5; i++)
        {
            model.BuildFrame(40, 10);
        }

        await Task.Delay(50);
        Assert.AreEqual(requests, fetcher.Requests);
        Assert.IsTrue(model.BuildFrame(40, 10).Unavailable > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATileOnItsWayIsDrawnFromTheZoomLevelOut()
    {
        var (model, _, tiles, _) = Create(url => url.Contains("openstreetmap.org/4/", StringComparison.Ordinal) ? null : MapTestSupport.Png(MapTestSupport.Water));
        model.CenterOn(0, 0, 3);
        await WaitForAsync(() => model.BuildFrame(30, 10).Loading == 0);
        Assert.IsNotNull(tiles.TryGetLoaded(new MapTileKey(3, 4, 4)));

        model.ZoomBy(1);
        var frame = model.BuildFrame(30, 10);

        Assert.AreEqual('~', frame[0, 0].Char, "the zoom-3 tile stands in for the missing zoom-4 one");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SavedPlacesInViewAreMarked()
    {
        var (model, _, _, _) = Create();
        model.CenterOn(47.5, 19.04, 12);
        var place = new MapPlace("Near", 47.51, 19.05);

        var frame = model.BuildFrame(81, 31, [place]);

        var (column, row) = model.CoordinateToCell(place.Latitude, place.Longitude, 81, 31)!.Value;
        Assert.AreEqual('P', frame[column, row].Char);
        Assert.AreEqual(Color.Magenta, frame[column, row].Foreground);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SavingForOfflineRefusesWhenOffline()
    {
        var (model, fetcher, _, _) = Create();
        model.SetOnline(false);

        Assert.IsFalse(model.StartSavingForOffline(80, 24));

        StringAssert.Contains(model.Message, "online");
        Assert.AreEqual(0, fetcher.Requests);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SavingForOfflineStoresTheViewAndTwoZoomsIn()
    {
        var (model, fetcher, tiles, _) = Create(_ => MapTestSupport.Png(MapTestSupport.Land));
        model.CenterOn(0.001, 0.001, 8);

        Assert.IsTrue(model.StartSavingForOffline(60, 20));
        await WaitForAsync(() => model.Message.StartsWith("Saved", StringComparison.Ordinal));

        var keys = model.TilesForView(60, 20, 2);
        Assert.IsTrue(keys.Any(k => k.Zoom == 8));
        Assert.IsTrue(keys.Any(k => k.Zoom == 10));
        Assert.IsTrue(keys.All(tiles.IsAvailableOffline));
        Assert.AreEqual(keys.Count, fetcher.Requests);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SavingForOfflineIsCappedForTheTileUsagePolicy()
    {
        var (model, _, _, _) = Create();
        model.CenterOn(0, 0, 6);

        var keys = model.TilesForView(400, 120, 3);

        Assert.IsTrue(keys.Count > MapModel.MaxDownloadTiles);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheFrameAsTextHasOneLinePerRow()
    {
        var (model, _, _, _) = Create();

        var text = model.BuildFrame(20, 5).ToText();

        Assert.AreEqual(5, text.Split('\n').Length);
        Assert.IsTrue(text.Split('\n').All(line => line.Length == 20));
    }
}
