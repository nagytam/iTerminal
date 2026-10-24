using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Defender;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Commands.Test;

/// <summary>Scan with Microsoft Defender: the verdict is always said, even when the scanner itself throws.</summary>
[TestClass]
public class ScanWithDefenderCommandTest
{
    private static (ScanWithDefenderCommand Command, Mock<IMalwareScanService> Scanner, Mock<INotificationService> Notes) Command(IMalwareScanService scanner)
    {
        var entry = new Mock<IEntry>();
        entry.SetupGet(e => e.Url).Returns(new Url(Path.Combine(Path.GetTempPath(), "suspect.exe")));
        entry.SetupGet(e => e.Exists).Returns(true);
        var model = new Mock<IContentModel>();
        model.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        model.Setup(m => m.ExistsFile(It.IsAny<Url>())).Returns(true);
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetFocusedModel()).Returns(model.Object);
        var notes = new Mock<INotificationService>();
        var mock = Mock.Get(scanner);
        return (new ScanWithDefenderCommand(service.Object, scanner, notes.Object), mock, notes);
    }

    [TestMethod]
    public async Task AScannerThatThrows_IsSaidAsAFailedScan()
    {
        var scanner = new Mock<IMalwareScanService>();
        scanner.SetupGet(s => s.IsAvailable).Returns(true);
        scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("engine not running"));
        var (command, _, notes) = Command(scanner.Object);

        await command.ExecuteAsync(null);
        await command.ScanTask;

        notes.Verify(n => n.Notify("Microsoft Defender scan failed", "engine not running", NotificationSeverity.Warning, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [TestMethod]
    public async Task ACleanScan_SaysNoThreatsFound()
    {
        var scanner = new Mock<IMalwareScanService>();
        scanner.SetupGet(s => s.IsAvailable).Returns(true);
        scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ScanResult(ScanOutcome.Clean, "clean"));
        var (command, _, notes) = Command(scanner.Object);

        await command.ExecuteAsync(null);
        await command.ScanTask;

        notes.Verify(n => n.Notify("Microsoft Defender", "No threats found in suspect.exe.", NotificationSeverity.Info, It.IsAny<TimeSpan?>()), Times.Once);
    }
}
