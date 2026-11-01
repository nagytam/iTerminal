using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Shift+Alt+F9 and Tools &gt; Virtual Machine Manager. Only on Linux for now: the backend is libvirt, and
/// the Hyper-V backend for Windows is not written yet, so elsewhere the command is not available.
/// </summary>
public class VirtualMachineManagerDialogCommand(VirtualMachineManagerDialog virtualMachineManagerDialog) : ShowDialogCommand<VirtualMachineManagerDialog>(virtualMachineManagerDialog)
{
    public override string Title => "Virtual machine manager";
    public override string Description => "Start, stop, pause, restart and open the consoles of the virtual machines.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F9, true, true, false);

    public override bool CanExecute() => OperatingSystem.IsLinux() && base.CanExecute();
}
