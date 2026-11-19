using iTerminal.Core.Services.Weather;

namespace iTerminal.Core.Test.Services.Weather;

/// <summary>The forecast and place-search replies are read from Open-Meteo's JSON: the hours start at the current hour, a hole reads as zero, and a reply with no match is an empty list.</summary>
[TestClass]
public class OpenMeteoForecastParserTest
{
    private static readonly DateTimeOffset FetchedAt = new(2026, 11, 18, 10, 30, 0, TimeSpan.Zero);

    private static string Array(IEnumerable<string> items) => "[" + string.Join(",", items) + "]";

    /// <summary>Two days of hours from midnight on 18 November; the temperature of hour <c>i</c> is <c>i</c>, and the precipitation of hour 12 is a hole (null).</summary>
    private static string ForecastJson(int? gapHour = null, int valueCount = 48)
    {
        var start = new DateTime(2026, 11, 18);
        var times = Array(Enumerable.Range(0, 48).Select(i => $"\"{start.AddHours(i):yyyy-MM-ddTHH:mm}\""));
        var temperatures = Array(Enumerable.Range(0, valueCount).Select(i => i == gapHour ? "null" : i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var precipitation = Array(Enumerable.Range(0, 48).Select(i => i == 12 ? "null" : "0.5"));
        var chance = Array(Enumerable.Range(0, 48).Select(i => "20"));
        var codes = Array(Enumerable.Range(0, 48).Select(i => "3"));
        return "{"
            + "\"current\":{\"time\":\"2026-11-18T10:30\",\"temperature_2m\":12.4,\"is_day\":1,\"weather_code\":2},"
            + $"\"hourly\":{{\"time\":{times},\"temperature_2m\":{temperatures},\"precipitation\":{precipitation},\"precipitation_probability\":{chance},\"weather_code\":{codes}}},"
            + "\"daily\":{\"time\":[\"2026-11-18\",\"2026-11-19\"],\"weather_code\":[3,63],\"temperature_2m_max\":[12,9],\"temperature_2m_min\":[4,2],"
            + "\"precipitation_sum\":[0,4.5],\"precipitation_probability_max\":[10,80]}"
            + "}";
    }

    [TestMethod]
    public void ParseForecast_StartsAtTheCurrentHourAndTakes24Hours()
    {
        var forecast = OpenMeteoForecastProvider.ParseForecast(ForecastJson(), FetchedAt);

        Assert.AreEqual(24, forecast.Hourly.Count);
        Assert.AreEqual(new DateTime(2026, 11, 18, 10, 0, 0), forecast.Hourly[0].Time, "the 10:00 hour, not midnight");
        Assert.AreEqual(new DateTime(2026, 11, 19, 9, 0, 0), forecast.Hourly[^1].Time);
        Assert.AreEqual(10, forecast.Hourly[0].TemperatureC, 1e-9);
    }

    [TestMethod]
    public void ParseForecast_ReadsTheConditionsNow()
    {
        var forecast = OpenMeteoForecastProvider.ParseForecast(ForecastJson(), FetchedAt);

        Assert.AreEqual(12.4, forecast.Current.TemperatureC, 1e-9);
        Assert.AreEqual(2, forecast.Current.WeatherCode);
        Assert.IsTrue(forecast.Current.IsDay);
        Assert.AreEqual(FetchedAt, forecast.FetchedAt);
    }

    [TestMethod]
    public void ParseForecast_ReadsEachDay()
    {
        var forecast = OpenMeteoForecastProvider.ParseForecast(ForecastJson(), FetchedAt);

        Assert.AreEqual(2, forecast.Daily.Count);
        Assert.AreEqual(new DateOnly(2026, 11, 19), forecast.Daily[1].Date);
        Assert.AreEqual(2, forecast.Daily[1].MinC, 1e-9);
        Assert.AreEqual(9, forecast.Daily[1].MaxC, 1e-9);
        Assert.AreEqual(4.5, forecast.Daily[1].PrecipitationMm, 1e-9);
        Assert.AreEqual(80, forecast.Daily[1].PrecipitationChanceMax);
        Assert.AreEqual(63, forecast.Daily[1].WeatherCode);
    }

    [TestMethod]
    public void ParseForecast_ReadsAHoleAsZeroRatherThanFailing()
    {
        var forecast = OpenMeteoForecastProvider.ParseForecast(ForecastJson(), FetchedAt);

        // The hole is at 12:00 on the 18th, which is inside the 24 hours from 10:00, so the view reads it.
        var holeHour = forecast.Hourly.Single(hour => hour.Time == new DateTime(2026, 11, 18, 12, 0, 0));
        Assert.AreEqual(0, holeHour.PrecipitationMm, 1e-9);
        Assert.AreEqual(0.5, forecast.Hourly[0].PrecipitationMm, 1e-9);
    }

    [TestMethod]
    public void ParseForecast_LeavesOutAnHourWithNoTemperatureRatherThanShowingZero()
    {
        // Hour 12 has no temperature in the feed: it is left out, and the window reaches one hour further to still hold 24.
        var json = ForecastJson(gapHour: 12);

        var forecast = OpenMeteoForecastProvider.ParseForecast(json, FetchedAt);

        Assert.AreEqual(24, forecast.Hourly.Count);
        Assert.IsFalse(forecast.Hourly.Any(hour => hour.Time == new DateTime(2026, 11, 18, 12, 0, 0)), "the gap hour is not shown as 0 °C");
        Assert.IsTrue(forecast.Hourly.All(hour => hour.TemperatureC != 0), "no hour reads as a false zero");
    }

    [TestMethod]
    public void ParseForecast_AValueArrayShorterThanItsTimesIsAGapNotAFailure()
    {
        // The temperatures stop at hour 19: the hours from 10:00 to 19:00 are kept, the rest are gaps.
        var forecast = OpenMeteoForecastProvider.ParseForecast(ForecastJson(valueCount: 20), FetchedAt);

        Assert.AreEqual(10, forecast.Hourly.Count);
        Assert.AreEqual(new DateTime(2026, 11, 18, 19, 0, 0), forecast.Hourly[^1].Time);
    }

    [TestMethod]
    public void ParseSearch_ReadsEachPlaceWithItsRegionAndCountry()
    {
        const string json = """
            {"results":[
              {"name":"Budapest","latitude":47.49835,"longitude":19.04045,"country":"Hungary","admin1":"Budapest","timezone":"Europe/Budapest"},
              {"name":"Budapest","latitude":33.71511,"longitude":-85.21801,"country":"United States","admin1":"Georgia","timezone":"America/New_York"}
            ],"generationtime_ms":0.2}
            """;

        var hits = OpenMeteoForecastProvider.ParseSearch(json);

        Assert.AreEqual(2, hits.Count);
        Assert.AreEqual(new PlaceHit("Budapest", "Budapest", "Hungary", 47.49835, 19.04045, "Europe/Budapest"), hits[0]);
        Assert.AreEqual("Georgia", hits[1].Region);
        Assert.AreEqual("America/New_York", hits[1].Timezone);
    }

    [TestMethod]
    public void ParseForecast_AMissingCurrentWeatherCodeFailsRatherThanReadingClearSky()
    {
        var json = ForecastJson().Replace(",\"weather_code\":2}", "}", StringComparison.Ordinal);

        Assert.ThrowsExactly<KeyNotFoundException>(() => OpenMeteoForecastProvider.ParseForecast(json, FetchedAt));
    }

    [TestMethod]
    public void ParseForecast_ANullTimestampLeavesOutItsHourRatherThanFailingTheForecast()
    {
        var json = ForecastJson().Replace("\"2026-11-18T12:00\"", "null", StringComparison.Ordinal);

        var forecast = OpenMeteoForecastProvider.ParseForecast(json, FetchedAt);

        Assert.AreEqual(24, forecast.Hourly.Count);
        Assert.IsFalse(forecast.Hourly.Any(hour => hour.Time == new DateTime(2026, 11, 18, 12, 0, 0)));
    }

    [TestMethod]
    public void ParseSearch_LeavesOutAPlaceWhoseCoordinatesAreNull()
    {
        const string json = """{"results":[{"name":"Nowhere","latitude":null,"longitude":null,"country":"Nowhere"},{"name":"Budapest","latitude":47.5,"longitude":19.0,"country":"Hungary"}]}""";

        var hits = OpenMeteoForecastProvider.ParseSearch(json);

        Assert.AreEqual(1, hits.Count, "a null coordinate is no coordinate");
        Assert.AreEqual("Budapest", hits[0].Name);
    }

    [TestMethod]
    public void ParseSearch_LeavesOutAPlaceWithNoCoordinates()
    {
        const string json = """{"results":[{"name":"Nowhere","country":"Nowhere"},{"name":"Budapest","latitude":47.5,"longitude":19.0,"country":"Hungary"}]}""";

        var hits = OpenMeteoForecastProvider.ParseSearch(json);

        Assert.AreEqual(1, hits.Count, "the place with no coordinates is not saved at 0,0");
        Assert.AreEqual("Budapest", hits[0].Name);
    }

    [TestMethod]
    public void ParseSearch_AReplyWithNoResultsPropertyIsNoMatch()
    {
        Assert.AreEqual(0, OpenMeteoForecastProvider.ParseSearch("{\"generationtime_ms\":0.1}").Count);
    }
}
