using iTerminal.Core.Model.Watch;

namespace iTerminal.Core.Test.Model.Watch;

[TestClass]
public class WatchTimeZonesTest
{
    [TestMethod]
    public void TryFind_RecognisesASystemId()
    {
        Assert.IsTrue(WatchTimeZones.TryFind("UTC", out var zone));
        Assert.AreEqual(TimeZoneInfo.Utc.Id, zone.Id);
    }

    [TestMethod]
    public void TryFind_RefusesAnUnknownOrEmptyId()
    {
        Assert.IsFalse(WatchTimeZones.TryFind("Mars/Olympus_Mons", out _));
        Assert.IsFalse(WatchTimeZones.TryFind("   ", out _));
    }

    [TestMethod]
    public void ConvertFromUtc_ShowsTheZonesWallClock()
    {
        var utc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var fixedZone = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

        Assert.AreEqual(new DateTime(2026, 1, 15, 14, 0, 0), WatchTimeZones.ConvertFromUtc(utc, fixedZone));
        Assert.AreEqual("UTC+02:00", WatchTimeZones.OffsetLabel(utc, fixedZone));
    }

    [TestMethod]
    public void OffsetLabel_ShowsANegativeOffsetWithItsSign()
    {
        var utc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test-5:30", new TimeSpan(-5, -30, 0), "Test-5:30", "Test-5:30");

        Assert.AreEqual("UTC-05:30", WatchTimeZones.OffsetLabel(utc, zone));
    }
}
