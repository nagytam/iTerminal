using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.VirtualMachines;

/// <summary>
/// The service the dialog talks to: the machines of every backend that answers, each tagged with its hypervisor,
/// and every action sent back to the backend the machine came from, so two hypervisors may hold machines with
/// the same name.
///
/// <list type="bullet">
/// <item>A backend that is not installed, or does not answer, is left out of the list. Only when no backend answers
/// is the list an error, and then the error is the first backend's sentence.</item>
/// <item>The backends are asked at the same time, so one slow hypervisor delays the list by at most its own limit.</item>
/// </list>
/// </summary>
public sealed class CombinedVirtualMachineService(IEnumerable<IVirtualMachineBackend> backends) : IVirtualMachineService
{
    private readonly IReadOnlyList<IVirtualMachineBackend> _backends = backends.ToList();

    public async Task<IReadOnlyList<VirtualMachine>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (_backends.Count == 0)
        {
            throw new InvalidOperationException("No virtual machine hypervisor is available on this computer.");
        }

        var answers = await Task.WhenAll(_backends.Select(backend => ListOneAsync(backend, cancellationToken)));
        var machines = answers.Where(answer => answer.Machines is not null).SelectMany(answer => answer.Machines!).ToList();
        if (machines.Count == 0 && answers.All(answer => answer.Error is not null))
        {
            throw new InvalidOperationException(answers[0].Error);
        }

        return machines;
    }

    public Task<VirtualMachineActionResult> RunAsync(VirtualMachine machine, VirtualMachineAction action, CancellationToken cancellationToken = default)
    {
        var backend = _backends.FirstOrDefault(candidate => candidate.Hypervisor == machine.Hypervisor);
        return backend is null
            ? Task.FromResult(new VirtualMachineActionResult(false, $"{machine.Name}: no hypervisor named {machine.Hypervisor} is available."))
            : backend.RunAsync(machine, action, cancellationToken);
    }

    private static async Task<(IReadOnlyList<VirtualMachine>? Machines, string? Error)> ListOneAsync(IVirtualMachineBackend backend, CancellationToken cancellationToken)
    {
        try
        {
            return (await backend.ListAsync(cancellationToken), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return (null, exception.Message);
        }
    }
}

/// <summary>Which hypervisor backends this computer gets, by operating system.</summary>
public static class VirtualMachineBackends
{
    public static IReadOnlyList<IVirtualMachineBackend> ForThisComputer(IExternalProcessRunner processRunner)
    {
        if (OperatingSystem.IsWindows())
        {
            return [new HyperVVirtualMachineService(processRunner), new VirtualBoxVirtualMachineService(processRunner)];
        }

        if (OperatingSystem.IsLinux())
        {
            return
            [
                new LibvirtVirtualMachineService(processRunner),
                new LibvirtVirtualMachineService(processRunner, null, LibvirtVirtualMachineService.SessionConnection, LibvirtVirtualMachineService.BoxesLabel),
                new VirtualBoxVirtualMachineService(processRunner),
            ];
        }

        return [];
    }
}
