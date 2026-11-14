using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Services;

namespace iTerminal.Core.Controls.MenuBar;

/// <summary>The current temperature in the menu bar, marked the same way as the quotes when it is from the cache.</summary>
public class WeatherIndicator(WeatherService weatherService, IThemeService themeService) : iConsole.Controls.Control(themeService)
{
    private WeatherService _weatherService { get; set; } = weatherService;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        StartAutoRefresh();
    }

    /// <summary>The text to draw, fitted to <paramref name="width"/>.</summary>
    internal string Describe(int width)
    {
        var weather = _weatherService.CurrentWeather;

        string text;
        if (weather is null)
        {
            // A spinner means "still trying": once the last attempt has failed and none is running, say so.
            text = _weatherService.LastRefreshFailed && !_weatherService.IsRefreshing
                ? UiText.Get("Weather.Unavailable", "Weather unavailable")
                : string.Format(UiText.Get("Weather.Loading", "Weather {0}"), ProgressChar());
        }
        else
        {
            // "[cached]" as the quotes say it — it used to shout "[CACHED]" beside the quotes' "[C]".
            var refreshing = _weatherService.IsRefreshing ? $" {ProgressChar()}" : string.Empty;
            var reading = $"{weather.LocationName} {weather.TemperatureC:0.#}°C{refreshing}";

            // Longest first, as the quotes do: a narrow slot keeps the place and the temperature and says
            // "cached" with the quotes' "*". A middle cut ("[cached]...ch 4.8°C") loses the place and the marker.
            string[] candidates = _weatherService.IsFromCache
                ? [$"{QuoteIndicator.CachedMarker} {reading}", $"* {reading}"]
                : [reading];
            text = candidates.FirstOrDefault(c => c.Length <= width) ?? candidates[^1];
        }

        return (text.Shrink(width) ?? string.Empty).ExpandRight(width);
    }

    public override void Draw(IBuffer buffer)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        var weather = new ConxelString(Describe((int)ViewSize.Width), colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, weather);
    }
}
