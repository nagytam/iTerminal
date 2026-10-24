using iConsole.Data;
using iTerminal.Core.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Commands.Test;

/// <summary>F2 opens the terminal dialog.</summary>
[TestClass]
public class TerminalDialogCommandTest
{
    [TestMethod]
    public void Key_IsF2_AndItHasATitleAndDescription()
    {
        var command = new TerminalDialogCommand(null!);

        Assert.AreEqual(ConsoleKey.F2, command.Key!.ConsoleKey);
        Assert.IsFalse(command.Key.Shift);
        Assert.IsFalse(command.Key.Control);
        Assert.IsFalse(command.Key.Alt);
        Assert.AreEqual("Terminal", command.Title);
        Assert.IsFalse(string.IsNullOrWhiteSpace(command.Description));
    }
}
