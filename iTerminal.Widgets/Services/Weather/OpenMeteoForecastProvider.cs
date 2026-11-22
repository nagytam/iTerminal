using System.Globalization;
using System.Text.Json;

namespace iTerminal.Widgets.Services.Weather;

/// <summary>Where forecasts and place names come from and where they are looked up. Async: every call is a network round trip.</summary>
public interface IForecastProvider
{
    /// <summary>The forecast for a place: the conditions now, the next 24 hours and the next 7 days.</summary>
    Task<Forecast> FetchAsync(SavedPlace place, CancellationToken cancellationToken = default);

    /// <summary>Places whose name matches <paramref name="query"/>. Empty when none match.</summary>
    Task<IReadOnlyList<PlaceHit>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Open-Meteo: the forecast endpoint and the geocoding search. Both need no key. The forecast uses Celsius and the place's own
/// time zone, so the hours and days are the place's, not this machine's.
/// </summary>
public sealed class OpenMeteoForecastProvider : IForecastProvider
{
    private const string ForecastUrl = "https://api.open-meteo.com/v1/forecast";
    private const string SearchUrl = "https://geocoding-api.open-meteo.com/v1/search";

    private static readonly HttpClient SharedClient = CreateClient();

    private readonly HttpClient _client;
    private readonly TimeProvider _time;

    public OpenMeteoForecastProvider()
        : this(SharedClient, TimeProvider.System)
    {
    }

    internal OpenMeteoForecastProvider(HttpClient client, TimeProvider time)
    {
        _client = client;
        _time = time;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("iTerminal");
        return client;
    }

    public async Task<Forecast> FetchAsync(SavedPlace place, CancellationToken cancellationToken = default)
    {
        var latitude = place.Latitude.ToString(CultureInfo.InvariantCulture);
        var longitude = place.Longitude.ToString(CultureInfo.InvariantCulture);
        var url = $"{ForecastUrl}?latitude={latitude}&longitude={longitude}"
            + "&current=temperature_2m,is_day,weather_code"
            + "&hourly=temperature_2m,precipitation_probability,precipitation,weather_code"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,precipitation_probability_max"
            + "&forecast_days=7&temperature_unit=celsius&timezone=auto";
        using var response = await _client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseForecast(json, _time.GetUtcNow());
    }

    public async Task<IReadOnlyList<PlaceHit>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"{SearchUrl}?name={Uri.EscapeDataString(query)}&count=10&language=en&format=json";
        using var response = await _client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseSearch(json);
    }

    /// <summary>The forecast from Open-Meteo's JSON. The hours start at the current hour; the days are the seven it returns.</summary>
    internal static Forecast ParseForecast(string json, DateTimeOffset fetchedAt)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var currentElement = root.GetProperty("current");
        var currentTime = DateTime.Parse(currentElement.GetProperty("time").GetString()!, CultureInfo.InvariantCulture);
        // A forecast without the temperature now is no forecast: it throws, and the dialog says the service could not be reached.
        var current = new CurrentConditions(
            currentTime,
            currentElement.GetProperty("temperature_2m").GetDouble(),
            currentElement.GetProperty("weather_code").GetInt32(),
            ReadInt(currentElement, "is_day") == 1);

        var hourly = root.GetProperty("hourly");
        var hourTimes = hourly.GetProperty("time");
        var hourTemperatures = hourly.GetProperty("temperature_2m");
        var hourPrecipitation = hourly.GetProperty("precipitation");
        var hourChance = hourly.GetProperty("precipitation_probability");
        var hourCodes = hourly.GetProperty("weather_code");
        var hours = new List<HourlyPoint>();
        var hourStart = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day, currentTime.Hour, 0, 0);
        for (var i = 0; i < hourTimes.GetArrayLength(); i++)
        {
            if (hourTimes[i].ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var time = DateTime.Parse(hourTimes[i].GetString()!, CultureInfo.InvariantCulture);
            if (time < hourStart)
            {
                continue;
            }

            // An hour with no temperature or code is left out: shown as 0 °C it would be a false reading.
            var temperature = ReadNumber(hourTemperatures, i);
            var code = ReadNumber(hourCodes, i);
            if (temperature is null || code is null)
            {
                continue;
            }

            hours.Add(new HourlyPoint(time, temperature.Value, ReadNumber(hourPrecipitation, i) ?? 0, (int)(ReadNumber(hourChance, i) ?? 0), (int)code.Value));
            if (hours.Count == 24)
            {
                break;
            }
        }

        var daily = root.GetProperty("daily");
        var dayTimes = daily.GetProperty("time");
        var dayCodes = daily.GetProperty("weather_code");
        var dayMax = daily.GetProperty("temperature_2m_max");
        var dayMin = daily.GetProperty("temperature_2m_min");
        var dayPrecipitation = daily.GetProperty("precipitation_sum");
        var dayChance = daily.GetProperty("precipitation_probability_max");
        var days = new List<DailyPoint>();
        for (var i = 0; i < dayTimes.GetArrayLength(); i++)
        {
            // A day with no date, minimum, maximum or code is left out, for the same reason as an hour.
            if (dayTimes[i].ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var min = ReadNumber(dayMin, i);
            var max = ReadNumber(dayMax, i);
            var dayCode = ReadNumber(dayCodes, i);
            if (min is null || max is null || dayCode is null)
            {
                continue;
            }

            days.Add(new DailyPoint(
                DateOnly.Parse(dayTimes[i].GetString()!, CultureInfo.InvariantCulture),
                min.Value,
                max.Value,
                ReadNumber(dayPrecipitation, i) ?? 0,
                (int)(ReadNumber(dayChance, i) ?? 0),
                (int)dayCode.Value));
        }

        return new Forecast(current, hours, days, fetchedAt);
    }

    /// <summary>The places in Open-Meteo's geocoding reply. A reply with no <c>results</c> property means no match.</summary>
    internal static IReadOnlyList<PlaceHit> ParseSearch(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var hits = new List<PlaceHit>();
        foreach (var result in results.EnumerateArray())
        {
            // A place with no coordinates cannot be forecast: it is left out rather than saved at 0,0.
            if (!HasNumber(result, "latitude") || !HasNumber(result, "longitude"))
            {
                continue;
            }

            hits.Add(new PlaceHit(
                Name: ReadString(result, "name"),
                Region: ReadString(result, "admin1"),
                Country: ReadString(result, "country"),
                Latitude: ReadDouble(result, "latitude"),
                Longitude: ReadDouble(result, "longitude"),
                Timezone: ReadString(result, "timezone")));
        }

        return hits;
    }

    private static bool HasNumber(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number;

    private static double ReadDouble(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    private static int ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    /// <summary>The number at <paramref name="index"/>, or null when the service sent a gap there.</summary>
    private static double? ReadNumber(JsonElement array, int index) =>
        index < array.GetArrayLength() && array[index].ValueKind == JsonValueKind.Number ? array[index].GetDouble() : null;

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
