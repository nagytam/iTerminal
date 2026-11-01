using iConsole.Data;
using iTerminal.Core.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Commands.Test;

/// <summary>Ctrl+Shift+O shows or hides the command log; it is a no-op when no content is attached to it.</summary>
[TestClass]
public class ToggleTerminalCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Key_IsCtrlShiftO()
    {
        var key = new ToggleTerminalCommand(null).Key;

        Assert.IsNotNull(key);
        Assert.AreEqual(ConsoleKey.O, key.ConsoleKey);
        Assert.IsTrue(key.Shift);
        Assert.IsTrue(key.Control);
        Assert.IsFalse(key.Alt);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_WithoutContent_CompletesWithoutThrowing()
    {
        await new ToggleTerminalCommand(null).ExecuteAsync(null);
    }
}
