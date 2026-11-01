using iTerminal.Core.Test.Services.BackgroundStart;
using iTerminal.Core.Services.BackgroundStart;
using iConsole.Services;
using iTerminal.Core.Jobs;
using iTerminal.Core.Services.Updates;
using Moq;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class UpdateCheckJobTest
{
    private static Mock<IUpdateService> Updates(bool canInstall)
    {
        var updates = new Mock<IUpdateService>();
        updates.SetupGet(u => u.Available).Returns(new UpdateInfo("1.2.0", null));
        updates.SetupGet(u => u.CurrentVersion).Returns("1.1.0");
        updates.SetupGet(u => u.ReleasesAddress).Returns("https://example.test/releases");
        updates.SetupGet(u => u.CanInstall).Returns(canInstall);
        return updates;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WhereNothingCanBeInstalled_ANewReleaseIsOnlyAToast()
    {
        var notifications = new Mock<INotificationService>();
        var job = new UpdateCheckJob(Updates(canInstall: false).Object, notifications.Object, Mock.Of<IServiceProvider>(), TestBackgroundStart.Configured(BackgroundStartMode.Immediately));

        await job.OfferAsync(openDialog: true, CancellationToken.None);
        await job.OfferAsync(openDialog: false, CancellationToken.None);

        notifications.Verify(n => n.Notify("iTerminal 1.2.0 is available", It.IsAny<string>(), It.IsAny<NotificationSeverity>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASkippedReleaseIsNotAnnounced()
    {
        Mock<IUpdateService> updates = Updates(canInstall: false);
        updates.SetupGet(u => u.IsSkipped).Returns(true);
        var notifications = new Mock<INotificationService>();
        var job = new UpdateCheckJob(updates.Object, notifications.Object, Mock.Of<IServiceProvider>(), TestBackgroundStart.Configured(BackgroundStartMode.Immediately));

        await job.OfferAsync(openDialog: true, CancellationToken.None);

        notifications.VerifyNoOtherCalls();
    }
}
