using System.Globalization;
using iTerminal.Widgets.Services.Weather;

namespace iTerminal.Widgets.Test.Services.Weather;

/// <summary>The key of a saved place is stored in the settings, so it must read the same whatever the culture of the machine.</summary>
[TestClass]
public class SavedPlaceTest
{
    [TestMethod]
    public void TheKeyIsTheSameInAnyCulture()
    {
        var place = new SavedPlace("Budapest", "Budapest", "Hungary", 47.4979, 19.0402, "Europe/Budapest");
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var english = place.Key;
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.AreEqual(english, place.Key);
            StringAssert.Contains(place.Key, "47.498|", "a dot, not a comma");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
