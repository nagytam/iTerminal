using System.Globalization;
using System.Text.Json;

namespace iTerminal.Core.Services.Map;

/// <summary>A named spot on the map: a search result or a place the user saved.</summary>
public sealed record MapPlace(string Name, double Latitude, double Longitude);

/// <summary>Where the map was left and the places saved on it, kept in the settings like the other tools do.</summary>
public sealed class MapStore(ISettingsService settingsService)
{
    public const string ViewKey = "Map.View";
    public const string PlacesKey = "Map.Places";
    public const string TileUrlKey = "Map.TileUrl";
    public const string OnlineKey = "Map.Online";
    public const string DefaultTileUrl = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public const int MaxPlaces = 200;

    /// <summary>Budapest, at a zoom where the Danube and the bridges read.</summary>
    public static readonly MapPlace Home = new("Budapest", 47.4979, 19.0402);
    public const int HomeZoom = 12;

    private readonly object _lock = new();
    private List<MapPlace>? _places;

    /// <summary>The tile address template; <c>{z}</c>, <c>{x}</c> and <c>{y}</c> stand for the tile.</summary>
    public string TileUrl => settingsService.Get(TileUrlKey) is { Length: > 0 } url && url.Contains("{z}", StringComparison.Ordinal) ? url : DefaultTileUrl;

    public bool Online
    {
        get => settingsService.Get(OnlineKey) != "false";
        set => settingsService.Set(OnlineKey, value ? "true" : "false");
    }

    public (double Latitude, double Longitude, int Zoom) LoadView()
    {
        var parts = settingsService.Get(ViewKey)?.Split(';');
        if (parts is { Length: 3 }
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var zoom)
            && Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180)
        {
            return (latitude, longitude, zoom);
        }

        return (Home.Latitude, Home.Longitude, HomeZoom);
    }

    public void SaveView(double latitude, double longitude, int zoom) =>
        settingsService.Set(ViewKey, string.Create(CultureInfo.InvariantCulture, $"{latitude:0.######};{longitude:0.######};{zoom}"));

    public IReadOnlyList<MapPlace> Places
    {
        get
        {
            lock (_lock)
            {
                return [.. Loaded()];
            }
        }
    }

    public void AddPlace(MapPlace place)
    {
        lock (_lock)
        {
            var places = Loaded();
            places.RemoveAll(p => p.Name == place.Name);
            places.Insert(0, place);
            if (places.Count > MaxPlaces)
            {
                places.RemoveRange(MaxPlaces, places.Count - MaxPlaces);
            }

            Save(places);
        }
    }

    public void RemovePlace(MapPlace place)
    {
        lock (_lock)
        {
            var places = Loaded();
            if (places.RemoveAll(p => p.Name == place.Name && p.Latitude == place.Latitude && p.Longitude == place.Longitude) > 0)
            {
                Save(places);
            }
        }
    }

    private List<MapPlace> Loaded()
    {
        if (_places is not null)
        {
            return _places;
        }

        try
        {
            _places = settingsService.Get(PlacesKey) is { Length: > 0 } json
                ? JsonSerializer.Deserialize<List<MapPlace>>(json) ?? []
                : [];
        }
        catch (JsonException)
        {
            _places = [];
        }

        return _places;
    }

    private void Save(List<MapPlace> places) => settingsService.Set(PlacesKey, JsonSerializer.Serialize(places));
}
