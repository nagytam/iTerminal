using iTerminal.Core.Test.Services.BackgroundStart;
using iTerminal.Core.Services.BackgroundStart;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Jobs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.RuntimeDependencies;
using Moq;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class RuntimeDependencyCheckJobTest
{
    private static RuntimeDependencyStatus Update(string target) =>
        new("playwright", "Playwright", "Chromium", "1.63.0", target, RuntimeDependencyState.UpdateAvailable);

    [TestMethod]
    public void AnUpdateIsAnnouncedOnceForEachVersion()
    {
        var service = new Mock<IRuntimeDependencyService>();
        service.SetupGet(s => s.Statuses).Returns([Update("1.63.2")]);
        var notifications = new Mock<INotificationService>();
        var settings = new SettingsService();
        var job = new RuntimeDependencyCheckJob(service.Object, notifications.Object, settings, TestBackgroundStart.Configured(BackgroundStartMode.Immediately));

        job.AnnounceUpdates();
        job.AnnounceUpdates();

        notifications.Verify(n => n.Notify("Playwright 1.63.2 is available", It.IsAny<string>(), It.IsAny<NotificationSeverity>(), It.IsAny<TimeSpan?>()), Times.Once);

        service.SetupGet(s => s.Statuses).Returns([Update("1.63.3")]);
        job.AnnounceUpdates();

        notifications.Verify(n => n.Notify("Playwright 1.63.3 is available", It.IsAny<string>(), It.IsAny<NotificationSeverity>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    [TestMethod]
    public void AnInstalledOrMissingDependencyIsNotAnnounced()
    {
        var service = new Mock<IRuntimeDependencyService>();
        service.SetupGet(s => s.Statuses).Returns(
        [
            new RuntimeDependencyStatus("playwright", "Playwright", "Chromium", "1.63.0", null, RuntimeDependencyState.Installed),
            new RuntimeDependencyStatus("other", "Other", "Something", null, "1.0.0", RuntimeDependencyState.Missing),
        ]);
        var notifications = new Mock<INotificationService>();
        var job = new RuntimeDependencyCheckJob(service.Object, notifications.Object, new SettingsService(), TestBackgroundStart.Configured(BackgroundStartMode.Immediately));

        job.AnnounceUpdates();

        notifications.Verify(n => n.Notify(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<NotificationSeverity>(), It.IsAny<TimeSpan?>()), Times.Never);
    }
}
