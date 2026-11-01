using iConsole.Data;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Commands.Test;

[TestClass]
public class VirtualMachineManagerDialogCommandTest
{
    [TestMethod]
    public void IsBoundToShiftAltF9()
    {
        var command = new VirtualMachineManagerDialogCommand(null!);

        Assert.AreEqual(ConsoleKey.F9, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Shift);
        Assert.IsTrue(command.Key.Alt);
        Assert.IsFalse(command.Key.Control);
    }

    // Reverted (the OS gate is removed): on Windows CanExecute is true and this fails; on Linux both sides are true.
    // CI runs both platforms, so the Windows run is what proves the gate.
    [TestMethod]
    public void IsAvailableOnlyOnLinux()
    {
        var command = new VirtualMachineManagerDialogCommand(null!);

        Assert.AreEqual(OperatingSystem.IsLinux(), command.CanExecute());
    }
}
