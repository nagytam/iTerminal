using iConsole.Services;

namespace iConsole.Test.Services;

[TestClass]
public class DateTimeProviderVirtualTimeTest
{
    /// <summary>The cached time is kept for the refresh interval and then read again, all without sleeping.</summary>
    [TestMethod]
    public void Now_IsRefreshedOnlyOnceTheRefreshIntervalHasPassed()
    {
        var time = new VirtualTimeProvider(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var service = new DateTimeProviderService(time);
        var first = service.UtcNow;

        time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.AreEqual(first, service.UtcNow, "Inside the interval the cached time is returned.");

        time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.AreEqual(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(600), service.UtcNow);
    }
}
