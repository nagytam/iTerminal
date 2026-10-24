using iConsole.Data;
using iTerminal.Core.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Commands.Test;

/// <summary>Alt+F6 opens the theme chooser.</summary>
[TestClass]
public class ThemeManagerDialogCommandTest
{
    [TestMethod]
    public void Key_IsAltF6_AndItHasATitleAndDescription()
    {
        var command = new ThemeManagerDialogCommand(null!);

        Assert.AreEqual(ConsoleKey.F6, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Alt);
        Assert.IsFalse(command.Key.Control);
        Assert.IsFalse(command.Key.Shift);
        Assert.AreEqual("Themes", command.Title);
        Assert.IsFalse(string.IsNullOrWhiteSpace(command.Description));
    }
}
