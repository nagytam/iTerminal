using iTerminal.Core.Data;

namespace iTerminal.Core.Services;

public class WeatherService
{
    public WeatherInfo? CurrentWeather { get; set; }

    /// <summary>
    /// Indicates whether the current weather data is from cache (true) or freshly fetched (false)
    /// </summary>
    public bool IsFromCache { get; set; } = false;

    /// <summary>
    /// Indicates whether a refresh is currently in progress
    /// </summary>
    public bool IsRefreshing { get; set; } = false;

    /// <summary>
    /// Whether the last attempt to fetch the weather failed (offline, no location, a bad response). Without this a failed
    /// first attempt looked exactly like one still in progress: a spinner, and no sign that nothing was coming.
    /// </summary>
    public bool LastRefreshFailed { get; set; } = false;
}
