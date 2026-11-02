using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Shift+Alt+F9 and Tools &gt; Virtual Machine Manager. It needs a hypervisor backend, which exists on Linux
/// (libvirt) and Windows (Hyper-V); elsewhere the command is not available.
/// </summary>
public class VirtualMachineManagerDialogCommand(VirtualMachineManagerDialog virtualMachineManagerDialog) : ShowDialogCommand<VirtualMachineManagerDialog>(virtualMachineManagerDialog)
{
    /// <summary>Whether this operating system has a hypervisor backend. The hot key and the Tools menu ask the same question.</summary>
    public static bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

    public override string Title => "Virtual machine manager";
    public override string Description => "Start, stop, pause, restart and open the consoles of the virtual machines.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F9, true, true, false);

    public override bool CanExecute() => IsSupported && base.CanExecute();
}
