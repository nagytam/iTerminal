using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Model;
using iTerminal.Core.Services.Shell;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Model.Test;

/// <summary>The "shell" line that the command log answers itself, and the panel following a session's folder.</summary>
[TestClass]
public class TerminalModelShellTest
{
    private static TerminalModel NewTerminal(IContentModelService? contentModelService = null, ILocalShellSessionService? sessions = null) =>
        new(new TaskManager(), contentModelService ?? Mock.Of<IContentModelService>(), sessions);

    [TestMethod]
    public void ShellLineArgument_IsEmptyForABareShell_TheNameForAShellLine_AndNullForAnythingElse()
    {
        Assert.AreEqual(string.Empty, TerminalModel.ShellLineArgument("shell"));
        Assert.AreEqual("bash", TerminalModel.ShellLineArgument("shell bash"));
        Assert.AreEqual("sh", TerminalModel.ShellLineArgument("  shell   sh  "));
        Assert.IsNull(TerminalModel.ShellLineArgument("shell sh && ls"));
        Assert.IsNull(TerminalModel.ShellLineArgument("ls shell"));
        Assert.IsNull(TerminalModel.ShellLineArgument("shellfish"));
    }

    [TestMethod]
    public void RunShellLine_BareShell_ListsTheShellsAndMarksTheCurrentOne()
    {
        var sessions = new FakeShellSessions();
        var terminal = NewTerminal(sessions: sessions);
        var command = new TerminalCommand { Command = "shell" };

        terminal.RunShellLineAsync(command, string.Empty).Wait();

        Assert.IsNull(command.Error);
        StringAssert.Contains(command.Output, "sh, bash");
        StringAssert.Contains(command.Output, "Current: sh");
    }

    [TestMethod]
    public void RunShellLine_NamedShell_SwitchesTheSession()
    {
        var sessions = new FakeShellSessions();
        var terminal = NewTerminal(sessions: sessions);
        var command = new TerminalCommand { Command = "shell bash" };

        terminal.RunShellLineAsync(command, "bash").Wait();

        Assert.AreEqual(ShellKind.Bash, sessions.Selected);
        StringAssert.Contains(command.Output, "Shell is now bash");
        Assert.AreEqual(0, command.ExitCode);
        Assert.IsNull(command.Error);
    }

    [TestMethod]
    public void RunShellLine_AnUnknownName_IsRefusedWithTheAvailableShells()
    {
        var sessions = new FakeShellSessions();
        var terminal = NewTerminal(sessions: sessions);
        var command = new TerminalCommand { Command = "shell fish" };

        terminal.RunShellLineAsync(command, "fish").Wait();

        Assert.AreEqual(ShellKind.Sh, sessions.Selected, "a refused name changes nothing");
        StringAssert.Contains(command.Error, "fish is not available here");
        Assert.AreEqual(1, command.ExitCode);
    }

    [TestMethod]
    public void RunShellLine_WhileACommandRuns_IsRefused()
    {
        var sessions = new FakeShellSessions { IsBusy = true };
        var terminal = NewTerminal(sessions: sessions);
        var command = new TerminalCommand { Command = "shell bash" };

        terminal.RunShellLineAsync(command, "bash").Wait();

        Assert.AreEqual(ShellKind.Sh, sessions.Selected);
        StringAssert.Contains(command.Error, "A command is running");
    }

    [TestMethod]
    public void RunShellLine_WithoutASession_SaysSoAndRunsNothing()
    {
        var terminal = NewTerminal(sessions: null);
        var command = new TerminalCommand { Command = "shell" };

        terminal.RunShellLineAsync(command, string.Empty).Wait();

        StringAssert.Contains(command.Error, "No shell session is available here");
        Assert.AreEqual(1, command.ExitCode);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task StartCommand_ShellLine_IsAnsweredByTheAppAndNeverSentToTheSession()
    {
        var sessions = new FakeShellSessions();
        var terminal = NewTerminal(sessions: sessions);
        terminal.Command.Command = "shell bash";

        terminal.StartCommand();

        await UiTestHelper.WaitUntilAsync(() => terminal.History.Count == 1, "the shell line is recorded in the history");
        StringAssert.Contains(terminal.History[0].Output, "Shell is now bash");
        Assert.AreEqual(0, sessions.Calls.Count);
    }

    [TestMethod]
    public void FollowPanel_MovesThePanel_WhileItStillShowsTheStartFolder()
    {
        // Platform paths: the follow adds Path.DirectorySeparatorChar, so a "/" folder would not match on Windows.
        string work = Path.Combine(Path.GetTempPath(), "work") + Path.DirectorySeparatorChar;
        string sub = Path.Combine(work, "sub");
        var model = new Mock<IContentModel>();
        model.SetupGet(m => m.Url).Returns(new Url(work));
        model.SetupGet(m => m.Name).Returns("LeftPanel");
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetLastContentModel()).Returns(model.Object);
        contentModelService.Setup(s => s.SetUrlAsync("LeftPanel", It.IsAny<Url>())).Returns(Task.CompletedTask);
        var terminal = NewTerminal(contentModelService.Object);

        terminal.FollowPanel(model.Object, new Url(work), sub);

        contentModelService.Verify(s => s.SetUrlAsync("LeftPanel", It.Is<Url>(u => (string)u == (string)new Url(sub + Path.DirectorySeparatorChar))), Times.Once);
    }

