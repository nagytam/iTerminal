using iTerminal.Core.Data;
using iTerminal.Core.Jobs;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class WeatherJobTest
{
    [Timeout(60_000)]
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

    [Timeout(60_000)]
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

    [Timeout(60_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_ShowsTheCachedWeatherAtOnceAndHoldsBackTheFirstRequest()
    {
        Mock<ICacheService> cache = new();
        cache.Setup(c => c.GetCachedWeatherAsync()).ReturnsAsync(new CachedWeatherInfo
        {
            LocationName = "Budapest",
            TemperatureC = 12.5,
            WeatherCode = 3,
            IsDay = true,
            CachedAtUtc = DateTime.UtcNow,
        });
        WeatherService service = new();
        WeatherJob job = new(service, cache.Object) { FirstRefreshDelay = TimeSpan.FromHours(1) };
        using CancellationTokenSource cancellation = new();

        Task running = job.ExecuteAsync(cancellation.Token);
        for (int attempt = 0; attempt < 200 && service.CurrentWeather is null; attempt++)
        {
            await Task.Delay(25);
        }

        Assert.IsNotNull(service.CurrentWeather, "The cached weather is shown without waiting for the delay");
        Assert.AreEqual("Budapest", service.CurrentWeather.LocationName);
        Assert.IsTrue(service.IsFromCache);
        await Task.Delay(300);
        Assert.IsFalse(service.IsRefreshing, "No request goes out before the first-refresh delay has passed");
        Assert.IsFalse(service.LastRefreshFailed);
        Assert.IsFalse(running.IsCompleted);

        cancellation.Cancel();
        await running.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
