using iConsole.Data;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Commands.Test;

[TestClass]
public class VirtualMachineManagerDialogCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void IsBoundToShiftAltF9()
    {
        var command = new VirtualMachineManagerDialogCommand(null!);

        Assert.AreEqual(ConsoleKey.F9, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Shift);
        Assert.IsTrue(command.Key.Alt);
        Assert.IsFalse(command.Key.Control);
    }

    // Reverted (Windows is dropped from the gate): on Windows CanExecute is false and this fails.
    // On macOS neither side is true, so CI's macOS run, if any, is what proves the other half of the gate.
    [Timeout(60_000)]
    [TestMethod]
    public void IsAvailableWhereAHypervisorBackendExists()
    {
        var command = new VirtualMachineManagerDialogCommand(null!);

        bool expected = OperatingSystem.IsLinux() || OperatingSystem.IsWindows();
        Assert.AreEqual(expected, VirtualMachineManagerDialogCommand.IsSupported);
        Assert.AreEqual(expected, command.CanExecute());
    }
}
