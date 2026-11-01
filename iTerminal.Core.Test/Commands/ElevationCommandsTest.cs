using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Services.Elevation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Commands.Test;

/// <summary>Restart or run as administrator: a refused or failed attempt is said to the user, whatever form the failure takes.</summary>
[TestClass]
public class ElevationCommandsTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task RestartAsAdministrator_WhenTheAttemptThrows_SaysSo()
    {
        var elevation = new Mock<IElevationService>();
        elevation.SetupGet(e => e.IsSupported).Returns(true);
        elevation.SetupGet(e => e.IsElevated).Returns(false);
        elevation.Setup(e => e.RestartElevatedAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("no pkexec"));
        var notes = new Mock<INotificationService>();
        var command = new RestartAsAdministratorCommand(elevation.Object, notes.Object);

        await command.ExecuteAsync(null);
        await command.AttemptTask;

        notes.Verify(n => n.Notify("Not restarted as administrator", "no pkexec", NotificationSeverity.Warning, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RunAsAdministrator_WhenTheAttemptThrows_SaysSo()
    {
        var file = Path.Combine(Path.GetTempPath(), "setup.sh");
        var elevation = new Mock<IElevationService>();
        elevation.Setup(e => e.CanRunElevated(It.IsAny<string>())).Returns(true);
        elevation.Setup(e => e.RunElevatedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("sudo missing"));
        var notes = new Mock<INotificationService>();
        var contentModelService = new Mock<iConsole.Interfaces.IContentModelService>();
        var entry = new Mock<iConsole.Interfaces.IEntry>();
        entry.SetupGet(e => e.Url).Returns(new iConsole.Data.Url(file));
        entry.SetupGet(e => e.Exists).Returns(true);
        var model = new Mock<iConsole.Interfaces.IContentModel>();
        model.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        model.Setup(m => m.ExistsFile(It.IsAny<iConsole.Data.Url>())).Returns(true);
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(model.Object);
        contentModelService.SetupGet(s => s.ContentModels).Returns(new iConsole.Data.ConcurrentList<iConsole.Interfaces.IContentModel>());
        var command = new RunAsAdministratorCommand(contentModelService.Object, elevation.Object, notes.Object);

        await command.ExecuteAsync(null);
        await command.AttemptTask;

        notes.Verify(n => n.Notify("Could not run as administrator", "sudo missing", NotificationSeverity.Warning, It.IsAny<TimeSpan?>()), Times.Once);
    }
}
