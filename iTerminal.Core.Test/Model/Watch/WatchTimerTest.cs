using iTerminal.Core.Model.Watch;

namespace iTerminal.Core.Test.Model.Watch;

[TestClass]
public class WatchTimerTest
{
    private static readonly DateTime Start = new(2026, 10, 14, 10, 0, 0);

    [TestMethod]
    [DataRow("5 Tea", 5, "Tea")]
    [DataRow("5", 5, "")]
    [DataRow("05:00 Eggs", 5, "Eggs")]
    [DataRow("1:30:00 Bread dough", 90, "Bread dough")]
    public void TryParse_ReadsMinutesOrClockTimeThenTheLabel(string text, int minutes, string label)
    {
        bool ok = WatchTimer.TryParse(text, out var duration, out var parsedLabel, out var error);

        Assert.IsTrue(ok, error);
        Assert.AreEqual(TimeSpan.FromMinutes(minutes), duration);
        Assert.AreEqual(label, parsedLabel);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("0")]
    [DataRow("00:00")]
    [DataRow("soon")]
    public void TryParse_RefusesAZeroOrUnreadableDuration(string text)
    {
        Assert.IsFalse(WatchTimer.TryParse(text, out _, out _, out var error));
        Assert.IsFalse(string.IsNullOrEmpty(error));
    }

    [TestMethod]
    public void ARunningTimerCountsDownAndFinishes()
    {
        var timer = new WatchTimer("Tea", TimeSpan.FromSeconds(10));
        timer.Toggle(Start);

        Assert.AreEqual(WatchTimerState.Running, timer.State(Start.AddSeconds(3)));
        Assert.AreEqual(TimeSpan.FromSeconds(7), timer.Remaining(Start.AddSeconds(3)));
        Assert.AreEqual(WatchTimerState.Done, timer.State(Start.AddSeconds(10)));
        Assert.AreEqual(TimeSpan.Zero, timer.Remaining(Start.AddSeconds(99)));
    }

    [TestMethod]
    public void PausingKeepsTheTimeAlreadyRun()
    {
        var timer = new WatchTimer("Tea", TimeSpan.FromSeconds(10));
        timer.Toggle(Start);
        timer.Toggle(Start.AddSeconds(4));

        Assert.AreEqual(WatchTimerState.Paused, timer.State(Start.AddSeconds(20)));
        Assert.AreEqual(TimeSpan.FromSeconds(6), timer.Remaining(Start.AddSeconds(20)));

        timer.Toggle(Start.AddSeconds(20));
        Assert.AreEqual(TimeSpan.FromSeconds(2), timer.Remaining(Start.AddSeconds(24)));
    }

    [TestMethod]
    public void AFinishedTimerDoesNotStartUntilItIsRestarted()
    {
        var timer = new WatchTimer("Tea", TimeSpan.FromSeconds(5));
        timer.Toggle(Start);
        timer.Toggle(Start.AddSeconds(6));
        Assert.AreEqual(WatchTimerState.Done, timer.State(Start.AddSeconds(6)));

        timer.Toggle(Start.AddSeconds(7));
        Assert.AreEqual(WatchTimerState.Done, timer.State(Start.AddSeconds(7)), "Space does nothing to a finished timer");

        timer.Announced = true;
        timer.Restart();

        Assert.AreEqual(WatchTimerState.Ready, timer.State(Start.AddSeconds(8)));
        Assert.AreEqual(TimeSpan.FromSeconds(5), timer.Remaining(Start.AddSeconds(8)));
        Assert.IsFalse(timer.Announced, "a restarted timer is announced again when it runs out");
    }
}
