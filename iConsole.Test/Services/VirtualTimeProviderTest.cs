using iConsole.Services;

namespace iConsole.Test.Services;

[TestClass]
public class VirtualTimeProviderTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void StartsAtTheGivenUtcTime_WithTheMonotonicClockAtZero()
    {
        var time = new VirtualTimeProvider(new DateTime(2030, 6, 1, 12, 0, 0, DateTimeKind.Utc));

        Assert.AreEqual(new DateTime(2030, 6, 1, 12, 0, 0, DateTimeKind.Utc), time.UtcNow);
        Assert.AreEqual(0, time.MonotonicMilliseconds);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Advance_MovesEveryClockForward()
    {
        var time = new VirtualTimeProvider(new DateTime(2030, 6, 1, 12, 0, 0, DateTimeKind.Utc));

        time.Advance(TimeSpan.FromSeconds(90));

        Assert.AreEqual(new DateTime(2030, 6, 1, 12, 1, 30, DateTimeKind.Utc), time.UtcNow);
        Assert.AreEqual(time.UtcNow.ToLocalTime(), time.Now);
        Assert.AreEqual(90_000, time.MonotonicMilliseconds);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Advance_RejectsNegativeDurations()
    {
        var time = new VirtualTimeProvider();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => time.Advance(TimeSpan.FromSeconds(-1)));
        Assert.AreEqual(0, time.MonotonicMilliseconds);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SystemTimeProvider_ReadsTheRealClocks()
    {
        ITimeProvider time = new SystemTimeProvider();

        Assert.IsTrue(Math.Abs((time.UtcNow - DateTime.UtcNow).TotalSeconds) < 5);
        Assert.IsTrue(time.MonotonicMilliseconds > 0);
    }
}
