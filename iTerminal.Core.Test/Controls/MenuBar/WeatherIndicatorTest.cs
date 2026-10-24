using Microsoft.VisualStudio.TestTools.UnitTesting;
using iTerminal.Core.Controls.MenuBar;
using iTerminal.Core.Data;
using iTerminal.Core.Services;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Controls.MenuBar.Test;

[TestClass]
public class WeatherIndicatorTest
{
    private static (WeatherIndicator Indicator, WeatherService Service) Create(WeatherInfo? weather = null)
    {
        var service = new WeatherService { CurrentWeather = weather };
        return (new WeatherIndicator(service, TestTheme()), service);
    }

    private static WeatherInfo Budapest() => new() { LocationName = "Budapest", TemperatureC = 21.5 };

    [TestMethod]
    public void BeforeTheFirstReadingItSaysItIsLoading()
    {
        var (indicator, _) = Create();

        StringAssert.StartsWith(indicator.Describe(20), "Weather ");
    }

    [TestMethod]
    public void ShowsThePlaceAndTheTemperature()
    {
        var (indicator, _) = Create(Budapest());

        Assert.AreEqual($"Budapest {21.5:0.#}°C".PadRight(20), indicator.Describe(20));
    }

    [TestMethod]
    public void ACachedReadingUsesTheSameMarkerAsTheQuotes()
    {
        // It said "[CACHED]" beside the quotes' "[C]".
        var (indicator, service) = Create(Budapest());
        service.IsFromCache = true;

        StringAssert.StartsWith(indicator.Describe(40), QuoteIndicator.CachedMarker + " Budapest");
    }

    [TestMethod]
    public void TheTextFitsItsWidth()
    {
        var (indicator, service) = Create(new WeatherInfo { LocationName = "Llanfairpwllgwyngyll station", TemperatureC = 9 });
        service.IsFromCache = true;
        service.IsRefreshing = true;

        var text = indicator.Describe(20);

        Assert.AreEqual(20, text.Length);
    }

    [TestMethod]
    public void DrawFillsItsWidth()
    {
        var (indicator, _) = Create(Budapest());
        indicator.ViewSize = new iConsole.Data.Size(20, 1);
        var buffer = new iConsole.Test.Core.ScreenBuffer(20, 1);

        indicator.Draw(buffer);

        StringAssert.StartsWith(buffer.Line(0), "Budapest");
    }

    [TestMethod]
    public void AFailedAttempt_SaysUnavailableInsteadOfSpinningForever()
    {
        // Offline, the spinner used to run on with no sign that nothing was coming.
        var (indicator, service) = Create();
        service.LastRefreshFailed = true;

        Assert.AreEqual("Weather unavailable".PadRight(20), indicator.Describe(20));
    }

    [TestMethod]
    public void AFailedAttemptBeingRetried_KeepsTheSpinner()
    {
        var (indicator, service) = Create();
        service.LastRefreshFailed = true;
        service.IsRefreshing = true;

        StringAssert.StartsWith(indicator.Describe(20), "Weather ");
    }
}
