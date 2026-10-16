using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Docker;
using iTerminal.Core.Services.Preview;
using iTerminal.Core.Test.Helper;
using Moq;
using System.Text;

namespace iTerminal.Core.Test.Model.ContentModels;

/// <summary>
/// The docker panel beyond its files: a container is operated from its row, previews as its log,
/// is never taken for a folder by F5/F8, and the terminal runs inside it.
/// </summary>
[TestClass]
public class ContentModelDockerDriveTest
{
    private const string Running = "web\trunning\tUp 2 hours\tnginx:latest\t2026-10-04 08:00:00 +0200 CEST\n";
    private const string Stopped = "web\texited\tExited (0) 1 second ago\tnginx:latest\t2026-10-04 08:00:00 +0200 CEST\n";

    private static IFrequentExecutionGuardService ImmediateGuard()
    {
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());
        return guard.Object;
    }

    private static async Task<ContentModelDockerDrive> OpenAsync(FakeProcessRunner runner, string url = "docker://")
    {
        var model = new ContentModelDockerDrive("LeftPanel", new SettingsService(), new DockerDriveService(runner), folderHistoryService: null, ImmediateGuard());
        model.SetUrl(new Url(url));
        await model.ListingTask;
        return model;
    }

    private static async Task<ContentModelDockerDrive> OnTheContainerAsync(FakeProcessRunner runner)
    {
        var model = await OpenAsync(runner);
        model.CursorPosition.Y = 1;
        Assert.IsInstanceOfType<DockerContainerEntry>(model.GetFocusedEntry());
        return model;
    }

    [TestMethod]
    public async Task AContainerRowIsNothingAFileOperationMayTouch()
    {
        var model = await OnTheContainerAsync(new FakeProcessRunner().Answer("docker ps", Running));

        Assert.IsNull(FocusedEntry.ActionableUrl(model), "F8 here used to offer to delete the container's whole file system");
        Assert.AreEqual(0, FocusedEntry.ActionableUrls(model).Count);
        Assert.IsFalse(model.CanMark, "nothing on the container list is marked for F5 or F8");
        StringAssert.Contains(model.GetFooter(), "Shift+F9: start/stop");
    }

    [TestMethod]
    public async Task ShiftF9StopsARunningContainerAndTheRowShowsIt()
    {
        var stopped = false;
        using var dockerAnswers = new ManualResetEventSlim();
        var runner = new FakeProcessRunner();
        // docker stop takes seconds on a real container; held here so the footer is read while it runs.
        runner.AnswerWith("docker stop web", () =>
        {
            dockerAnswers.Wait(TimeSpan.FromSeconds(30));
            return "web\n";
        });
        var model = await OnTheContainerAsync(runner.AnswerWith("docker ps", () => stopped ? Stopped : Running));
        var row = (DockerContainerEntry)model.GetFocusedEntry()!;

        stopped = true;
        model.RunContainerAction(row, row.IsRunning ? DockerContainerAction.Stop : DockerContainerAction.Start);
        StringAssert.Contains(model.GetFooter(), "Stopping web…", "said at once, before docker answers");
        dockerAnswers.Set();
        await model.ContainerActionTask;

        Assert.IsTrue(runner.Calls.Any(c => c.CommandLine == "docker stop web"));
        StringAssert.Contains(model.GetFooter(), "web stopped");
        Assert.AreEqual("Exited", model.GetFocusedEntry()!.FileSize, "the listing is read again, cursor still on the row");
    }

    [TestMethod]
    public async Task AFailedActionIsSaidInTheFooter()
    {
        var runner = new FakeProcessRunner()
            .Answer("docker restart web", string.Empty, exitCode: 1, stderr: "Error response from daemon: Cannot restart container web: port is already allocated")
            .Answer("docker ps", Running);
        var model = await OnTheContainerAsync(runner);

        model.RunContainerAction((DockerContainerEntry)model.GetFocusedEntry()!, DockerContainerAction.Restart);
        await model.ContainerActionTask;

        StringAssert.Contains(model.GetFooter(), "port is already allocated");
    }

    [TestMethod]
    public async Task TheQuickViewOfAContainerIsItsLogOpenedAtTheEnd()
    {
        var runner = new FakeProcessRunner()
            .Answer("docker ps", Running)
            .Answer("docker logs --tail 200 --timestamps web", "2026-10-05T08:00:00.000000000Z GET / 200\n2026-10-05T08:00:01.000000000Z GET /favicon.ico 404\n");
        var model = await OnTheContainerAsync(runner);

        var preview = await new FilePreviewService().BuildAsync(model.GetFocusedEntry(), model, 60, CancellationToken.None);

        Assert.IsTrue(preview.StartAtEnd);
        StringAssert.Contains(preview.Title, "web");
        Assert.IsTrue(preview.Lines.Any(l => l.Contains("nginx:latest")), string.Join("\n", preview.Lines));
        Assert.AreEqual(" GET /favicon.ico 404", preview.Lines[^1], "the newest line last");
    }

    [TestMethod]
    public async Task AFileInsideAContainerStillPreviewsAsAFile()
    {
        var runner = new FakeProcessRunner().Answer("docker exec web ls", "-rw-r--r-- 1 0 0 5 Oct  5 08:00 a.txt\n");
        var model = await OpenAsync(runner, "docker://web/");

        Assert.IsNull(await model.BuildPreviewAsync(model.Entries.AsSnapshot()[1], 60, CancellationToken.None));
    }

    [TestMethod]
    public async Task TheTerminalRunsInsideTheContainerInThePanelsFolder()
    {
        var runner = new FakeProcessRunner()
            .Answer("docker exec web ls", "-rw-r--r-- 1 0 0 5 Oct  5 08:00 motd\n")
            .AnswerBytes("docker exec -w /etc web sh -c cat motd", Encoding.UTF8.GetBytes("Welcome\n"));
        var model = await OpenAsync(runner, "docker://web/etc/");
        var command = new TerminalCommand { Command = "cat motd", WorkingDirectoryUrl = model.Url };

        Assert.AreEqual("web:/etc", model.TerminalPrompt);
        await model.RunTerminalCommandAsync(Terminal(command));

        Assert.AreEqual("Welcome\n", command.Output);
        Assert.AreEqual(0, command.ExitCode);
        Assert.AreEqual("web", command.Host);
    }

    [TestMethod]
    public async Task OnTheContainerListTheTerminalRunsInTheContainerUnderTheCursor()
    {
        var runner = new FakeProcessRunner()
            .Answer("docker ps", Running)
            .AnswerBytes("docker exec -w / web sh -c", Encoding.UTF8.GetBytes("/\n"));
        var model = await OpenAsync(runner);
        var command = new TerminalCommand { Command = "pwd", WorkingDirectoryUrl = model.Url };

        Assert.IsNull(model.TerminalPrompt, "the cursor is on '..': there is no container to run in");
        await model.RunTerminalCommandAsync(Terminal(command));
        StringAssert.Contains(command.Error, "put the cursor on one");

        model.CursorPosition.Y = 1;
        Assert.AreEqual("web:/", model.TerminalPrompt);
        command = new TerminalCommand { Command = "pwd", WorkingDirectoryUrl = model.Url };
        await model.RunTerminalCommandAsync(Terminal(command));
        Assert.AreEqual("/\n", command.Output);
    }

    [TestMethod]
    public async Task AStoppedContainerIsSaidInsteadOfDockersParagraph()
    {
        var runner = new FakeProcessRunner()
            .Answer("docker exec web ls", "-rw-r--r-- 1 0 0 5 Oct  5 08:00 motd\n")
            .AnswerBytes("docker exec -w", [], exitCode: 1, stderr: "Error response from daemon: container 4f2a is not running");
        var model = await OpenAsync(runner, "docker://web/");
        var command = new TerminalCommand { Command = "ls", WorkingDirectoryUrl = model.Url };

        await model.RunTerminalCommandAsync(Terminal(command));

        StringAssert.Contains(command.Error, "Shift+F9");
    }

    private static iConsole.Interfaces.ITerminalModel Terminal(TerminalCommand command)
    {
        var terminal = new Mock<iConsole.Interfaces.ITerminalModel>();
        terminal.SetupGet(t => t.Command).Returns(command);
        return terminal.Object;
    }
}
