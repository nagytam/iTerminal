using iConsole.Data;
using iTerminal.Core.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Commands.Test;

/// <summary>F9 is the task manager; the command has its title and description for the menus and help.</summary>
[TestClass]
public class TaskManagerDialogCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Key_IsF9_AndItHasATitleAndDescription()
    {
        var command = new TaskManagerDialogCommand(null!);

        Assert.AreEqual(ConsoleKey.F9, command.Key!.ConsoleKey);
        Assert.IsFalse(command.Key.Shift);
        Assert.IsFalse(command.Key.Control);
        Assert.IsFalse(command.Key.Alt);
        Assert.AreEqual("Task manager", command.Title);
        Assert.IsFalse(string.IsNullOrWhiteSpace(command.Description));
    }
}
