using iTerminal.Core.Model.Watch;

namespace iTerminal.Core.Test.Model.Watch;

[TestClass]
public class WatchAlarmTest
{
    private static DateTime At(int hour, int minute, int second = 0, int day = 14) => new(2026, 10, day, hour, minute, second);

    [TestMethod]
    public void TryParse_ReadsTheTimeThenTheLabel()
    {
        bool ok = WatchAlarm.TryParse("07:30 Wake up  now", out var time, out var label, out var error);

        Assert.IsTrue(ok, error);
        Assert.AreEqual(new TimeOnly(7, 30), time);
        Assert.AreEqual("Wake up now", label);
    }

    [TestMethod]
    public void TryParse_AcceptsASingleDigitHourAndNoLabel()
    {
        bool ok = WatchAlarm.TryParse("7:05", out var time, out var label, out _);

        Assert.IsTrue(ok);
        Assert.AreEqual(new TimeOnly(7, 5), time);
        Assert.AreEqual(string.Empty, label);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("25:00")]
    [DataRow("noon")]
    public void TryParse_RefusesWhatIsNotATime(string text)
    {
        bool ok = WatchAlarm.TryParse(text, out _, out _, out var error);

        Assert.IsFalse(ok);
        Assert.IsFalse(string.IsNullOrEmpty(error));
    }

    [TestMethod]
    public void IsDue_RingsInItsMinuteOnly()
    {
        var alarm = new WatchAlarm("a", new TimeOnly(7, 30), "Wake up");

        Assert.IsTrue(WatchAlarm.IsDue(alarm, At(7, 30, 0)));
        Assert.IsTrue(WatchAlarm.IsDue(alarm, At(7, 30, 59)));
        Assert.IsFalse(WatchAlarm.IsDue(alarm, At(7, 29, 59)));
        Assert.IsFalse(WatchAlarm.IsDue(alarm, At(7, 31, 0)));
    }

    [TestMethod]
    public void IsDue_IsFalseWhenSwitchedOff()
    {
        var alarm = new WatchAlarm("a", new TimeOnly(7, 30), "Wake up", Enabled: false);

        Assert.IsFalse(WatchAlarm.IsDue(alarm, At(7, 30)));
    }

    [TestMethod]
    public void IsDue_RingsOnceADay()
    {
        var rangToday = new WatchAlarm("a", new TimeOnly(7, 30), "Wake up", LastRungOn: new DateOnly(2026, 10, 14));

        Assert.IsFalse(WatchAlarm.IsDue(rangToday, At(7, 30, 10, day: 14)), "a second check in the same minute must not ring again");
        Assert.IsTrue(WatchAlarm.IsDue(rangToday, At(7, 30, 10, day: 15)), "the next day it rings again");
    }

    [TestMethod]
    public void DisplayLabel_FallsBackToAlarm()
    {
        Assert.AreEqual("Alarm", new WatchAlarm("a", new TimeOnly(7, 30), string.Empty).DisplayLabel);
        Assert.AreEqual("Wake", new WatchAlarm("a", new TimeOnly(7, 30), "Wake").DisplayLabel);
    }
}
