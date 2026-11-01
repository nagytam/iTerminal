using iConsole.Data;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Commands.Test;

/// <summary>Ctrl+F11 is the system information dialog; its title and description are what the menus and help show.</summary>
[TestClass]
public class SystemInfoDialogCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Key_IsCtrlF11_AndItHasATitleAndDescription()
    {
        var command = new SystemInfoDialogCommand(null!);

        Assert.AreEqual(ConsoleKey.F11, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Control);
        Assert.IsFalse(command.Key.Alt);
        Assert.IsFalse(command.Key.Shift);
        Assert.AreEqual("System information", command.Title);
        Assert.IsFalse(string.IsNullOrWhiteSpace(command.Description));
    }
}
