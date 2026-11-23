using iConsole.Services;
using iTerminal.Core.Jobs;
using iTerminal.Core.Model.ToDo;
using iTerminal.Core.Services;
using iTerminal.Core.Services.ToDo;
using Moq;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class ToDoReminderJobTest
{
    private static readonly DateTime Now = new(2026, 11, 14, 10, 0, 0);
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static ToDoStore StoreWithADueReminder()
    {
        var store = new ToDoStore(new Mock<ISettingsService>().Object);
        store.Add(new ToDoTask(Guid.NewGuid(), "Call the bank", null, ToDoPriority.Normal, false, Now.AddMinutes(-5)));
        return store;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADueReminderIsGivenOnATick()
    {
        var given = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = new Mock<INotificationService>();
        notifications
            .Setup(n => n.Notify(It.IsAny<string>(), "Call the bank", NotificationSeverity.Warning, Timeout.InfiniteTimeSpan))
            .Callback(() => given.TrySetResult());
        var clock = Mock.Of<IDateTimeProviderService>(c => c.Now == Now);
        var job = new ToDoReminderJob(new ToDoReminderService(StoreWithADueReminder(), notifications.Object), clock, Tick);
        using var cancellation = new CancellationTokenSource();

        // The job loops on the thread that starts it, as it does under the host, so it gets a thread of its own here.
        Task running = Task.Factory.StartNew(() => job.ExecuteAsync(cancellation.Token), TaskCreationOptions.LongRunning).Unwrap();
        await given.Task.WaitAsync(Patience);
        await cancellation.CancelAsync();
        await running.WaitAsync(Patience);

        notifications.Verify(n => n.Notify(It.IsAny<string>(), "Call the bank", NotificationSeverity.Warning, Timeout.InfiniteTimeSpan), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedCheckDoesNotStopTheJob()
    {
        var given = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = new Mock<INotificationService>();
        notifications
            .Setup(n => n.Notify(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<NotificationSeverity>(), It.IsAny<TimeSpan?>()))
            .Callback(() => given.TrySetResult());
        // The first check fails while it reads the clock; the job has to try again on the next tick.
        var clock = new Mock<IDateTimeProviderService>();
        clock.SetupSequence(c => c.Now).Throws(new InvalidOperationException("clock")).Returns(Now);
        var job = new ToDoReminderJob(new ToDoReminderService(StoreWithADueReminder(), notifications.Object), clock.Object, Tick);
        using var cancellation = new CancellationTokenSource();

        // The job loops on the thread that starts it, as it does under the host, so it gets a thread of its own here.
        Task running = Task.Factory.StartNew(() => job.ExecuteAsync(cancellation.Token), TaskCreationOptions.LongRunning).Unwrap();
        await given.Task.WaitAsync(Patience);
        await cancellation.CancelAsync();
        await running.WaitAsync(Patience);

        notifications.Verify(n => n.Notify(It.IsAny<string>(), "Call the bank", NotificationSeverity.Warning, Timeout.InfiniteTimeSpan), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CancellingStopsTheJobWithoutWaitingForTheNextTick()
    {
        var notifications = new Mock<INotificationService>();
        var clock = Mock.Of<IDateTimeProviderService>(c => c.Now == Now);
        var job = new ToDoReminderJob(new ToDoReminderService(StoreWithADueReminder(), notifications.Object), clock, TimeSpan.FromHours(1));
        using var cancellation = new CancellationTokenSource();

        // The job loops on the thread that starts it, as it does under the host, so it gets a thread of its own here.
        Task running = Task.Factory.StartNew(() => job.ExecuteAsync(cancellation.Token), TaskCreationOptions.LongRunning).Unwrap();
        // Gives the job thread time to reach its hour-long wait, so the cancel has to release it (cancelling first would end the loop before it).
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await cancellation.CancelAsync();

        await running.WaitAsync(TimeSpan.FromSeconds(30));
        notifications.Verify(n => n.Notify(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<NotificationSeverity>(), It.IsAny<TimeSpan?>()), Times.Never);
    }
}
