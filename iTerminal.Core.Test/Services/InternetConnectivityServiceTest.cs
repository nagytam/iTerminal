using Microsoft.VisualStudio.TestTools.UnitTesting;
using iTerminal.Core.Jobs;
using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class InternetConnectivityServiceTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void IsOnlineIsUnknownUntilTheFirstCheckReports()
    {
        var service = new InternetConnectivityService();

        Assert.IsNull(service.IsOnline);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReportKeepsTheLatestOutcome()
    {
        var service = new InternetConnectivityService();

        service.Report(true);
        Assert.AreEqual(true, service.IsOnline);

        service.Report(false);
        Assert.AreEqual(false, service.IsOnline);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheJobStopsWithoutReportingWhenCancelledBeforeTheFirstCheck()
    {
        var service = new InternetConnectivityService();
        var job = new InternetConnectivityJob(service) { FirstCheckDelay = TimeSpan.FromHours(1) };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await job.ExecuteAsync(cancellation.Token);

        Assert.IsNull(service.IsOnline);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACancelledCheckThrowsRatherThanReportingOffline()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var thrown = false;
        try
        {
            await InternetConnectivityJob.IsInternetReachableAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            thrown = true;
        }

        Assert.IsTrue(thrown);
    }
}
