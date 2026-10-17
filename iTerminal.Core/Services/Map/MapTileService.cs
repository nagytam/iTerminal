using System.Collections.Concurrent;
using iTerminal.Core.Model.Map;
using iTerminal.Core.Services.Web;
using Microsoft.Data.Sqlite;

namespace iTerminal.Core.Services.Map;

/// <summary>Where the tiles of the Map come from.</summary>
public interface IMapTileService
{
    /// <summary>The folder that holds saved tiles (<c>tiles/z/x/y.png</c>) and any <c>.mbtiles</c> files.</summary>
    string Directory { get; }

    /// <summary>The decoded tile when it is already in memory; never touches the disk or the network.</summary>
    MapTile? TryGetLoaded(MapTileKey key);

    /// <summary>
    /// Finds a tile in memory, then in the saved tiles and <c>.mbtiles</c> files, and only when
    /// <paramref name="online"/> is set, on the tile server (saving what it gets). Null when no source has it.
    /// </summary>
    Task<MapTile?> LoadAsync(MapTileKey key, bool online, CancellationToken cancellationToken = default);

    /// <summary>True when the tile is on disk or in an offline file, whatever the memory holds.</summary>
    bool IsAvailableOffline(MapTileKey key);

    /// <summary>Fetches and saves the tiles that are not saved yet; returns how many were downloaded.</summary>
    Task<int> DownloadAsync(IReadOnlyList<MapTileKey> keys, IProgress<int>? progress, CancellationToken cancellationToken = default);
}

/// <summary>
/// Memory → disk → <c>.mbtiles</c> → tile server. Everything fetched is saved under <see cref="Directory"/> in
/// the same <c>z/x/y.png</c> layout an OpenStreetMap tile dump uses, so the map you have browsed keeps working
/// offline and a tile folder copied in works too. The server is asked politely: two requests at a time, the
/// fetcher's identifying User-Agent, and a tile that failed is not asked for again for half a minute.
/// </summary>
public sealed class MapTileService : IMapTileService, IDisposable
{
    private const int MemoryTiles = 192;
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

    private readonly IWebFetcher _fetcher;
    private readonly MapStore _store;
    private readonly ConcurrentDictionary<MapTileKey, MapTile> _memory = new();
    private readonly ConcurrentDictionary<MapTileKey, DateTime> _failed = new();
    private readonly SemaphoreSlim _network = new(2);
    private readonly object _packLock = new();
    private List<string>? _packs;

    public MapTileService(IWebFetcher fetcher, MapStore store, string directory)
    {
        _fetcher = fetcher;
        _store = store;
        Directory = directory;
    }

    public string Directory { get; }

    public void Dispose() => _network.Dispose();

    public MapTile? TryGetLoaded(MapTileKey key) => _memory.GetValueOrDefault(key);

    public bool IsAvailableOffline(MapTileKey key) => File.Exists(PathOf(key)) || ReadFromPacks(key) is not null;

    public async Task<MapTile?> LoadAsync(MapTileKey key, bool online, CancellationToken cancellationToken = default)
    {
        if (!IsValid(key))
        {
            return null;
        }

        if (_memory.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var tile = await Task.Run(() => ReadOffline(key), cancellationToken);
        if (tile is null && online)
        {
            tile = await FetchAsync(key, cancellationToken);
        }

        return tile is null ? null : Remember(key, tile);
    }

    public async Task<int> DownloadAsync(IReadOnlyList<MapTileKey> keys, IProgress<int>? progress, CancellationToken cancellationToken = default)
    {
        var downloaded = 0;
        var done = 0;
        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsValid(key) && !IsAvailableOffline(key))
            {
                _failed.TryRemove(key, out _);
                if (await FetchAsync(key, cancellationToken) is not null)
                {
                    downloaded++;
                }
            }

            progress?.Report(++done);
        }

        return downloaded;
    }

    private static bool IsValid(MapTileKey key) =>
        key.Zoom is >= MapProjection.MinZoom and <= MapProjection.MaxZoom
        && key.X >= 0 && key.Y >= 0
        && key.X < MapProjection.TilesPerSide(key.Zoom) && key.Y < MapProjection.TilesPerSide(key.Zoom);

    private string PathOf(MapTileKey key) =>
        Path.Combine(Directory, "tiles", key.Zoom.ToString(), key.X.ToString(), key.Y + ".png");

    private MapTile Remember(MapTileKey key, MapTile tile)
    {
        if (_memory.Count >= MemoryTiles)
        {
            // Not an LRU: dropping everything is cheap and the visible tiles are decoded again on the next draw.
            _memory.Clear();
        }

        _memory[key] = tile;
        return tile;
    }

    private MapTile? ReadOffline(MapTileKey key)
    {
        var path = PathOf(key);
        try
        {
            if (File.Exists(path) && MapTile.Decode(File.ReadAllBytes(path)) is { } saved)
            {
                return saved;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A tile that cannot be read is as good as missing.
        }

        return ReadFromPacks(key) is { } packed ? MapTile.Decode(packed) : null;
    }

    private async Task<MapTile?> FetchAsync(MapTileKey key, CancellationToken cancellationToken)
    {
        if (_failed.TryGetValue(key, out var failedAt) && DateTime.UtcNow - failedAt < RetryAfterFailure)
        {
            return null;
        }

        await _network.WaitAsync(cancellationToken);
        try
        {
            var url = _store.TileUrl
                .Replace("{z}", key.Zoom.ToString(), StringComparison.Ordinal)
                .Replace("{x}", key.X.ToString(), StringComparison.Ordinal)
                .Replace("{y}", key.Y.ToString(), StringComparison.Ordinal);
            var response = await _fetcher.FetchAsync(new WebFetchRequest(url), cancellationToken);
            var tile = response.IsSuccess ? MapTile.Decode(response.Content) : null;
            if (tile is null)
            {
                _failed[key] = DateTime.UtcNow;
                return null;
            }

            Save(key, response.Content);
            return tile;
        }
        catch (WebFetchException)
        {
            _failed[key] = DateTime.UtcNow;
            return null;
        }
        finally
        {
            _network.Release();
        }
    }

    private void Save(MapTileKey key, byte[] bytes)
    {
        var path = PathOf(key);
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The tile is still shown from memory; it just will not be there offline.
        }
    }

    private byte[]? ReadFromPacks(MapTileKey key)
    {
        List<string> packs;
        lock (_packLock)
        {
            _packs ??= FindPacks();
            packs = _packs;
        }

        foreach (var pack in packs)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={pack};Mode=ReadOnly;Pooling=False");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT tile_data FROM tiles WHERE zoom_level=$z AND tile_column=$x AND tile_row=$y";
                command.Parameters.AddWithValue("$z", key.Zoom);
                command.Parameters.AddWithValue("$x", key.X);
                // MBTiles rows count from the south, slippy-map rows from the north.
                command.Parameters.AddWithValue("$y", MapProjection.TilesPerSide(key.Zoom) - 1 - key.Y);
                if (command.ExecuteScalar() is byte[] { Length: > 0 } data)
                {
                    return data;
                }
            }
            catch (Exception exception) when (exception is SqliteException or IOException or InvalidOperationException)
            {
                // Not a raster MBTiles file, or one being written: skip it.
            }
        }

        return null;
    }

    private List<string> FindPacks()
    {
        try
        {
            return System.IO.Directory.Exists(Directory)
                ? [.. System.IO.Directory.EnumerateFiles(Directory, "*.mbtiles").Order(StringComparer.OrdinalIgnoreCase)]
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
