using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Commands.CommandLine;
using iTerminal.Core.Model;
using Moq;

namespace iTerminal.Core.Commands.CommandLine.Test;

/// <summary>Enter with a command on the command line runs it in the panel's folder and opens the terminal dialog on it.</summary>
[TestClass]
public class RunCommandLineCommandTest
{
    private static (Mock<IContentModel> Panel, Mock<IContentModelService> Services) FocusedPanel()
    {
        var panel = new Mock<IContentModel>();
        panel.SetupGet(p => p.Url).Returns(new Url(Path.GetTempPath()));
        panel.Setup(p => p.RunTerminalCommandAsync(It.IsAny<ITerminalModel>())).Returns(Task.CompletedTask);
        var services = new Mock<IContentModelService>();
        services.Setup(s => s.GetFocusedModel()).Returns(panel.Object);
        services.Setup(s => s.GetLastContentModel()).Returns(panel.Object);
        return (panel, services);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AStartedCommand_OpensTheTerminalDialog()
    {
        var (panel, services) = FocusedPanel();
        var terminal = new TerminalModel(new TaskManager(), services.Object);
        var gate = new TaskCompletionSource();
        panel.Setup(p => p.RunTerminalCommandAsync(It.IsAny<ITerminalModel>())).Returns(gate.Task);
        terminal.Command.Command = "dir";
        var started = terminal.Command;
        var shown = 0;
        var command = new RunCommandLineCommand(panel.Object, services.Object, terminal, () => true, () => { shown++; return Task.CompletedTask; });

        await command.ExecuteAsync(null);

        Assert.AreEqual(1, shown);
        Assert.IsTrue(terminal.IsRunning, "the dialog is opened on a command that is running");
        var running = started.CommandTask!;
        gate.SetResult();
        await running;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACdMovesThePanelAndOpensNoDialog()
    {
        // A plain cd changes the panel's folder and starts no command, so there is no output to show.
        var (panel, services) = FocusedPanel();
        var terminal = new TerminalModel(new TaskManager(), services.Object);
        terminal.Command.Command = "cd ..";
        var shown = 0;
        var command = new RunCommandLineCommand(panel.Object, services.Object, terminal, () => true, () => { shown++; return Task.CompletedTask; });

        await command.ExecuteAsync(null);

        Assert.AreEqual(0, shown);
    }
}
