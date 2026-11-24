using iTerminal.Core.Model.Watch;

namespace iTerminal.Core.Test.Model.Watch;

[TestClass]
public class WatchStopwatchTest
{
    private static readonly DateTime Start = new(2026, 10, 14, 10, 0, 0);

    [TestMethod]
    public void StartAndStopAddUp()
    {
        var watch = new WatchStopwatch();
        watch.Toggle(Start);
        watch.Toggle(Start.AddSeconds(3));
        watch.Toggle(Start.AddSeconds(10));
        watch.Toggle(Start.AddSeconds(12));

        Assert.AreEqual(TimeSpan.FromSeconds(5), watch.Elapsed(Start.AddSeconds(20)), "a stopped stopwatch stands still, and the runs add up");
        Assert.IsFalse(watch.IsRunning);
    }

    [TestMethod]
    public void ALapIsRecordedOnlyWhileItRuns()
    {
        var watch = new WatchStopwatch();
        Assert.IsFalse(watch.Lap(Start), "a lap on a stopped stopwatch is ignored");

        watch.Toggle(Start);
        Assert.IsTrue(watch.Lap(Start.AddSeconds(2)));
        Assert.IsTrue(watch.Lap(Start.AddSeconds(5)));

        CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) }, watch.Laps.ToArray());
    }

    [TestMethod]
    public void ResetClearsTheTimeAndTheLaps()
    {
        var watch = new WatchStopwatch();
        watch.Toggle(Start);
        watch.Lap(Start.AddSeconds(1));
        watch.Reset();

        Assert.IsFalse(watch.IsRunning);
        Assert.AreEqual(TimeSpan.Zero, watch.Elapsed(Start.AddSeconds(9)));
        Assert.AreEqual(0, watch.Laps.Count);
    }

    [TestMethod]
    [DataRow(0, "00:00.00")]
    [DataRow(1234, "00:01.23")]
    [DataRow(3723456, "1:02:03.45")]
    public void FormatShowsHundredths(long milliseconds, string expected)
    {
        Assert.AreEqual(expected, WatchStopwatch.Format(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [TestMethod]
    public void AClockThatGoesBackwardsCountsAsStanding()
    {
        var watch = new WatchStopwatch();
        watch.Toggle(Start);

        Assert.AreEqual(TimeSpan.Zero, watch.Elapsed(Start.AddSeconds(-5)));
    }
}
