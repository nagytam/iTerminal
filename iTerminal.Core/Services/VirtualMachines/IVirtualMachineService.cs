namespace iTerminal.Core.Services.VirtualMachines;

/// <summary>The outcome of one action: what to tell the user, and whether it worked.</summary>
public sealed record VirtualMachineActionResult(bool Success, string Message);

/// <summary>
/// What the Virtual Machine Manager lists and runs. The dialog talks to one of these: on this computer that is
/// a <see cref="CombinedVirtualMachineService"/> over every hypervisor backend that is installed.
/// </summary>
public interface IVirtualMachineService
{
    /// <summary>Every machine, running or not, from every hypervisor that answers. Throws <see cref="InvalidOperationException"/> with a sentence for the user when none does.</summary>
    Task<IReadOnlyList<VirtualMachine>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs one action on the machine, in the hypervisor it was listed by. Failures come back as a result, not an exception.</summary>
    Task<VirtualMachineActionResult> RunAsync(VirtualMachine machine, VirtualMachineAction action, CancellationToken cancellationToken = default);
}

/// <summary>
/// One hypervisor: libvirt (system or GNOME Boxes' session), Hyper-V or VirtualBox. Its machines carry
/// <see cref="Hypervisor"/> as their label, which is how <see cref="CombinedVirtualMachineService"/> routes an action back to it.
/// </summary>
public interface IVirtualMachineBackend : IVirtualMachineService
{
    /// <summary>The name shown next to each machine, and the key its machines are routed by.</summary>
    string Hypervisor { get; }
}
