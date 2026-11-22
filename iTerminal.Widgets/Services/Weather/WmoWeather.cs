using iConsole.Localization;

namespace iTerminal.Widgets.Services.Weather;

/// <summary>
/// The WMO weather codes Open-Meteo reports, as a description and a one-cell glyph. Intensity (light, heavy) is folded
/// into the description's group, so each group is one translated phrase.
/// </summary>
public static class WmoWeather
{
    private static readonly (int[] Codes, string Key, string English, char Glyph)[] Groups =
    [
        ([0], "Weather.Code.Clear", "Clear sky", '☀'),
        ([1], "Weather.Code.MainlyClear", "Mainly clear", '☀'),
        ([2], "Weather.Code.PartlyCloudy", "Partly cloudy", '◐'),
        ([3], "Weather.Code.Overcast", "Overcast", '☁'),
        ([45, 48], "Weather.Code.Fog", "Fog", '≡'),
        ([51, 53, 55], "Weather.Code.Drizzle", "Drizzle", '∴'),
        ([56, 57], "Weather.Code.FreezingDrizzle", "Freezing drizzle", '∴'),
        ([61, 63, 65], "Weather.Code.Rain", "Rain", '☂'),
        ([66, 67], "Weather.Code.FreezingRain", "Freezing rain", '☂'),
        ([71, 73, 75], "Weather.Code.Snow", "Snow", '❄'),
        ([77], "Weather.Code.SnowGrains", "Snow grains", '❄'),
        ([80, 81, 82], "Weather.Code.RainShowers", "Rain showers", '☂'),
        ([85, 86], "Weather.Code.SnowShowers", "Snow showers", '❄'),
        ([95], "Weather.Code.Thunderstorm", "Thunderstorm", 'ϟ'),
        ([96, 99], "Weather.Code.ThunderstormHail", "Thunderstorm with hail", 'ϟ'),
    ];

    /// <summary>Every code Open-Meteo documents, in order: a test checks that each one has a description.</summary>
    public static IReadOnlyList<int> KnownCodes { get; } = Groups.SelectMany(group => group.Codes).ToList();

    /// <summary>The description in the current language. An unknown code reads as "Unknown" rather than failing the view.</summary>
    public static string Describe(int code)
    {
        var group = Find(code);
        return group is { } found
            ? UiText.Get(found.Key, found.English)
            : UiText.Get("Weather.Code.Unknown", "Unknown");
    }

    public static char Glyph(int code, bool isDay = true)
    {
        var group = Find(code);
        if (group is not { } found)
        {
            return '?';
        }

        // The sun does not shine at night: a clear or mainly clear night reads as a moon.
        return !isDay && (code == 0 || code == 1) ? '☾' : found.Glyph;
    }

    private static (int[] Codes, string Key, string English, char Glyph)? Find(int code)
    {
        foreach (var group in Groups)
        {
            if (Array.IndexOf(group.Codes, code) >= 0)
            {
                return group;
            }
        }

        return null;
    }
}
