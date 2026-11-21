using iConsole.Services;
using iTerminal.Core.Jobs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Watch;
using Moq;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class WatchAlarmJobTest
{
    private static readonly DateTime Now = new(2026, 10, 14, 7, 30, 0);

    private sealed class Fixture
    {
        public required WatchAlarmJob Job { get; init; }
        public required WatchStore Store { get; init; }
        public required WatchTimers Timers { get; init; }
        public required Mock<INotificationService> Notifications { get; init; }
    }

    private static Fixture Create()
    {
        var settings = new Mock<ISettingsService>();
        var store = new WatchStore(settings.Object);
        var timers = new WatchTimers();
        var notifications = new Mock<INotificationService>();
        var job = new WatchAlarmJob(store, timers, Mock.Of<IDateTimeProviderService>(), notifications.Object, new Mock<Serilog.ILogger>().Object);
        return new Fixture { Job = job, Store = store, Timers = timers, Notifications = notifications };
    }

    [TestMethod]
    public void ADueAlarmRingsOnceAndStaysUntilDismissed()
    {
        var fixture = Create();
        fixture.Store.AddAlarm(new TimeOnly(7, 30), "Wake up");

        fixture.Job.Check(Now);
        fixture.Job.Check(Now.AddSeconds(20));

        fixture.Notifications.Verify(
            n => n.Notify("Wake up", "07:30", NotificationSeverity.Warning, Timeout.InfiniteTimeSpan),
            Times.Once);
    }

    [TestMethod]
    public void ASwitchedOffAlarmDoesNotRing()
    {
        var fixture = Create();
        var alarm = fixture.Store.AddAlarm(new TimeOnly(7, 30), "Wake up");
        fixture.Store.ReplaceAlarm(alarm with { Enabled = false });

        fixture.Job.Check(Now);

        fixture.Notifications.Verify(n => n.Notify(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<NotificationSeverity>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [TestMethod]
    public void AFinishedTimerIsAnnouncedOnce()
    {
        var fixture = Create();
        var timer = fixture.Timers.Add("Tea", TimeSpan.FromSeconds(3));
        fixture.Timers.Toggle(timer.Uid, Now);

        fixture.Job.Check(Now.AddSeconds(3));
        fixture.Job.Check(Now.AddSeconds(4));

        fixture.Notifications.Verify(n => n.Notify("Tea", It.IsAny<string?>(), It.IsAny<NotificationSeverity>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    [TestMethod]
    public void ATimerWithoutALabelIsAnnouncedAsATimer()
    {
        var fixture = Create();
        var timer = fixture.Timers.Add(string.Empty, TimeSpan.FromSeconds(1));
        fixture.Timers.Toggle(timer.Uid, Now);

        fixture.Job.Check(Now.AddSeconds(2));

        fixture.Notifications.Verify(n => n.Notify("Timer", It.IsAny<string?>(), It.IsAny<NotificationSeverity>(), It.IsAny<TimeSpan?>()), Times.Once);
    }
}
