using iTerminal.Core.Jobs;
using iTerminal.Core.Model.Calendar;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class CalendarIdentityJobTest
{
    // A list saved before meetings had identities: loading it gives each meeting one in memory and leaves them pending.
    private const string ListWithoutIdentities = """[{"Date":"2026-11-14","Start":"09:30:00","DurationMinutes":60,"Title":"Standup"}]""";

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static Mock<ISettingsService> SettingsWith(string? meetings)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(MeetingStore.SettingsKey)).Returns(meetings);
        return settings;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PendingIdentitiesAreWrittenOnATick()
    {
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<ISettingsService> settings = SettingsWith(ListWithoutIdentities);
        settings.Setup(s => s.Write()).Callback(() => written.TrySetResult());
        var store = new MeetingStore(settings.Object);
        var job = new CalendarIdentityJob(store, Mock.Of<Serilog.ILogger>(), Tick);
        using var cancellation = new CancellationTokenSource();

        // The job loops on the thread that starts it, as it does under the host, so it gets a thread of its own here.
        Task running = Task.Factory.StartNew(() => job.ExecuteAsync(cancellation.Token), TaskCreationOptions.LongRunning).Unwrap();
        await written.Task.WaitAsync(Patience);
        await cancellation.CancelAsync();
        await running.WaitAsync(Patience);

        settings.Verify(s => s.Set(MeetingStore.SettingsKey, It.Is<string>(json => json.Contains("\"Uid\":\"") && !json.Contains("\"Uid\":null"))), Times.Once);
        Assert.IsFalse(store.HasPendingIdentities);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NothingIsWrittenWhenNoIdentityIsPending()
    {
        // The first tick reads the list (the store loads it on first use), which shows that the job has checked at least once.
        var checkedOnce = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<ISettingsService> settings = SettingsWith(null);
        settings.Setup(s => s.Get(MeetingStore.SettingsKey)).Callback(() => checkedOnce.TrySetResult()).Returns((string?)null);
        var store = new MeetingStore(settings.Object);
        var job = new CalendarIdentityJob(store, Mock.Of<Serilog.ILogger>(), Tick);
        using var cancellation = new CancellationTokenSource();

        // The job loops on the thread that starts it, as it does under the host, so it gets a thread of its own here.
        Task running = Task.Factory.StartNew(() => job.ExecuteAsync(cancellation.Token), TaskCreationOptions.LongRunning).Unwrap();
        await checkedOnce.Task.WaitAsync(Patience);
        await cancellation.CancelAsync();
        await running.WaitAsync(Patience);

        settings.Verify(s => s.Set(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        settings.Verify(s => s.Write(), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedWriteIsLoggedAndTriedAgainOnTheNextTick()
    {
        var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<ISettingsService> settings = SettingsWith(ListWithoutIdentities);
        int writes = 0;
        settings.Setup(s => s.Write()).Callback(() =>
        {
            if (Interlocked.Increment(ref writes) == 1)
            {
                throw new IOException("disk full");
            }

            retried.TrySetResult();
        });
        var logger = new Mock<Serilog.ILogger>();
        var store = new MeetingStore(settings.Object);
        var job = new CalendarIdentityJob(store, logger.Object, Tick);
        using var cancellation = new CancellationTokenSource();

        // The job loops on the thread that starts it, as it does under the host, so it gets a thread of its own here.
        Task running = Task.Factory.StartNew(() => job.ExecuteAsync(cancellation.Token), TaskCreationOptions.LongRunning).Unwrap();
        await retried.Task.WaitAsync(Patience);
        await cancellation.CancelAsync();
        await running.WaitAsync(Patience);

        logger.Verify(l => l.Warning(It.IsAny<IOException>(), It.IsAny<string>()), Times.Once);
        Assert.IsFalse(store.HasPendingIdentities, "the retry saved them");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CancellingStopsTheJobWithoutWaitingForTheNextTick()
    {
        var store = new MeetingStore(SettingsWith(null).Object);
        var job = new CalendarIdentityJob(store, Mock.Of<Serilog.ILogger>(), TimeSpan.FromHours(1));
        using var cancellation = new CancellationTokenSource();

        // The job loops on the thread that starts it, as it does under the host, so it gets a thread of its own here.
        Task running = Task.Factory.StartNew(() => job.ExecuteAsync(cancellation.Token), TaskCreationOptions.LongRunning).Unwrap();
        // Gives the job thread time to reach its hour-long wait, so the cancel has to release it (cancelling first would end the loop before it).
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await cancellation.CancelAsync();

        await running.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
