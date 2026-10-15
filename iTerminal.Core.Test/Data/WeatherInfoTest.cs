using iTerminal.Core.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Test.Data;

[TestClass]
public class WeatherInfoTest
{
    [TestMethod]
    public void CanInstantiateWeatherInfo_WithRequiredMembers()
    {
        var w = new WeatherInfo { LocationName = "X", TemperatureC = 1.23 };
        Assert.IsNotNull(w);
        Assert.AreEqual("X", w.LocationName);
    }

    [TestMethod]
    public void TemperatureC_SetAndGet()
    {
        var w = new WeatherInfo { LocationName = "L", TemperatureC = 12.34 };
        Assert.AreEqual(12.34, w.TemperatureC);
    }
}
