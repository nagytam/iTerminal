using iConsole.Display;
using iTerminal.Core.Services.Map;

namespace iTerminal.Core.Model.Map;

/// <summary>What the map looks like for one window size: a grid of characters and how complete it is.</summary>
public sealed class MapFrame(int columns, int rows, MapCell[] cells, int loading, int unavailable, int centerColumn, int centerRow)
{
    public int Columns { get; } = columns;
    public int Rows { get; } = rows;
    public MapCell[] Cells { get; } = cells;

    /// <summary>Tiles still being fetched or read.</summary>
    public int Loading { get; } = loading;

    /// <summary>Tiles no source could supply (offline and not saved, or the server refused).</summary>
    public int Unavailable { get; } = unavailable;

    public int CenterColumn { get; } = centerColumn;
    public int CenterRow { get; } = centerRow;

    public MapCell this[int column, int row] => Cells[row * Columns + column];

    /// <summary>The frame as plain text, one line per row.</summary>
    public string ToText()
    {
        var builder = new System.Text.StringBuilder(Rows * (Columns + 1));
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                builder.Append(this[column, row].Char);
            }

            if (row < Rows - 1)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }
}

/// <summary>
/// The state of the Map: where it looks (centre and zoom), whether it may use the network, and the tiles
/// it has asked for. <see cref="BuildFrame"/> draws from the tiles already in memory and starts background
/// loads for the rest; each load that lands moves <see cref="Version"/>, which is what tells the dialog to draw
/// again. Nothing here blocks on the disk or the network.
/// </summary>
public sealed class MapModel : IDisposable
{
    /// <summary>The most tiles "save for offline" will fetch in one go; the OSM tile policy discourages bulk downloads.</summary>
    public const int MaxDownloadTiles = 120;

    private const int FallbackLevels = 3;

    private readonly IMapTileService _tiles;
    private readonly MapStore _store;
    private readonly object _sync = new();
    private readonly HashSet<MapTileKey> _inflight = [];
    private readonly Dictionary<MapTileKey, bool> _unavailable = [];
    private readonly CancellationTokenSource _cancellation = new();
    private int _version;
    private string _message = string.Empty;

    public MapModel(IMapTileService tiles, MapStore store)
    {
        _tiles = tiles;
        _store = store;
        (Latitude, Longitude, Zoom) = store.LoadView();
        Zoom = Math.Clamp(Zoom, MapProjection.MinZoom, MapProjection.MaxZoom);
        Online = store.Online;
    }

    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public int Zoom { get; private set; }

    /// <summary>When false the map reads only saved tiles and offline files and never opens a connection.</summary>
    public bool Online { get; private set; }

    public MapStore Store => _store;

    public IMapTileService Tiles => _tiles;

    /// <summary>Moves whenever something the picture depends on changed behind the dialog's back.</summary>
    public int Version => Volatile.Read(ref _version);

