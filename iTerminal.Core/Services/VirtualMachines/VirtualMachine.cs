namespace iTerminal.Core.Services.VirtualMachines;

/// <summary>What the hypervisor says a machine is doing. Each backend maps its own words onto these.</summary>
public enum VirtualMachineState
{
    Running,
    Idle,
    Paused,
    Blocked,
    InShutdown,
    ShutOff,
    Crashed,
    PowerManagementSuspended,
    Unknown,
}

/// <summary>
/// One machine. <see cref="StateText"/> keeps the word the hypervisor printed, and <see cref="Hypervisor"/>
/// names the backend it belongs to (empty until the list has been through a backend).
/// </summary>
public sealed record VirtualMachine(string Name, VirtualMachineState State, string StateText, string Hypervisor = "");

public enum VirtualMachineAction
{
    Start,
    Shutdown,
    PowerOff,
    Restart,
    Pause,
    Resume,
    SaveState,
    Console,
}

/// <summary>Which actions a machine in a given state can take, and which of them need a y/n first.</summary>
public static class VirtualMachineActions
{
    /// <summary>A guest that is up and taking input: the states where a graceful shutdown, restart, pause or save applies.</summary>
    private static bool IsRunningGuest(VirtualMachineState state) =>
        state is VirtualMachineState.Running or VirtualMachineState.Idle or VirtualMachineState.Blocked;

    public static bool IsAvailable(VirtualMachine machine, VirtualMachineAction action) => action switch
    {
        VirtualMachineAction.Start => machine.State == VirtualMachineState.ShutOff,
        VirtualMachineAction.Resume => machine.State == VirtualMachineState.Paused,
        VirtualMachineAction.Pause => IsRunningGuest(machine.State),
        VirtualMachineAction.Shutdown => IsRunningGuest(machine.State),
        VirtualMachineAction.Restart => IsRunningGuest(machine.State),
        VirtualMachineAction.SaveState => IsRunningGuest(machine.State) || machine.State == VirtualMachineState.Paused,
        VirtualMachineAction.PowerOff => machine.State is not (VirtualMachineState.ShutOff or VirtualMachineState.Unknown),
        VirtualMachineAction.Console => machine.State is not (VirtualMachineState.ShutOff or VirtualMachineState.Unknown),
        _ => false,
    };

    /// <summary>Actions that stop or restart a guest ask y/n first; pause, resume, start and console act at once.</summary>
    public static bool NeedsConfirmation(VirtualMachineAction action) =>
        action is VirtualMachineAction.Shutdown or VirtualMachineAction.PowerOff
            or VirtualMachineAction.Restart or VirtualMachineAction.SaveState;

    /// <summary>The virsh subcommand for an action. Console is not a virsh command: it launches virt-viewer.</summary>
    public static string Verb(VirtualMachineAction action) => action switch
    {
        VirtualMachineAction.Start => "start",
        VirtualMachineAction.Shutdown => "shutdown",
        VirtualMachineAction.PowerOff => "destroy",
        VirtualMachineAction.Restart => "reboot",
        VirtualMachineAction.Pause => "suspend",
        VirtualMachineAction.Resume => "resume",
        VirtualMachineAction.SaveState => "managedsave",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Console is not a virsh command."),
    };
}
