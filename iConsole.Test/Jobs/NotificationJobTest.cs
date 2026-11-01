using iConsole.Jobs;
using iConsole.Services;

namespace iConsole.Test.Jobs;

[TestClass]
public class NotificationJobTest
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Deadline;
        while (!condition())
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, $"Timed out waiting for {because}.");
            await Task.Delay(20);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Running_ExpiresANotification_AndStopsWhenCancelled()
    {
        var service = new NotificationService();
        var job = new NotificationJob(service);
        using var cancellation = new CancellationTokenSource();
        var run = Task.Run(() => job.ExecuteAsync(cancellation.Token));

        service.Notify("Done", duration: TimeSpan.FromMilliseconds(300));
        Assert.AreEqual(1, service.Active.Count);

        await WaitUntilAsync(() => service.Active.Count == 0, "the notification to expire");

        cancellation.Cancel();
        await run.WaitAsync(Deadline);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WhileTheTerminalIsLent_TimeStandsStill()
    {
        var service = new NotificationService();
        var ownership = new ConsoleOwnership();
        var job = new NotificationJob(service, ownership);
        using var cancellation = new CancellationTokenSource();
        var run = Task.Run(() => job.ExecuteAsync(cancellation.Token));

        using var shellExited = new ManualResetEventSlim();
        var lend = ownership.LendAsync(() =>
        {
            shellExited.Wait();
            return 0;
        });
        await WaitUntilAsync(() => ownership.IsLent, "the terminal to be lent");

        // Posted behind the shell, as a copy finishing there would be.
        service.Notify("Copy finished", duration: TimeSpan.FromMilliseconds(300));
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.AreEqual(1, service.Active.Count, "nobody could have read it yet");

        shellExited.Set();
        await lend;
        await WaitUntilAsync(() => service.Active.Count == 0, "the notification to expire once the terminal is back");

        cancellation.Cancel();
        await run.WaitAsync(Deadline);
    }
}