    public string Message
    {
        get
        {
            lock (_sync)
            {
                return _message;
            }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    public void Say(string message)
    {
        lock (_sync)
        {
            _message = message;
        }

        Interlocked.Increment(ref _version);
    }

    #region Navigation

    /// <summary>Moves the view by whole characters; east and south are positive.</summary>
    public void Pan(double columns, double rows)
    {
        var (x, y) = MapProjection.ToPixel(Latitude, Longitude, Zoom);
        MoveTo(x + columns * MapAsciiRenderer.CellWidth, y + rows * MapAsciiRenderer.CellHeight);
    }

    public void CenterOn(double latitude, double longitude, int? zoom = null)
    {
        Latitude = MapProjection.ClampLatitude(latitude);
        Longitude = MapProjection.WrapLongitude(longitude);
        if (zoom is { } z)
        {
            Zoom = Math.Clamp(z, MapProjection.MinZoom, MapProjection.MaxZoom);
        }

        Persist();
    }

    public bool ZoomBy(int delta)
    {
        var zoom = Math.Clamp(Zoom + delta, MapProjection.MinZoom, MapProjection.MaxZoom);
        if (zoom == Zoom)
        {
            return false;
        }

        Zoom = zoom;
        Persist();
        return true;
    }

    /// <summary>Zooms while keeping the spot under character (<paramref name="column"/>, <paramref name="row"/>) where it is.</summary>
    public bool ZoomAround(int delta, int column, int row, int columns, int rows)
    {
        var (latitude, longitude) = CellToCoordinate(column, row, columns, rows);
        if (!ZoomBy(delta))
        {
            return false;
        }

        var (px, py) = MapProjection.ToPixel(latitude, longitude, Zoom);
        MoveTo(
            px - (column + 0.5 - columns / 2.0) * MapAsciiRenderer.CellWidth,
            py - (row + 0.5 - rows / 2.0) * MapAsciiRenderer.CellHeight);
        return true;
    }

    public void SetOnline(bool online)
    {
        if (Online == online)
        {
            return;
        }

        Online = online;
        _store.Online = online;
        lock (_sync)
        {
            // A tile that the other mode could not supply may be available in this one.
            foreach (var key in _unavailable.Where(pair => pair.Value != online).Select(pair => pair.Key).ToList())
            {
                _unavailable.Remove(key);
            }
        }

        Say(online ? "Online: missing tiles are fetched from the tile server and saved." : "Offline: only saved tiles and .mbtiles files are used.");
    }

    /// <summary>Forgets which tiles failed, so the next draw asks for them again.</summary>
    public void Retry()
    {
        lock (_sync)
        {
            _unavailable.Clear();
        }

        Interlocked.Increment(ref _version);
    }

    private void MoveTo(double x, double y)
    {
        var size = MapProjection.WorldSize(Zoom);
        (Latitude, Longitude) = MapProjection.FromPixel(x, Math.Clamp(y, 0, size - 1), Zoom);
        Persist();
    }

    private void Persist() => _store.SaveView(Latitude, Longitude, Zoom);

    /// <summary>The map position under character (<paramref name="column"/>, <paramref name="row"/>) of a grid this size.</summary>
    public (double Latitude, double Longitude) CellToCoordinate(int column, int row, int columns, int rows)
    {
        var (left, top) = Origin(columns, rows);
        return MapProjection.FromPixel(
            left + (column + 0.5) * MapAsciiRenderer.CellWidth,
            top + (row + 0.5) * MapAsciiRenderer.CellHeight,
            Zoom);
    }

    /// <summary>Where a position falls in a grid this size, or null when it is outside it.</summary>
    public (int Column, int Row)? CoordinateToCell(double latitude, double longitude, int columns, int rows)
    {
        var (left, top) = Origin(columns, rows);
        var (x, y) = MapProjection.ToPixel(latitude, longitude, Zoom);
        var worldSize = MapProjection.WorldSize(Zoom);

        // The view can show the same longitude twice when it is wider than the world; take the nearest copy.
        var (cx, _) = MapProjection.ToPixel(Latitude, Longitude, Zoom);
        if (x - cx > worldSize / 2)
        {
            x -= worldSize;
        }
        else if (cx - x > worldSize / 2)
        {
            x += worldSize;
        }

        var column = (int)Math.Floor((x - left) / MapAsciiRenderer.CellWidth);
        var row = (int)Math.Floor((y - top) / MapAsciiRenderer.CellHeight);
        return column >= 0 && column < columns && row >= 0 && row < rows ? (column, row) : null;
    }

    /// <summary>The world pixel at the top-left of the grid, snapped to a whole character so no character straddles two tiles.</summary>
    private (double Left, double Top) Origin(int columns, int rows)
    {
        var (cx, cy) = MapProjection.ToPixel(Latitude, Longitude, Zoom);
        var left = Math.Floor((cx - columns * MapAsciiRenderer.CellWidth / 2.0) / MapAsciiRenderer.CellWidth) * MapAsciiRenderer.CellWidth;
        var top = Math.Floor((cy - rows * MapAsciiRenderer.CellHeight / 2.0) / MapAsciiRenderer.CellHeight) * MapAsciiRenderer.CellHeight;
        return (left, top);
    }

    #endregion

    #region Drawing

    /// <summary>
    /// Draws the map into a grid of <paramref name="columns"/> × <paramref name="rows"/> characters, with the
    /// centre marked <c>X</c> and each of <paramref name="places"/> that is in view marked <c>P</c>. A tile that has
    /// not arrived yet is drawn from the next zoom level out when that is in memory (blurry, not blank).
    /// </summary>
    public MapFrame BuildFrame(int columns, int rows, IReadOnlyList<MapPlace>? places = null)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        var cells = new MapCell[columns * rows];
        var (left, top) = Origin(columns, rows);
        var perSide = MapProjection.TilesPerSide(Zoom);
        var worldSize = MapProjection.WorldSize(Zoom);
        var seen = new HashSet<MapTileKey>();
        var wanted = new List<MapTileKey>();

        for (var row = 0; row < rows; row++)
        {
            var wy = top + row * MapAsciiRenderer.CellHeight;
            for (var column = 0; column < columns; column++)
            {
                var wx = left + column * MapAsciiRenderer.CellWidth;
                if (wy < 0 || wy >= worldSize)
                {
                    cells[row * columns + column] = new MapCell(' ', Color.Gray);
                    continue;
                }

                var tileX = (int)Math.Floor(wx / MapProjection.TileSize);
                var key = new MapTileKey(Zoom, ((tileX % perSide) + perSide) % perSide, (int)(wy / MapProjection.TileSize));
                var u = wx - tileX * MapProjection.TileSize;
                var v = wy - key.Y * MapProjection.TileSize;

                var cell = Sample(key, u, v, out var state);
                if (state != TileState.Ready && seen.Add(key))
                {
                    wanted.Add(key);
                }

                cells[row * columns + column] = cell is { } drawn ? drawn : Placeholder(column, row, state);
            }
        }

        var loading = Request(wanted, left, top, columns, rows);
        var unavailable = seen.Count(IsUnavailable);

        foreach (var place in places ?? [])
        {
            if (CoordinateToCell(place.Latitude, place.Longitude, columns, rows) is { } at)
            {
                cells[at.Row * columns + at.Column] = new MapCell('P', Color.Magenta, true);
            }
        }

        var centerColumn = columns / 2;
        var centerRow = rows / 2;
        if (CoordinateToCell(Latitude, Longitude, columns, rows) is { } center)
        {
            (centerColumn, centerRow) = center;
        }

        cells[centerRow * columns + centerColumn] = new MapCell('X', Color.Red, true);
        return new MapFrame(columns, rows, cells, loading, unavailable, centerColumn, centerRow);
    }

    private enum TileState
    {
        Ready,
        Loading,
        Unavailable,
    }

    private MapCell? Sample(MapTileKey key, double u, double v, out TileState state)
    {
        if (_tiles.TryGetLoaded(key) is { } tile)
        {
            state = TileState.Ready;
            return MapAsciiRenderer.Render(tile, u, v, MapAsciiRenderer.CellWidth, MapAsciiRenderer.CellHeight);
        }

        state = IsUnavailable(key) ? TileState.Unavailable : TileState.Loading;

        // Draw from a tile further out while this one is on its way (or cannot come).
        var ancestor = key;
        for (var level = 1; level <= FallbackLevels && ancestor.Zoom > 0; level++)
        {
            ancestor = ancestor.Parent;
            if (_tiles.TryGetLoaded(ancestor) is { } parent)
            {
                var divisor = 1 << level;
                var offsetU = (key.X & (divisor - 1)) * (MapProjection.TileSize / (double)divisor);
                var offsetV = (key.Y & (divisor - 1)) * (MapProjection.TileSize / (double)divisor);
                return MapAsciiRenderer.Render(parent, offsetU + u / divisor, offsetV + v / divisor,
                    MapAsciiRenderer.CellWidth / (double)divisor, MapAsciiRenderer.CellHeight / (double)divisor);
            }
        }

        return null;
    }

    private static MapCell Placeholder(int column, int row, TileState state) => state == TileState.Unavailable
        ? new MapCell(((column + row) & 1) == 0 ? '/' : ' ', Color.DarkGray)
        : new MapCell(((column + row) & 1) == 0 ? '.' : ' ', Color.DarkGray);

    private bool IsUnavailable(MapTileKey key)
    {
        lock (_sync)
        {
            return _unavailable.TryGetValue(key, out var online) && online == Online;
        }
    }

    /// <summary>Starts a background load for each tile not already on its way; returns how many are loading.</summary>
    private int Request(List<MapTileKey> wanted, double left, double top, int columns, int rows)
    {
        var centerX = (left + columns * MapAsciiRenderer.CellWidth / 2.0) / MapProjection.TileSize;
        var centerY = (top + rows * MapAsciiRenderer.CellHeight / 2.0) / MapProjection.TileSize;
        var perSide = MapProjection.TilesPerSide(Zoom);
        var loading = 0;
        foreach (var key in wanted
                     .Where(key => !IsUnavailable(key))
                     .OrderBy(key => Math.Abs(WrapDistance(key.X + 0.5 - centerX, perSide)) + Math.Abs(key.Y + 0.5 - centerY)))
        {
            loading++;
            lock (_sync)
            {
                if (!_inflight.Add(key))
                {
                    continue;
                }
            }

            _ = LoadInBackground(key, Online);
        }

        return loading;
    }

    private static double WrapDistance(double distance, int perSide)
    {
        distance %= perSide;
        if (distance > perSide / 2.0)
        {
            distance -= perSide;
        }
        else if (distance < -perSide / 2.0)
        {
            distance += perSide;
        }

        return distance;
    }

    private async Task LoadInBackground(MapTileKey key, bool online)
    {
        MapTile? tile = null;
        try
        {
            tile = await _tiles.LoadAsync(key, online, _cancellation.Token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or HttpRequestException)
        {
            // Treated as a tile nobody has.
        }

        lock (_sync)
        {
            _inflight.Remove(key);
            if (tile is null)
            {
                _unavailable[key] = online;
            }
        }

        Interlocked.Increment(ref _version);
    }

    #endregion

    #region Saving for offline use

    /// <summary>The tiles that cover a grid this size at the current zoom and the <paramref name="extraZooms"/> levels in, nearest zoom first.</summary>
    public IReadOnlyList<MapTileKey> TilesForView(int columns, int rows, int extraZooms)
    {
        var keys = new List<MapTileKey>();
        var (cx, cy) = MapProjection.ToPixel(Latitude, Longitude, Zoom);
        var halfWidth = columns * MapAsciiRenderer.CellWidth / 2.0;
        var halfHeight = rows * MapAsciiRenderer.CellHeight / 2.0;
        for (var zoom = Zoom; zoom <= Math.Min(MapProjection.MaxZoom, Zoom + extraZooms); zoom++)
        {
            var factor = Math.Pow(2, zoom - Zoom);
            var perSide = MapProjection.TilesPerSide(zoom);
            var first = (int)Math.Floor((cx - halfWidth) * factor / MapProjection.TileSize);
            var last = (int)Math.Floor((cx + halfWidth) * factor / MapProjection.TileSize);
            var firstY = Math.Max(0, (int)Math.Floor((cy - halfHeight) * factor / MapProjection.TileSize));
            var lastY = Math.Min(perSide - 1, (int)Math.Floor((cy + halfHeight) * factor / MapProjection.TileSize));
            for (var y = firstY; y <= lastY; y++)
            {
                for (var x = first; x <= last; x++)
                {
                    keys.Add(new MapTileKey(zoom, ((x % perSide) + perSide) % perSide, y));
                }
            }
        }

        return [.. keys.Distinct()];
    }

    /// <summary>
    /// Saves the tiles of the current view (and two zoom levels in) so they work offline. Online only, and
    /// capped at <see cref="MaxDownloadTiles"/> tiles per request, as OpenStreetMap asks for no bulk downloading.
    /// </summary>
    public bool StartSavingForOffline(int columns, int rows)
    {
        if (!Online)
        {
            Say("Switch to online mode (O) to save tiles.");
            return false;
        }

        var keys = TilesForView(columns, rows, 2);
        var truncated = keys.Count > MaxDownloadTiles;
        if (truncated)
        {
            keys = [.. keys.Take(MaxDownloadTiles)];
        }

        Say($"Saving {keys.Count} tiles for offline use{(truncated ? $" (limited to {MaxDownloadTiles})" : string.Empty)}…");
        _ = Task.Run(async () =>
        {
            try
            {
                var saved = await _tiles.DownloadAsync(keys, null, _cancellation.Token);
                Say(saved == 0 && keys.Any(key => !_tiles.IsAvailableOffline(key))
                    ? "No tiles could be saved. Is the tile server reachable?"
                    : $"Saved {saved} new tiles in {_tiles.Directory}. They work offline now.");
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
            {
                // The map was closed.
            }
        });
        return true;
    }

    #endregion
}
