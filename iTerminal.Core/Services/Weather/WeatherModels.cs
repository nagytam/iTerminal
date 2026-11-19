using System.Globalization;

namespace iTerminal.Core.Services.Weather;

/// <summary>A place found by the geocoding search, before the user saves it.</summary>
public sealed record PlaceHit(string Name, string Region, string Country, double Latitude, double Longitude, string Timezone);

/// <summary>A place the user has saved. Its key identifies it in the settings and in the forecast cache.</summary>
public sealed record SavedPlace(string Name, string Region, string Country, double Latitude, double Longitude, string Timezone)
{
    /// <summary>Formatted in the invariant culture: the key is stored, so a comma decimal on one machine must not make another read a different place.</summary>
    public string Key { get; } = string.Join('|', Name, Region, Country, Latitude.ToString("F3", CultureInfo.InvariantCulture), Longitude.ToString("F3", CultureInfo.InvariantCulture));

    public static SavedPlace From(PlaceHit hit) => new(hit.Name, hit.Region, hit.Country, hit.Latitude, hit.Longitude, hit.Timezone);
}

/// <summary>The conditions now. Times are local to the place, as the service returns them.</summary>
public sealed record CurrentConditions(DateTime Time, double TemperatureC, int WeatherCode, bool IsDay);

/// <summary>One hour of the forecast.</summary>
public sealed record HourlyPoint(DateTime Time, double TemperatureC, double PrecipitationMm, int PrecipitationChance, int WeatherCode);

/// <summary>One day of the forecast.</summary>
public sealed record DailyPoint(DateOnly Date, double MinC, double MaxC, double PrecipitationMm, int PrecipitationChanceMax, int WeatherCode);

/// <summary>A forecast for one place: the conditions now, the hours from now on, and the days. Kept per place, so it survives a restart.</summary>
public sealed record Forecast(CurrentConditions Current, IReadOnlyList<HourlyPoint> Hourly, IReadOnlyList<DailyPoint> Daily, DateTimeOffset FetchedAt);
