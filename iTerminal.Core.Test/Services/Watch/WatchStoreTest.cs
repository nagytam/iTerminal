using System.Text.Json;
using iTerminal.Core.Model.Watch;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Watch;
using Moq;

namespace iTerminal.Core.Test.Services.Watch;

[TestClass]
public class WatchStoreTest
{
    private static Mock<ISettingsService> Settings(string? alarmsJson = null, string? zonesJson = null)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(WatchStore.AlarmsKey)).Returns(alarmsJson);
        settings.Setup(s => s.Get(WatchStore.WorldClocksKey)).Returns(zonesJson);
        return settings;
    }

    [TestMethod]
    public void AddAlarmWritesTheWholeListUnderItsKey()
    {
        var settings = Settings();
        var store = new WatchStore(settings.Object);

        var alarm = store.AddAlarm(new TimeOnly(7, 30), "Wake up");

        settings.Verify(s => s.Set(WatchStore.AlarmsKey, It.Is<string>(json => json.Contains("Wake up"))), Times.Once);
        Assert.AreEqual(1, store.Alarms.Count);
        Assert.AreEqual(alarm.Uid, store.Alarms[0].Uid);
    }

    [TestMethod]
    public void AlarmsSavedBeforeAreReadBackOnFirstUse()
    {
        var saved = JsonSerializer.Serialize(new List<WatchAlarm> { new("u1", new TimeOnly(6, 15), "Run", true, new DateOnly(2026, 10, 13)) });
        var store = new WatchStore(Settings(alarmsJson: saved).Object);

        var alarm = store.Alarms.Single();

        Assert.AreEqual("u1", alarm.Uid);
        Assert.AreEqual(new TimeOnly(6, 15), alarm.Time);
        Assert.AreEqual(new DateOnly(2026, 10, 13), alarm.LastRungOn);
    }

    [TestMethod]
    public void ADamagedValueReadsAsAnEmptyList()
    {
        var store = new WatchStore(Settings(alarmsJson: "{not json", zonesJson: "[").Object);

        Assert.AreEqual(0, store.Alarms.Count);
        Assert.AreEqual(0, store.WorldClocks.Count);
    }

    [TestMethod]
    public void ReplaceAlarmKeepsItsIdentity()
    {
        var store = new WatchStore(Settings().Object);
        var alarm = store.AddAlarm(new TimeOnly(7, 30), "Wake up");

        store.ReplaceAlarm(alarm with { Enabled = false, Label = "Sleep in" });

        var stored = store.Alarms.Single();
        Assert.AreEqual(alarm.Uid, stored.Uid);
        Assert.IsFalse(stored.Enabled);
        Assert.AreEqual("Sleep in", stored.Label);
    }

    [TestMethod]
    public void MarkRungRecordsTheDayAndSavesIt()
    {
        var settings = Settings();
        var store = new WatchStore(settings.Object);
        var alarm = store.AddAlarm(new TimeOnly(7, 30), "Wake up");

        store.MarkRung(alarm.Uid, new DateOnly(2026, 10, 14));

        Assert.AreEqual(new DateOnly(2026, 10, 14), store.Alarms.Single().LastRungOn);
        settings.Verify(s => s.Set(WatchStore.AlarmsKey, It.Is<string>(json => json.Contains("2026-10-14"))), Times.AtLeastOnce);
    }

    [TestMethod]
    public void RemoveAlarmDropsOnlyThatAlarm()
    {
        var store = new WatchStore(Settings().Object);
        var first = store.AddAlarm(new TimeOnly(7, 30), "Wake up");
        store.AddAlarm(new TimeOnly(8, 0), "Coffee");

        store.RemoveAlarm(first.Uid);

        Assert.AreEqual("Coffee", store.Alarms.Single().Label);
    }

    [TestMethod]
    public void AWorldClockNeedsAKnownZoneAndIsListedOnce()
    {
        var settings = Settings();
        var store = new WatchStore(settings.Object);

        Assert.IsFalse(store.AddWorldClock("Mars/Olympus_Mons"));
        Assert.IsTrue(store.AddWorldClock("UTC"));
        Assert.IsFalse(store.AddWorldClock("UTC"));

        CollectionAssert.AreEqual(new[] { "UTC" }, store.WorldClocks.ToArray());
        settings.Verify(s => s.Set(WatchStore.WorldClocksKey, It.IsAny<string>()), Times.Once);
    }

    [TestMethod]
    public void RemoveWorldClockDropsThatZone()
    {
        var store = new WatchStore(Settings(zonesJson: "[\"UTC\",\"Etc/GMT\"]").Object);

        store.RemoveWorldClock("UTC");

        CollectionAssert.AreEqual(new[] { "Etc/GMT" }, store.WorldClocks.ToArray());
    }
}
