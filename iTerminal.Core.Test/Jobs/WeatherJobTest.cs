using iTerminal.Core.Jobs;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class WeatherJobTest
{
    [TestMethod]
    public void ParseWeatherLocation_ReturnsLocation_ForSuccessfulIpApiResponse()
    {
        var json = """
        {
          "status": "success",
          "countryCode": "HU",
          "city": "Budapest",
          "lat": 47.4979,
          "lon": 19.0402
        }
        """;

        var location = WeatherJob.ParseWeatherLocation(json);

        Assert.IsNotNull(location);
        Assert.AreEqual("Budapest", location.Value.City);
        Assert.AreEqual("HU", location.Value.CountryCode);
        Assert.AreEqual(47.4979, location.Value.Latitude, 0.0001);
        Assert.AreEqual(19.0402, location.Value.Longitude, 0.0001);
    }

    [TestMethod]
    public void ParseWeatherLocation_ReturnsNull_ForFailedIpApiResponse()
    {
        var json = """
        {
          "status": "fail",
          "message": "private range"
        }
        """;

        var location = WeatherJob.ParseWeatherLocation(json);

        Assert.IsNull(location);
    }

    [TestMethod]
    public void ParseWeather_ReturnsWeatherInfo_ForCurrentOpenMeteoResponse()
    {
        var json = """
        {
          "current": {
            "temperature_2m": 21.4,
            "is_day": 1,
            "weather_code": 3
          }
        }
        """;
        var location = new WeatherJob.WeatherLocation("Budapest", "HU", 47.4979, 19.0402);

        var weather = WeatherJob.ParseWeather(json, location);

        Assert.IsNotNull(weather);
        Assert.AreEqual("Budapest", weather.LocationName);
        Assert.AreEqual(21.4, weather.TemperatureC, 0.0001);
        Assert.AreEqual(3, weather.WeatherCode);
        Assert.IsTrue(weather.IsDay);
    }
}
