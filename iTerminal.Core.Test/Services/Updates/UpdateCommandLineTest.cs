using iTerminal.Core.Application;
using iTerminal.Core.Services.Updates;
using Moq;

namespace iTerminal.Core.Test.Services.Updates;

[TestClass]
public class UpdateCommandLineTest
{
    private static Mock<IUpdateService> Updates(UpdateInfo? available, bool canInstall, bool installed = true, params string[] log)
    {
        var updates = new Mock<IUpdateService>();
        updates.SetupGet(u => u.CurrentVersion).Returns("1.1.0");
        updates.SetupGet(u => u.ReleasesAddress).Returns("https://example.test/releases");
        updates.SetupGet(u => u.CanInstall).Returns(canInstall);
        updates.SetupGet(u => u.Log).Returns(log);
        updates.Setup(u => u.CheckAsync(It.IsAny<CancellationToken>())).ReturnsAsync(available);
        updates.Setup(u => u.InstallAsync(It.IsAny<CancellationToken>())).ReturnsAsync(installed);
        return updates;
    }

    [TestMethod]
    public void TheSwitchIsReadFromTheCommandLine()
    {
        Assert.IsTrue(LaunchOptions.Parse(["--update"]).Update);
        Assert.IsTrue(LaunchOptions.Parse(["--other", "--update"]).Update);
        Assert.IsFalse(LaunchOptions.Parse(["--updates"]).Update);
        Assert.IsFalse(LaunchOptions.Parse([]).Update);
    }

    [TestMethod]
    public void HelpIsRequestedByHelpSwitchesOnly()
    {
        Assert.IsTrue(LaunchOptions.Parse(["--help"]).HelpRequested);
        Assert.IsTrue(LaunchOptions.Parse(["-h"]).HelpRequested);
        Assert.IsTrue(LaunchOptions.Parse(["-?"]).HelpRequested);
        Assert.IsFalse(LaunchOptions.Parse(["--update"]).HelpRequested);
        Assert.IsFalse(LaunchOptions.Parse([]).HelpRequested);
    }

    [TestMethod]
    public void TheWaitForParentIdIsRead()
    {
        Assert.AreEqual(1234, LaunchOptions.Parse(["--wait-for-parent", "1234"]).ParentProcessId);
        Assert.IsNull(LaunchOptions.Parse(["--wait-for-parent", "x"]).ParentProcessId);
        Assert.IsNull(LaunchOptions.Parse([]).ParentProcessId);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ANewReleaseIsInstalledAndTheRunSucceeds()
    {
        Mock<IUpdateService> updates = Updates(new UpdateInfo("1.2.0", null), canInstall: true, installed: true, "Starting the update.");
        var output = new StringWriter();

        int code = await UpdateCommandLine.RunAsync(updates.Object, output, CancellationToken.None);

        Assert.AreEqual(UpdateCommandLine.Success, code);
        updates.Verify(u => u.InstallAsync(It.IsAny<CancellationToken>()), Times.Once);
        StringAssert.Contains(output.ToString(), "Installing iTerminal 1.2.0");
        StringAssert.Contains(output.ToString(), "Starting the update.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithNoNewerReleaseNothingIsInstalled()
    {
        Mock<IUpdateService> updates = Updates(available: null, canInstall: true);
        var output = new StringWriter();

        int code = await UpdateCommandLine.RunAsync(updates.Object, output, CancellationToken.None);

        Assert.AreEqual(UpdateCommandLine.Success, code);
        updates.Verify(u => u.InstallAsync(It.IsAny<CancellationToken>()), Times.Never);
        StringAssert.Contains(output.ToString(), "up to date");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AReleaseThisCopyCannotInstallFailsWithTheReleasesAddress()
    {
        Mock<IUpdateService> updates = Updates(new UpdateInfo("1.2.0", null), canInstall: false);
        var output = new StringWriter();

        int code = await UpdateCommandLine.RunAsync(updates.Object, output, CancellationToken.None);

        Assert.AreEqual(UpdateCommandLine.Failed, code);
        updates.Verify(u => u.InstallAsync(It.IsAny<CancellationToken>()), Times.Never);
        StringAssert.Contains(output.ToString(), "https://example.test/releases");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnInstallThatDoesNotStartFails()
    {
        Mock<IUpdateService> updates = Updates(new UpdateInfo("1.2.0", null), canInstall: true, installed: false, "The installer was not started.");
        var output = new StringWriter();

        int code = await UpdateCommandLine.RunAsync(updates.Object, output, CancellationToken.None);

        Assert.AreEqual(UpdateCommandLine.Failed, code);
        StringAssert.Contains(output.ToString(), "The installer was not started.");
    }
}
