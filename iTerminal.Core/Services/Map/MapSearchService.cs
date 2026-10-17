using System.Globalization;
using System.Text.Json;
using iTerminal.Core.Model.Map;
using iTerminal.Core.Services.Web;

namespace iTerminal.Core.Services.Map;

/// <summary>Finds places by name. Needs the network: there is no offline gazetteer.</summary>
public interface IMapSearchService
{
    Task<IReadOnlyList<MapPlace>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Looks names up with OpenStreetMap's Nominatim. A pair of coordinates ("47.5, 19.04") is understood without
/// the network, so a place can be jumped to offline.
/// </summary>
public sealed class MapSearchService(IWebFetcher fetcher) : IMapSearchService
{
    public const string Endpoint = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=10&q=";

    public async Task<IReadOnlyList<MapPlace>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return [];
        }

        if (MapProjection.TryParseCoordinates(query, out var latitude, out var longitude))
        {
            return [new MapPlace(MapProjection.FormatCoordinates(latitude, longitude), latitude, longitude)];
        }

        var response = await fetcher.FetchAsync(new WebFetchRequest(Endpoint + Uri.EscapeDataString(query)), cancellationToken);
        if (!response.IsSuccess)
        {
            throw new WebFetchException($"The search failed: {response.Status} {response.ReasonPhrase}".TrimEnd());
        }

        return Parse(response.Content);
    }

    /// <summary>Reads Nominatim's JSON array; entries without a usable position are skipped.</summary>
    public static IReadOnlyList<MapPlace> Parse(byte[] json)
    {
        var places = new List<MapPlace>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return places;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("lat", out var lat) && item.TryGetProperty("lon", out var lon)
                    && double.TryParse(lat.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                    && double.TryParse(lon.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
                {
                    var name = item.TryGetProperty("display_name", out var display) ? display.GetString() : null;
                    places.Add(new MapPlace(string.IsNullOrWhiteSpace(name) ? MapProjection.FormatCoordinates(latitude, longitude) : name, latitude, longitude));
                }
            }
        }
        catch (JsonException)
        {
            // An answer that is not JSON (an error page) is no results.
        }

        return places;
    }
}
