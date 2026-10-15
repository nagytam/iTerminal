using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Docker;
using iTerminal.Core.Services.Shell;
using iTerminal.Core.Test.Helper;
using Moq;

namespace iTerminal.Core.Test.Services.Shell;

/// <summary>Where Ctrl+O's shell runs, for each kind of panel, and how a shell that cannot start is reported.</summary>
[TestClass]
public class ShellLaunchTest
{
    private static IFrequentExecutionGuardService ImmediateGuard()
    {
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());
        return guard.Object;
    }

    [TestMethod]
    public void InsideAContainerTheShellIsDockerExecWithATerminal()
    {
        var launch = ShellLaunch.Docker("web", "/etc/nginx");

        Assert.AreEqual("docker", launch.FileName);
        CollectionAssert.AreEqual(
            new[] { "exec", "-it", "-w", "/etc/nginx", "web", "sh", "-c", "if command -v bash >/dev/null 2>&1; then exec bash; fi; exec sh" },
            launch.Arguments.ToArray(),
            "bash when the image has it, sh when not");
        Assert.AreEqual("web:/etc/nginx", launch.Location);
    }

    [TestMethod]
    public void OverSshTheFolderIsQuotedForTheRemoteShell()
    {
        var launch = ShellLaunch.Ssh("me", "host", "/srv/it's here");

        Assert.AreEqual("ssh", launch.FileName);
        CollectionAssert.AreEqual(
            new[] { "-t", "me@host", "cd '/srv/it'\\''s here' && exec \"$SHELL\" -l" },
            launch.Arguments.ToArray());
    }

    [TestMethod]
    public void AConfiguredShellIsUsedAsIs()
    {
        var launch = ShellLaunch.Local(Path.GetTempPath(), "nu");

        Assert.AreEqual("nu", launch.FileName);
        Assert.AreEqual(0, launch.Arguments.Count);
        Assert.AreEqual(Path.GetTempPath(), launch.ToStartInfo().WorkingDirectory);
    }

    [TestMethod]
    public void TheDefaultLocalShellIsTheSystemsOwn()
    {
        var launch = ShellLaunch.Local(Path.GetTempPath());

        if (OperatingSystem.IsWindows())
        {
            StringAssert.Matches(Path.GetFileName(launch.FileName), new System.Text.RegularExpressions.Regex("^(pwsh|cmd)\\.exe$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }
        else
        {
            Assert.IsTrue(launch.FileName.StartsWith('/'), launch.FileName);
        }
    }

    [TestMethod]
    public async Task CtrlOOnTheDockerDriveOpensAShellInTheContainerUnderTheCursor()
    {
        var runner = new FakeProcessRunner().Answer("docker ps", "web\trunning\tUp 2 hours\tnginx:latest\t2026-10-04 08:00:00 +0200 CEST\n");
        var docker = new ContentModelDockerDrive("LeftPanel", new SettingsService(), new DockerDriveService(runner), null, ImmediateGuard());
        docker.SetUrl(new Url("docker://"));
        await docker.ListingTask;
        docker.CursorPosition.Y = 1;

        var command = CommandOver(docker);

        Assert.AreEqual("web:/", command.ChooseLaunch().Location);
    }

    [TestMethod]
    public void APanelWithNoShellOfItsOwnGetsOneInTheHomeFolder()
    {
        var command = CommandOver(Mock.Of<IContentModel>());

        Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), command.ChooseLaunch().WorkingDirectory);
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task AProgramThatCannotStartIsSaidInWordsAndTheTerminalComesBack()
    {
        var ownership = new ConsoleOwnership();
        var service = new InteractiveShellService(ownership);

        var outcome = await RunWithoutConsoleInputAsync(service, new ShellLaunch("no-such-shell-" + Guid.NewGuid().ToString("N"), [], null, "nowhere"));

        Assert.IsFalse(outcome.Started);
        StringAssert.Contains(outcome.Failure, "Cannot start no-such-shell-");
        Assert.IsFalse(ownership.IsLent);
        Assert.IsFalse(service.IsRunning);
    }

    [TestMethod]
    [Timeout(30_000)]
    public async Task TheShellsExitCodeComesBack()
    {
        var launch = OperatingSystem.IsWindows()
            ? new ShellLaunch("cmd.exe", ["/c", "exit 4"], Path.GetTempPath(), "temp")
            : new ShellLaunch("/bin/sh", ["-c", "exit 4"], Path.GetTempPath(), "temp");

        var outcome = await RunWithoutConsoleInputAsync(new InteractiveShellService(new ConsoleOwnership()), launch);

        Assert.IsTrue(outcome.Started);
        Assert.AreEqual(4, outcome.ExitCode);
    }

    /// <summary>
    /// A failed start or a quick non-zero exit waits for Enter before the panels come back. Under a
    /// terminal that wait is the test host's own keyboard, and it runs into the test timeout; redirected
    /// input ends it at once, as it does on a CI runner.
    /// </summary>
    private static async Task<ShellOutcome> RunWithoutConsoleInputAsync(InteractiveShellService service, ShellLaunch launch)
    {
        var originalInput = Console.In;
        Console.SetIn(TextReader.Null);
        try
        {
            return await service.RunAsync(launch);
        }
        finally
        {
            Console.SetIn(originalInput);
        }
    }

    private static InteractiveShellCommand CommandOver(IContentModel model)
    {
        var models = new Mock<IContentModelService>();
        models.Setup(m => m.GetFocusedModel()).Returns(model);
        return new InteractiveShellCommand(models.Object, Mock.Of<IInteractiveShellService>(), new SettingsService());
    }
}
