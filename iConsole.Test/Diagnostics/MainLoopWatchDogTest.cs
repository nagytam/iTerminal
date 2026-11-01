using iConsole.Diagnostics;
using iConsole.Services;

namespace iConsole.Test.Diagnostics;

[TestClass]
public class MainLoopWatchDogTest
{
    private readonly VirtualTimeProvider _time = new();

    private MainLoopWatchDog Create() => new(_time, TimeSpan.FromMilliseconds(200));

    [Timeout(60_000)]
    [TestMethod]
    public void A_PassWithinTheBudget_IsNotReported()
    {
        var watchDog = Create();
        var stalls = new List<StallEventArgs>();
        watchDog.Stalled += (_, e) => stalls.Add(e);

        using (watchDog.Watch("key"))
        {
            _time.Advance(TimeSpan.FromMilliseconds(150));
            watchDog.Check();
        }

        Assert.AreEqual(0, stalls.Count);
        Assert.AreEqual(0, watchDog.StallCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void A_PassPastTheBudget_IsReportedWhileItIsStillRunning_OnceOnly()
    {
        var watchDog = Create();
        var stalls = new List<StallEventArgs>();
        watchDog.Stalled += (_, e) => stalls.Add(e);

        using var pass = watchDog.Watch("render");
        _time.Advance(TimeSpan.FromMilliseconds(250));
        watchDog.Check();
        _time.Advance(TimeSpan.FromMilliseconds(250));
        watchDog.Check();

        Assert.AreEqual(1, stalls.Count, "A stalled pass is reported once, not on every poll.");
        Assert.AreEqual("render", stalls[0].Label);
        Assert.AreEqual(250, stalls[0].ElapsedMilliseconds);
        Assert.AreEqual(1, watchDog.StallCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AStalledPass_RecoversWithItsTotalDuration_WhenItEnds()
    {
        var watchDog = Create();
        var recovered = new List<StallEventArgs>();
        watchDog.Recovered += (_, e) => recovered.Add(e);

        var pass = watchDog.Watch("key");
        _time.Advance(TimeSpan.FromMilliseconds(250));
        watchDog.Check();
        _time.Advance(TimeSpan.FromMilliseconds(100));
        pass.Dispose();

        Assert.AreEqual(1, recovered.Count);
        Assert.AreEqual(350, recovered[0].ElapsedMilliseconds);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APassThatNeverStalled_DoesNotRecover()
    {
        var watchDog = Create();
        var recovered = new List<StallEventArgs>();
        watchDog.Recovered += (_, e) => recovered.Add(e);

        using (watchDog.Watch("key"))
        {
            _time.Advance(TimeSpan.FromMilliseconds(10));
        }

        Assert.AreEqual(0, recovered.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DisposingTwice_IsHarmless_AndAFinishedPassIsNoLongerChecked()
    {
        var watchDog = Create();
        var stalls = new List<StallEventArgs>();
        watchDog.Stalled += (_, e) => stalls.Add(e);

        var pass = watchDog.Watch("mouse");
        pass.Dispose();
        pass.Dispose();
        _time.Advance(TimeSpan.FromSeconds(5));
        watchDog.Check();

        Assert.AreEqual(0, stalls.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SeveralPassesAtOnce_AreEachReportedOnTheirOwn()
    {
        var watchDog = Create();
        var labels = new List<string>();
        watchDog.Stalled += (_, e) => labels.Add(e.Label);

        using var first = watchDog.Watch("key");
        _time.Advance(TimeSpan.FromMilliseconds(100));
        using var second = watchDog.Watch("render");
        _time.Advance(TimeSpan.FromMilliseconds(150));
        watchDog.Check();

        CollectionAssert.AreEqual(new[] { "key" }, labels, "Only the pass that has run past the budget is reported.");
    }
}
