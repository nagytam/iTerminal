using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using iTerminal.Core.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Tasks.Test;

/// <summary>A command typed at the terminal runs on the focused panel, lands in the history, and its failure is shown on the command.</summary>
[TestClass]
public class ExecuteCommandTaskTest
{
    private static TerminalModel NewTerminal()
    {
        var taskManager = new Moq.Mock<TaskManager>().Object;
        return new TerminalModel(taskManager, Mock.Of<IContentModelService>());
    }

    private static async Task WaitForAsync(ExecuteCommandTask task) =>
        await task.Completion.WaitAsync(TimeSpan.FromSeconds(15));

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithNothingToRun_CompletesAtOnce_AndRunsNoTask()
    {
        var task = new ExecuteCommandTask();

        await task.ExecuteAsync();

        await WaitForAsync(task);
        Assert.IsNull(task.Task);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithAPanelAndACommand_RunsItOnThePanel_AndAddsItToHistory()
    {
        var terminal = NewTerminal();
        var panel = new Mock<IContentModel>();
        panel.Setup(p => p.RunTerminalCommandAsync(It.IsAny<ITerminalModel>())).Returns(Task.CompletedTask);
        var task = new ExecuteCommandTask { ContentModel = panel.Object, TerminalModel = terminal };

        await task.ExecuteAsync();
        await WaitForAsync(task);

        panel.Verify(p => p.RunTerminalCommandAsync(terminal), Times.Once);
        Assert.AreEqual(1, terminal.History.Count);
        Assert.IsNull(terminal.History[0].Error, "a command that ran cleanly has no error");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WhenTheCommandFails_ItsMessageGoesOnTheCommand_AndItStillCompletes()
    {
        var terminal = NewTerminal();
        var panel = new Mock<IContentModel>();
        panel.Setup(p => p.RunTerminalCommandAsync(It.IsAny<ITerminalModel>())).ThrowsAsync(new IOException("no such program"));
        var task = new ExecuteCommandTask { ContentModel = panel.Object, TerminalModel = terminal };

        await task.ExecuteAsync();
        await WaitForAsync(task);

        Assert.AreEqual(1, terminal.History.Count, "a failed command is still history");
        StringAssert.Contains(terminal.History[0].Error, "no such program");
    }
}
