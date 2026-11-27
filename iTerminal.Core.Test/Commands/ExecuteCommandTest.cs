using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Model;
using Moq;

namespace iTerminal.Core.Commands.Test;

[TestClass]
public class ExecuteCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_Default_ShouldCreateInstance()
    {
        // Arrange

        // Act
        var type = typeof(iTerminal.Core.Commands.ExecuteCommand);
        object? instance = null;
        try { instance = Activator.CreateInstance(type); }
        catch
        {
            foreach (var c in type.GetConstructors())
            {
                var args = c.GetParameters().Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
                try { instance = c.Invoke(args); break; } catch { }
            }
        }

        Assert.IsNotNull(instance, "Could not instantiate iTerminal.Core.Commands.ExecuteCommand");
    }

    /// <summary>A panel with one entry under the cursor.</summary>
    private static Mock<IContentModel> PanelWith(string name, string fullName)
    {
        var entry = new Mock<IEntry>();
        entry.SetupGet(e => e.Name).Returns(name);
        entry.SetupGet(e => e.FullName).Returns(fullName);

        var panel = new Mock<IContentModel>();
        panel.SetupGet(p => p.FilteredEntries).Returns(new List<IEntry> { entry.Object });
        panel.SetupGet(p => p.CursorPosition).Returns(new Position(0, 0));
        panel.Setup(p => p.RunTerminalCommandAsync(It.IsAny<ITerminalModel>())).Returns(Task.CompletedTask);
        return panel;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_RunsTheProgram_AndShowsTheTerminalDialogOnIt()
    {
        var gate = new TaskCompletionSource();
        var panel = PanelWith("build.cmd", Path.Combine(Path.GetTempPath(), "build.cmd"));
        panel.Setup(p => p.RunTerminalCommandAsync(It.IsAny<ITerminalModel>())).Returns(gate.Task);
        var terminal = new TerminalModel(new TaskManager(), Mock.Of<IContentModelService>());
        var started = terminal.Command;
        var shown = 0;
        var command = new ExecuteCommand(panel.Object, terminal, () => { shown++; return Task.CompletedTask; });

        await command.ExecuteAsync(null);

        Assert.AreEqual(1, shown, "the terminal dialog opens once");
        Assert.IsTrue(started.Command.Contains("build.cmd"), "the program is the command");
        Assert.IsTrue(terminal.IsRunning, "the command is marked running while it runs");

        var running = started.CommandTask!;
        gate.SetResult();
        await running;
        Assert.IsFalse(terminal.IsRunning);
        Assert.AreEqual(1, terminal.History.Count, "the finished command is in the history the dialog shows");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WhileACommandRuns_EnterStartsNothingAndOpensNoDialog()
    {
        var panel = PanelWith("build.cmd", Path.Combine(Path.GetTempPath(), "build.cmd"));
        var terminal = new TerminalModel(new TaskManager(), Mock.Of<IContentModelService>());
        terminal.Command.CommandTask = new TaskCompletionSource().Task;
        var shown = 0;
        var command = new ExecuteCommand(panel.Object, terminal, () => { shown++; return Task.CompletedTask; });

        await command.ExecuteAsync(null);

        Assert.AreEqual(0, shown);
        Assert.AreEqual(0, terminal.History.Count);
    }
}
