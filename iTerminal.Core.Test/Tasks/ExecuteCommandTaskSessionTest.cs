using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Model;
using iTerminal.Core.Services.Shell;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Tasks.Test;

/// <summary>
/// A command on a local folder with a shell session runs in the session; a remote drive, or no session, runs as before.
/// The session's folder is followed by the panel only while the panel still shows the folder the command started from.
/// </summary>
[TestClass]
public class ExecuteCommandTaskSessionTest
{
    private static async Task WaitForAsync(ExecuteCommandTask task) =>
        await task.Completion.WaitAsync(TimeSpan.FromSeconds(15));

    private static Mock<IContentModel> Panel(string url)
    {
        var panel = new Mock<IContentModel>();
        panel.SetupGet(p => p.Url).Returns(new Url(url));
        panel.SetupGet(p => p.Name).Returns("LeftPanel");
        panel.Setup(p => p.RunTerminalCommandAsync(It.IsAny<ITerminalModel>())).Returns(Task.CompletedTask);
        return panel;
    }

    private static (TerminalModel Terminal, Mock<IContentModelService> Service) Terminal(Mock<IContentModel> panel, ILocalShellSessionService? sessions)
    {
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetLastContentModel()).Returns(panel.Object);
        service.Setup(s => s.SetUrlAsync(It.IsAny<string>(), It.IsAny<Url>())).Returns(Task.CompletedTask);
        return (new TerminalModel(new TaskManager(), service.Object, sessions), service);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ALocalCommand_RunsInTheSession_InTheCommandsFolder_AndRecordsTheResult()
    {
        var sessions = new FakeShellSessions();
        var panel = Panel("/work/");
        var (terminal, _) = Terminal(panel, sessions);
        terminal.Command.Command = "make";
        terminal.Command.WorkingDirectoryUrl = new Url("/work/");
        var task = new ExecuteCommandTask { ContentModel = panel.Object, TerminalModel = terminal };

        await task.ExecuteAsync();
        await WaitForAsync(task);

        CollectionAssert.AreEqual(new[] { ("/work/", "make") }, sessions.Calls.ToArray());
        panel.Verify(p => p.RunTerminalCommandAsync(It.IsAny<ITerminalModel>()), Times.Never);
        Assert.AreEqual("fake: make", terminal.History[0].Output);
        Assert.AreEqual(Environment.MachineName, terminal.History[0].Host);
        Assert.IsFalse(string.IsNullOrEmpty(terminal.History[0].EnvironmentVariables));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ARemoteDrive_RunsAsBefore_AndNeverTouchesTheSession()
    {
        var sessions = new FakeShellSessions();
        var panel = Panel("ssh://me@host/home/");
        var (terminal, _) = Terminal(panel, sessions);
        terminal.Command.Command = "ls";
        var task = new ExecuteCommandTask { ContentModel = panel.Object, TerminalModel = terminal };

        await task.ExecuteAsync();
        await WaitForAsync(task);

        Assert.AreEqual(0, sessions.Calls.Count);
        panel.Verify(p => p.RunTerminalCommandAsync(terminal), Times.Once);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task WithNoSessionAvailable_ALocalCommand_RunsAsBefore()
    {
        var sessions = new FakeShellSessions { IsAvailable = false };
        var panel = Panel("/work/");
        var (terminal, _) = Terminal(panel, sessions);
        terminal.Command.Command = "ls";
        var task = new ExecuteCommandTask { ContentModel = panel.Object, TerminalModel = terminal };

        await task.ExecuteAsync();
        await WaitForAsync(task);

        Assert.AreEqual(0, sessions.Calls.Count);
        panel.Verify(p => p.RunTerminalCommandAsync(terminal), Times.Once);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task WhenThePanelStillShowsTheStartFolder_ItFollowsTheSessionsFolder()
    {
        var sessions = new FakeShellSessions { Answer = (folder, command) => new ShellCommandResult(string.Empty, string.Empty, 0, "/work/sub", ShellRunState.Completed) };
        var panel = Panel("/work/");
        var (terminal, service) = Terminal(panel, sessions);
        terminal.Command.Command = "cd sub";
        terminal.Command.WorkingDirectoryUrl = new Url("/work/");
        var task = new ExecuteCommandTask { ContentModel = panel.Object, TerminalModel = terminal };

        await task.ExecuteAsync();
        await WaitForAsync(task);

        service.Verify(s => s.SetUrlAsync("LeftPanel", It.Is<Url>(u => (string)u == (string)new Url("/work/sub/"))), Times.Once);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task WhenThePanelHasMovedOn_ItIsLeftWhereItIs()
    {
        var panel = Panel("/work/");
        // The user navigates away while the command runs, so the panel no longer shows the folder the command started from.
        var sessions = new FakeShellSessions
        {
            Answer = (folder, command) =>
            {
                panel.SetupGet(p => p.Url).Returns(new Url("/elsewhere/"));
                return new ShellCommandResult(string.Empty, string.Empty, 0, "/work/sub", ShellRunState.Completed);
            },
        };
        var (terminal, service) = Terminal(panel, sessions);
        terminal.Command.Command = "cd sub";
        terminal.Command.WorkingDirectoryUrl = new Url("/work/");
        var task = new ExecuteCommandTask { ContentModel = panel.Object, TerminalModel = terminal };

        await task.ExecuteAsync();
        await WaitForAsync(task);

        service.Verify(s => s.SetUrlAsync(It.IsAny<string>(), It.IsAny<Url>()), Times.Never);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AnInterruptedCommand_SaysSo_AndTheFolderIsNotFollowed()
    {
        var sessions = new FakeShellSessions { Answer = (folder, command) => new ShellCommandResult(string.Empty, string.Empty, -1, folder, ShellRunState.Interrupted) };
        var panel = Panel("/work/");
        var (terminal, service) = Terminal(panel, sessions);
        terminal.Command.Command = "sleep 99";
        terminal.Command.WorkingDirectoryUrl = new Url("/work/");
        var task = new ExecuteCommandTask { ContentModel = panel.Object, TerminalModel = terminal };

        await task.ExecuteAsync();
        await WaitForAsync(task);

        StringAssert.Contains(terminal.History[0].Error, "Interrupted with Esc");
        service.Verify(s => s.SetUrlAsync(It.IsAny<string>(), It.IsAny<Url>()), Times.Never);
    }
}
