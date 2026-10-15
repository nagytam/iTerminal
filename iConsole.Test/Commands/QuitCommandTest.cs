using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;
using iConsole.Data;

namespace iConsole.Commands.Test;

[TestClass]
public class QuitCommandTest
{
    [TestMethod]
    public void TypeExists()
    {
        Assert.IsNotNull(typeof(QuitCommand));
    }

    [TestMethod]
    public async Task F10AndCtrlF10Quit()
    {
        var command = new QuitCommand();

        Assert.IsTrue(await command.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.F10, false, false, false)));
        Assert.IsTrue(await command.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.F10, false, false, true)));
    }

    [TestMethod]
    public async Task ShiftF10DoesNotQuit()
    {
        var command = new QuitCommand();

        Assert.IsFalse(await command.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.F10, true, false, false)));
    }
}
