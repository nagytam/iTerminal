namespace iTerminal.Core.Services.VirtualMachines;

/// <summary>The outcome of one action: what to tell the user, and whether it worked.</summary>
public sealed record VirtualMachineActionResult(bool Success, string Message);

/// <summary>
/// The hypervisor behind the Virtual Machine Manager. The Linux implementation drives libvirt through
/// <c>virsh</c>; it is injectable so the dialog and the tests never reach a real hypervisor.
/// </summary>
public interface IVirtualMachineService
{
    /// <summary>Every domain, running or not. Throws <see cref="InvalidOperationException"/> with a sentence for the user.</summary>
    Task<IReadOnlyList<VirtualMachine>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs one action on the named machine. Failures come back as a result, not an exception.</summary>
    Task<VirtualMachineActionResult> RunAsync(string name, VirtualMachineAction action, CancellationToken cancellationToken = default);
}
