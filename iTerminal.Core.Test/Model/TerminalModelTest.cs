using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Model;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Model;

/// <summary>
/// Running a terminal command. What is pinned down here is that starting one never blocks the key
/// handler that started it, and that the command reads as running for exactly as long as it runs.
/// </summary>
[TestClass]
public class TerminalModelTest
{
    private static (TerminalModel Model, TaskCompletionSource Gate, Mock<IContentModel> ContentModel, TaskManager TaskManager) Create()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contentModel = new Mock<IContentModel>();
        contentModel.Setup(m => m.RunTerminalCommandAsync(It.IsAny<ITerminalModel>()))
            .Returns<ITerminalModel>(async terminalModel =>
            {
                await gate.Task;
                terminalModel.Command.Output = "done";
            });
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetLastContentModel()).Returns(contentModel.Object);
        var taskManager = new TaskManager();
        return (new TerminalModel(taskManager, contentModelService.Object), gate, contentModel, taskManager);
    }

    [TestMethod]
    public async Task StartCommandReturnsAtOnceAndTheCommandReadsAsRunningUntilItFinishes()
    {
        var (model, gate, _, taskManager) = Create();
        using var _ = taskManager;
        model.Command.Command = "dir";

        var started = DateTime.UtcNow;
        model.StartCommand();

        Assert.IsTrue(DateTime.UtcNow - started < TimeSpan.FromSeconds(1), "StartCommand must not wait for the command");
        Assert.IsTrue(model.IsRunning, "the command is running while its content model is");

        gate.SetResult();

        await WaitUntilAsync(() => !model.IsRunning && model.History.Count == 1, "the command to finish and reach the history");
        Assert.AreEqual("dir", model.History[0].Command);
        Assert.AreEqual("done", model.History[0].Output);
        Assert.IsNull(model.History[0].CommandTask, "a finished command is no longer marked running");
        Assert.AreEqual(string.Empty, model.Command.Command, "a fresh command line follows");
    }

    [TestMethod]
    public async Task ASecondStartWhileRunningDoesNotRunTheCommandTwice()
    {
        var (model, gate, contentModel, taskManager) = Create();
        using var _ = taskManager;
        model.Command.Command = "build";

        model.StartCommand();
        model.StartCommand();
        gate.SetResult();

        await WaitUntilAsync(() => !model.IsRunning && model.History.Count == 1, "the command to finish");
        contentModel.Verify(m => m.RunTerminalCommandAsync(It.IsAny<ITerminalModel>()), Times.Once);
    }

    [TestMethod]
    public async Task AFailingCommandIsRecordedWithItsError()
    {
        var contentModel = new Mock<IContentModel>();
        contentModel.Setup(m => m.RunTerminalCommandAsync(It.IsAny<ITerminalModel>()))
            .ThrowsAsync(new InvalidOperationException("no such command"));
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetLastContentModel()).Returns(contentModel.Object);
        using var taskManager = new TaskManager();
        var model = new TerminalModel(taskManager, contentModelService.Object);
        model.Command.Command = "nope";

        model.StartCommand();

        await WaitUntilAsync(() => !model.IsRunning && model.History.Count == 1, "the failed command to reach the history");
        StringAssert.Contains(model.History[0].Error, "no such command");
    }

    [TestMethod]
    public async Task WithNoPanelToRunInTheCommandDoesNotStayRunning()
    {
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetLastContentModel()).Returns((IContentModel?)null);
        using var taskManager = new TaskManager();
        var model = new TerminalModel(taskManager, contentModelService.Object);

        model.StartCommand();

        await WaitUntilAsync(() => !model.IsRunning, "a command with nothing to run in to be released");
    }

    [TestMethod]
    public async Task StartingAndFinishingRaisePropertyChanged()
    {
        var (model, gate, _, taskManager) = Create();
        using var _ = taskManager;
        var changes = 0;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TerminalModel.Command))
            {
                Interlocked.Increment(ref changes);
            }
        };

        model.StartCommand();
        Assert.IsTrue(changes >= 1, "the prompt repaints to show the command running");

        gate.SetResult();
        await WaitUntilAsync(() => !model.IsRunning, "the command to finish");
        await WaitUntilAsync(() => changes >= 3, "start, the history swap and the finish each repaint");
    }
}