    [TestMethod]
    public void FollowPanel_LeavesThePanelAlone_WhenTheUserHasMovedOn()
    {
        var model = new Mock<IContentModel>();
        model.SetupGet(m => m.Url).Returns(new Url("/elsewhere/"));
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetLastContentModel()).Returns(model.Object);
        var terminal = NewTerminal(contentModelService.Object);

        terminal.FollowPanel(model.Object, new Url("/work/"), "/work/sub");

        contentModelService.Verify(s => s.SetUrlAsync(It.IsAny<string>(), It.IsAny<Url>()), Times.Never);
    }

    [TestMethod]
    public void SandboxLineArgument_IsEmptyForABareSandbox_TheNameForASandboxLine_AndNullForAnythingElse()
    {
        Assert.AreEqual(string.Empty, TerminalModel.SandboxLineArgument("sandbox"));
        Assert.AreEqual("bubblewrap", TerminalModel.SandboxLineArgument("sandbox bubblewrap"));
        Assert.IsNull(TerminalModel.SandboxLineArgument("sandbox none; ls"));
        Assert.IsNull(TerminalModel.SandboxLineArgument("sandboxed"));
        Assert.IsNull(TerminalModel.SandboxLineArgument("shell sandbox"));
    }

    [TestMethod]
    public void RunSandboxLine_BareSandbox_ListsTheAvailableSandboxesAndMarksTheCurrentOne()
    {
        var sessions = new FakeShellSessions();
        var terminal = NewTerminal(sessions: sessions);
        var command = new TerminalCommand { Command = "sandbox" };

        terminal.RunSandboxLineAsync(command, string.Empty).Wait();

        Assert.IsNull(command.Error);
        StringAssert.Contains(command.Output, "none, bubblewrap");
        StringAssert.Contains(command.Output, "Current: none");
    }

    [TestMethod]
    public void RunSandboxLine_NamedSandbox_SwitchesTheSession()
    {
        var sessions = new FakeShellSessions();
        var terminal = NewTerminal(sessions: sessions);
        var command = new TerminalCommand { Command = "sandbox bubblewrap" };

        terminal.RunSandboxLineAsync(command, "bubblewrap").Wait();

        Assert.AreEqual("bubblewrap", sessions.SelectedSandbox);
        StringAssert.Contains(command.Output, "Sandbox is now bubblewrap");
        Assert.AreEqual(0, command.ExitCode);
    }

    [TestMethod]
    public void RunSandboxLine_AnUnavailableName_IsRefusedWithTheAvailableOnes()
    {
        var sessions = new FakeShellSessions { Sandboxes = ["none"] };
        var terminal = NewTerminal(sessions: sessions);
        var command = new TerminalCommand { Command = "sandbox bubblewrap" };

        terminal.RunSandboxLineAsync(command, "bubblewrap").Wait();

        StringAssert.Contains(command.Error, "sandbox: bubblewrap is not available here. Available: none");
        Assert.AreEqual(1, command.ExitCode);
        Assert.AreEqual("none", sessions.SelectedSandbox);
    }

    [TestMethod]
    public void RunSandboxLine_WhileACommandRuns_IsRefused()
    {
        var sessions = new FakeShellSessions { IsBusy = true };
        var terminal = NewTerminal(sessions: sessions);
        var command = new TerminalCommand { Command = "sandbox bubblewrap" };

        terminal.RunSandboxLineAsync(command, "bubblewrap").Wait();

        StringAssert.Contains(command.Error, "A command is running");
        Assert.AreEqual("none", sessions.SelectedSandbox);
    }
}
