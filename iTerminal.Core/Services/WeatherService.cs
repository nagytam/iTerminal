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
}
