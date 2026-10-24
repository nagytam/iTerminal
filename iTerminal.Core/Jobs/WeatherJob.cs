using iConsole.Jobs;
using iTerminal.Core.Data;
using iTerminal.Core.Services;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace iTerminal.Core.Jobs;

public class WeatherJob(WeatherService weatherService, ICacheService cacheService) : Job
{
    private static readonly HttpClient _httpClient = new();
    private readonly WeatherService _weatherService = weatherService;
    private readonly ICacheService _cacheService = cacheService;
    private WeatherLocation? _weatherLocation;

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        // Load from cache if available
        var cachedWeather = await _cacheService.GetCachedWeatherAsync();
        if (cachedWeather is not null)
        {
            _weatherService.CurrentWeather = new WeatherInfo
            {
                LocationName = cachedWeather.LocationName,
                TemperatureC = cachedWeather.TemperatureC,
                WeatherCode = cachedWeather.WeatherCode,
                IsDay = cachedWeather.IsDay,
                SourceDateTime = cachedWeather.SourceDateTime
            };
            _weatherService.IsFromCache = true;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _weatherService.IsRefreshing = true;
                _weatherLocation ??= await GetWeatherLocationAsync(cancellationToken);
                var weather = _weatherLocation is null
                    ? null
                    : await GetWeatherAsync(_weatherLocation.Value, cancellationToken);
                _weatherService.LastRefreshFailed = weather is null;
                if (weather is not null)
                {
                    _weatherService.CurrentWeather = weather;
                    _weatherService.IsFromCache = false;
                    // Update cache
                    await _cacheService.SetCachedWeatherAsync(weather);
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"{nameof(WeatherJob)}.{nameof(RunAsync)} failed: {exception}");
                _weatherLocation = null;
                _weatherService.LastRefreshFailed = true;
            }
            finally
            {
                _weatherService.IsRefreshing = false;
            }

            try
            {
                await Task.Delay(_weatherLocation is null ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(15), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task<WeatherLocation?> GetWeatherLocationAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync("http://ip-api.com/json/?fields=status,message,city,countryCode,lat,lon", cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseWeatherLocation(json);
    }

    private static async Task<WeatherInfo?> GetWeatherAsync(WeatherLocation location, CancellationToken cancellationToken)
    {
        var latitude = location.Latitude.ToString(CultureInfo.InvariantCulture);
        var longitude = location.Longitude.ToString(CultureInfo.InvariantCulture);
        var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,is_day,weather_code&temperature_unit=celsius&timezone=auto";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseWeather(json, location);
    }

    internal static WeatherLocation? ParseWeatherLocation(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!string.Equals(root.GetProperty("status").GetString(), "success", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var city = root.GetProperty("city").GetString();
        var countryCode = root.GetProperty("countryCode").GetString();
        if (string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(countryCode))
        {
            return null;
        }

        return new WeatherLocation(
            City: city,
            CountryCode: countryCode,
            Latitude: root.GetProperty("lat").GetDouble(),
            Longitude: root.GetProperty("lon").GetDouble());
    }

    internal static WeatherInfo? ParseWeather(string json, WeatherLocation location)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("current", out var current))
        {
            return null;
        }

        if (!current.TryGetProperty("temperature_2m", out var temperatureElement))
        {
            return null;
        }

        var temperature = temperatureElement.GetDouble();
        var weatherCode = current.TryGetProperty("weather_code", out var weatherCodeElement)
            ? weatherCodeElement.GetInt32()
            : 0;
        var isDay = current.TryGetProperty("is_day", out var isDayElement)
            && isDayElement.GetInt32() == 1;

        return new WeatherInfo
        {
            LocationName = $"{location.City}",
            TemperatureC = temperature,
            WeatherCode = weatherCode,
            IsDay = isDay,
            SourceDateTime = DateTime.UtcNow
        };
    }

    internal readonly record struct WeatherLocation(string City, string CountryCode, double Latitude, double Longitude);
}
