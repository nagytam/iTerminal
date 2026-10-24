using iConsole.Data;
using iTerminal.Core.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Commands.Test;

/// <summary>Alt+F9 opens the disk manager, beside the task manager on F9.</summary>
[TestClass]
public class DiskManagerDialogCommandTest
{
    [TestMethod]
    public void Key_IsAltF9_NotTheTaskManagersF9()
    {
        var command = new DiskManagerDialogCommand(null!);

        Assert.AreEqual(ConsoleKey.F9, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Alt);
        Assert.IsFalse(command.Key.Shift);
        Assert.IsFalse(command.Key.Control);
        Assert.AreEqual("Disk manager", command.Title);
        Assert.IsFalse(string.IsNullOrWhiteSpace(command.Description));
    }
}
