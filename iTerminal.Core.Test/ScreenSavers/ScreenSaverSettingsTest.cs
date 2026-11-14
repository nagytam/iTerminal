using iTerminal.Core.ScreenSavers;

namespace iTerminal.Core.Test.ScreenSavers;

[TestClass]
public class ScreenSaverSettingsTest
{
    [TestMethod]
    [Timeout(60_000)]
    [DataRow("Starfield", ScreenSaverKind.Starfield)]
    [DataRow("matrixrain", ScreenSaverKind.MatrixRain)]
    [DataRow("Clock", ScreenSaverKind.Clock)]
    [DataRow("Off", ScreenSaverKind.Off)]
    public void ParseKind_ReadsTheStoredName(string stored, ScreenSaverKind expected)
    {
        Assert.AreEqual(expected, ScreenSaverSettings.ParseKind(stored));
    }

    [TestMethod]
    [Timeout(60_000)]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("Fireworks")]
    [DataRow("123")]
    public void ParseKind_FallsBackToTheDefault_ForAMissingOrUnknownValue(string? stored)
    {
        Assert.AreEqual(ScreenSaverSettings.DefaultKind, ScreenSaverSettings.ParseKind(stored));
    }

    [TestMethod]
    [Timeout(60_000)]
    [DataRow("12", 12)]
    [DataRow("0", 1)]
    [DataRow("9999", 240)]
    public void ParseIdleMinutes_ClampsToTheAllowedRange(string stored, int expected)
    {
        Assert.AreEqual(expected, ScreenSaverSettings.ParseIdleMinutes(stored));
    }

    [TestMethod]
    [Timeout(60_000)]
    [DataRow(null)]
    [DataRow("soon")]
    public void ParseIdleMinutes_UsesFiveMinutes_ForAMissingOrUnreadableValue(string? stored)
    {
        Assert.AreEqual(ScreenSaverSettings.DefaultIdleMinutes, ScreenSaverSettings.ParseIdleMinutes(stored));
        Assert.AreEqual(5, ScreenSaverSettings.DefaultIdleMinutes);
    }
}
