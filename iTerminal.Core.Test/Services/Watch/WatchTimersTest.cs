using iTerminal.Core.Model.Watch;
using iTerminal.Core.Services.Watch;

namespace iTerminal.Core.Test.Services.Watch;

[TestClass]
public class WatchTimersTest
{
    private static readonly DateTime Start = new(2026, 10, 14, 10, 0, 0);

    [TestMethod]
    public void AFinishedTimerIsTakenOnce()
    {
        var timers = new WatchTimers();
        var timer = timers.Add("Tea", TimeSpan.FromSeconds(5));
        timers.Toggle(timer.Uid, Start);

        Assert.AreEqual(0, timers.TakeFinished(Start.AddSeconds(4)).Count, "not yet");
        var finished = timers.TakeFinished(Start.AddSeconds(5));
        Assert.AreEqual(1, finished.Count);
        Assert.AreEqual("Tea", finished[0].Label);
        Assert.AreEqual(0, timers.TakeFinished(Start.AddSeconds(60)).Count, "announced once");
    }

    [TestMethod]
    public void RowsShowTheStateAndTheTimeLeft()
    {
        var timers = new WatchTimers();
        var timer = timers.Add("Tea", TimeSpan.FromMinutes(5));
        timers.Toggle(timer.Uid, Start);

        var row = timers.Rows(Start.AddMinutes(1)).Single();

        Assert.AreEqual(WatchTimerState.Running, row.State);
        Assert.AreEqual(TimeSpan.FromMinutes(4), row.Remaining);
        Assert.AreEqual("Tea", row.Label);
    }

    [TestMethod]
    public void RemoveDropsTheTimer()
    {
        var timers = new WatchTimers();
        var timer = timers.Add("Tea", TimeSpan.FromMinutes(5));

        timers.Remove(timer.Uid);

        Assert.AreEqual(0, timers.Rows(Start).Count);
    }
}
