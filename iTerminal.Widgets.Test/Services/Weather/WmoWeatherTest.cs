using iTerminal.Widgets.Services.Weather;

namespace iTerminal.Widgets.Test.Services.Weather;

/// <summary>Every WMO code Open-Meteo documents has a description and a glyph, so a forecast never shows a blank or a bad code.</summary>
[TestClass]
public class WmoWeatherTest
{
    /// <summary>The codes in Open-Meteo's table of WMO weather interpretation codes.</summary>
    private static readonly int[] DocumentedCodes = [0, 1, 2, 3, 45, 48, 51, 53, 55, 56, 57, 61, 63, 65, 66, 67, 71, 73, 75, 77, 80, 81, 82, 85, 86, 95, 96, 99];

    [TestMethod]
    public void EveryDocumentedCodeHasADescription()
    {
        CollectionAssert.AreEquivalent(DocumentedCodes, WmoWeather.KnownCodes.ToList());
        foreach (var code in DocumentedCodes)
        {
            Assert.AreNotEqual("Unknown", WmoWeather.Describe(code), $"code {code} has no description");
        }
    }

    [TestMethod]
    public void AnUnknownCodeReadsAsUnknownWithAQuestionMark()
    {
        Assert.AreEqual("Unknown", WmoWeather.Describe(1234));
        Assert.AreEqual('?', WmoWeather.Glyph(1234));
    }

    [TestMethod]
    public void AClearNightIsAMoonNotASun()
    {
        Assert.AreEqual('☀', WmoWeather.Glyph(0, isDay: true));
        Assert.AreEqual('☾', WmoWeather.Glyph(0, isDay: false));
        Assert.AreEqual('☂', WmoWeather.Glyph(63, isDay: false), "rain reads the same by night");
    }
}
