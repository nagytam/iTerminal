
using iTerminal.Core.Commands;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Commands.Test;

/// <summary>Enter on the command line runs the command from the panel's folder; with no panel there is nowhere to run it, so nothing starts.</summary>
[TestClass]
public class TerminalExecuteCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Key_IsEnter()
    {
        var command = new TerminalExecuteCommand(null!);

        Assert.AreEqual(ConsoleKey.Enter, command.Key!.ConsoleKey);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithNoPanel_StartsNothing()
    {
        var terminal = new TerminalModel(new Mock<iConsole.Tasks.TaskManager>().Object, Mock.Of<iConsole.Interfaces.IContentModelService>());
        var contentModelService = new Mock<iConsole.Interfaces.IContentModelService>();
        contentModelService.Setup(s => s.GetLastContentModel()).Returns((iConsole.Interfaces.IContentModel?)null);
        var command = new TerminalExecuteCommand(terminal) { ContentModelService = contentModelService.Object };
        var workingDirectoryBefore = terminal.Command.WorkingDirectoryUrl;

        await command.ExecuteAsync(null);

        Assert.AreSame(workingDirectoryBefore, terminal.Command.WorkingDirectoryUrl, "the folder is not changed");
        Assert.IsNull(terminal.Command.CommandTask, "no command was started");
    }
}
