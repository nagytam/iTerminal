using iTerminal.Core.Model.Map;
using iTerminal.Core.Services.Map;
using Microsoft.Data.Sqlite;

namespace iTerminal.Core.Test.Model.Map;

[TestClass]
public class MapTileServiceTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup() => _folder = Path.Combine(Path.GetTempPath(), "iterminal-map-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        MapTestSupport.DeleteFolder(_folder);
    }

    private MapTileService Create(FakeTileFetcher fetcher) => new(fetcher, new MapStore(new MemorySettings()), _folder);

    [TestMethod]
    public async Task OnlineFetchesFromTheOpenStreetMapServerAndSavesTheTile()
    {
        var fetcher = new FakeTileFetcher(_ => MapTestSupport.Png(MapTestSupport.Water));
        var service = Create(fetcher);
        var key = new MapTileKey(12, 2264, 1432);

        var tile = await service.LoadAsync(key, online: true);

        Assert.IsNotNull(tile);
        Assert.AreEqual("https://tile.openstreetmap.org/12/2264/1432.png", fetcher.Urls.Single());
        Assert.IsTrue(File.Exists(Path.Combine(_folder, "tiles", "12", "2264", "1432.png")));
        Assert.IsTrue(service.IsAvailableOffline(key));
    }

    [TestMethod]
    public async Task OfflineNeverTouchesTheNetwork()
    {
        var fetcher = new FakeTileFetcher(_ => MapTestSupport.Png(MapTestSupport.Water));
        var service = Create(fetcher);

        var tile = await service.LoadAsync(new MapTileKey(5, 1, 1), online: false);

        Assert.IsNull(tile);
        Assert.AreEqual(0, fetcher.Requests);
    }

    [TestMethod]
    public async Task OfflineReadsTilesSavedByAnEarlierOnlineVisit()
    {
        var key = new MapTileKey(4, 8, 5);
        await Create(new FakeTileFetcher(_ => MapTestSupport.Png(MapTestSupport.Park))).LoadAsync(key, online: true);

        var offlineFetcher = new FakeTileFetcher(_ => null);
        var tile = await Create(offlineFetcher).LoadAsync(key, online: false);

        Assert.IsNotNull(tile);
        Assert.AreEqual(0, offlineFetcher.Requests);
    }

    [TestMethod]
    public async Task ATileFolderCopiedIntoTheMapFolderIsUsed()
    {
        var path = Path.Combine(_folder, "tiles", "3", "2", "1.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, MapTestSupport.Png(MapTestSupport.Water));

        var tile = await Create(new FakeTileFetcher(_ => null)).LoadAsync(new MapTileKey(3, 2, 1), online: false);

        Assert.IsNotNull(tile);
    }

    [TestMethod]
    public async Task ReadsRasterMbtilesWithTheRowsCountedFromTheSouth()
    {
        Directory.CreateDirectory(_folder);
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(_folder, "city.mbtiles")};Pooling=False"))
        {
            connection.Open();
            using var create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB)";
            create.ExecuteNonQuery();
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO tiles VALUES (3, 2, $row, $data)";
            // Slippy row 1 of 8 is TMS row 6.
            insert.Parameters.AddWithValue("$row", 6);
            insert.Parameters.AddWithValue("$data", MapTestSupport.Png(MapTestSupport.Water));
            insert.ExecuteNonQuery();
        }

        var fetcher = new FakeTileFetcher(_ => null);
        var service = Create(fetcher);

        Assert.IsNotNull(await service.LoadAsync(new MapTileKey(3, 2, 1), online: false));
        Assert.IsNull(await service.LoadAsync(new MapTileKey(3, 2, 2), online: false));
        Assert.AreEqual(0, fetcher.Requests);
    }

    [TestMethod]
    public async Task ATileTheServerCannotSupplyIsNotAskedForAgainAtOnce()
    {
        var fetcher = new FakeTileFetcher(_ => null);
        var service = Create(fetcher);
        var key = new MapTileKey(6, 10, 10);

        Assert.IsNull(await service.LoadAsync(key, online: true));
        Assert.IsNull(await service.LoadAsync(key, online: true));

        Assert.AreEqual(1, fetcher.Requests);
    }

    [TestMethod]
    public async Task ABodyThatIsNotAnImageIsNotSaved()
    {
        var service = Create(new FakeTileFetcher(_ => [1, 2, 3]));
        var key = new MapTileKey(2, 1, 1);

        Assert.IsNull(await service.LoadAsync(key, online: true));
        Assert.IsFalse(service.IsAvailableOffline(key));
    }

    [TestMethod]
    public async Task TilesOutsideTheWorldAreNeverRequested()
    {
        var fetcher = new FakeTileFetcher(_ => MapTestSupport.Png(MapTestSupport.Land));
        var service = Create(fetcher);

        Assert.IsNull(await service.LoadAsync(new MapTileKey(2, 4, 0), online: true));
        Assert.IsNull(await service.LoadAsync(new MapTileKey(2, 0, -1), online: true));
        Assert.IsNull(await service.LoadAsync(new MapTileKey(25, 0, 0), online: true));
        Assert.AreEqual(0, fetcher.Requests);
    }

    [TestMethod]
    public async Task LoadedTilesAreServedFromMemory()
    {
        var fetcher = new FakeTileFetcher(_ => MapTestSupport.Png(MapTestSupport.Land));
        var service = Create(fetcher);
        var key = new MapTileKey(1, 0, 0);

        Assert.IsNull(service.TryGetLoaded(key));
        await service.LoadAsync(key, online: true);
        await service.LoadAsync(key, online: true);

        Assert.IsNotNull(service.TryGetLoaded(key));
        Assert.AreEqual(1, fetcher.Requests);
    }

    [TestMethod]
    public async Task DownloadSavesOnlyTheTilesNotAlreadySaved()
    {
        var fetcher = new FakeTileFetcher(_ => MapTestSupport.Png(MapTestSupport.Land));
        var service = Create(fetcher);
        await service.LoadAsync(new MapTileKey(2, 0, 0), online: true);
        var reported = 0;

        var saved = await service.DownloadAsync(
            [new MapTileKey(2, 0, 0), new MapTileKey(2, 1, 0), new MapTileKey(2, 2, 0)],
            new SyncProgress(n => reported = n));

        Assert.AreEqual(2, saved);
        Assert.AreEqual(3, fetcher.Requests);
        Assert.IsTrue(service.IsAvailableOffline(new MapTileKey(2, 2, 0)));
        Assert.AreEqual(3, reported);
    }

    [TestMethod]
    public async Task TheTileAddressComesFromTheSettings()
    {
        var settings = new MemorySettings();
        settings.Set(MapStore.TileUrlKey, "https://tiles.example/{z}/{x}/{y}.png?key=1");
        var fetcher = new FakeTileFetcher(_ => MapTestSupport.Png(MapTestSupport.Land));
        var service = new MapTileService(fetcher, new MapStore(settings), _folder);

        await service.LoadAsync(new MapTileKey(1, 1, 0), online: true);

        Assert.AreEqual("https://tiles.example/1/1/0.png?key=1", fetcher.Urls.Single());
    }

    /// <summary><see cref="Progress{T}"/> posts each report to the thread pool, so reports can arrive out of order; this one runs inline.</summary>
    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
