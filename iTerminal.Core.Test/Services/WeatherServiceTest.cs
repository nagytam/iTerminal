using iTerminal.Core.Data;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.Test;

/// <summary>The weather state the menu bar reads: nothing until a reading, and not failed until an attempt has failed.</summary>
[TestClass]
public class WeatherServiceTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void A_NewService_HasNoReadingAndHasFailedNothing()
    {
        var service = new WeatherService();

        Assert.IsNull(service.CurrentWeather);
        Assert.IsFalse(service.IsFromCache);
        Assert.IsFalse(service.IsRefreshing);
        Assert.IsFalse(service.LastRefreshFailed);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ARetrievedReading_IsKept()
    {
        var service = new WeatherService { CurrentWeather = new WeatherInfo { LocationName = "Oslo", TemperatureC = -3 } };

        Assert.AreEqual("Oslo", service.CurrentWeather!.LocationName);
    }
}
