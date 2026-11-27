using System.ComponentModel;
using System.Diagnostics;
using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.VirtualMachines;

/// <summary>
/// A libvirt backend, through the <c>virsh</c> command-line client. Like the docker drive, it goes through a CLI
/// rather than a binding, so nothing native is packaged.
///
/// <para>Two instances are registered on Linux. The default is the system connection (<c>qemu:///system</c>,
/// which needs membership of the <c>libvirt</c> group), labelled <c>libvirt</c>. The second is GNOME Boxes'
/// session connection (<c>qemu:///session</c>, no group needed), labelled <c>GNOME Boxes</c>: Boxes keeps its
/// machines there.</para>
///
/// <para>Names are passed as arguments, never quoted by hand; a name that starts with a dash is refused
/// so it cannot be read as an option. Console opens <c>virt-viewer</c> detached and does not wait for it.</para>
/// </summary>
public sealed class LibvirtVirtualMachineService(
    IExternalProcessRunner processRunner,
    Func<string, IReadOnlyList<string>, bool>? launchDetached = null,
    string connection = LibvirtVirtualMachineService.SystemConnection,
    string hypervisor = LibvirtVirtualMachineService.SystemLabel) : IVirtualMachineBackend
{
    public const string Virsh = "virsh";
    public const string VirtViewer = "virt-viewer";
    public const string SystemConnection = "qemu:///system";
    public const string SessionConnection = "qemu:///session";
    public const string SystemLabel = "libvirt";
    public const string BoxesLabel = "GNOME Boxes";

    /// <summary>The message the runner gives when a command outlives its 30-second limit.</summary>
    internal const string TimeoutMarker = "did not answer within";

    /// <summary>What the user is told when a hypervisor call outlives that limit; shared with the other backends.</summary>
    internal const string TimeoutSentence = "the hypervisor did not answer in time. A save or a shutdown can take longer; press F5 to see the state.";

    private readonly Func<string, IReadOnlyList<string>, bool> _launchDetached = launchDetached ?? LaunchDetached;
    private readonly string[] _connectArguments = ["-c", connection];

    public string Hypervisor { get; } = hypervisor;

    public async Task<IReadOnlyList<VirtualMachine>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = await processRunner.RunAsync(Virsh, [.. _connectArguments, "list", "--all"], cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(DescribeFailure(result));
        }

        return VirshListParser.Parse(result.StandardOutput)
            .Select(machine => machine with { Hypervisor = Hypervisor })
            .ToList();
    }

    public async Task<VirtualMachineActionResult> RunAsync(VirtualMachine machine, VirtualMachineAction action, CancellationToken cancellationToken = default)
    {
        var name = machine.Name;
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('-'))
        {
            return new VirtualMachineActionResult(false, $"Not a virtual machine name: {name}");
        }

        if (action == VirtualMachineAction.Console)
        {
            return _launchDetached(VirtViewer, [.. _connectArguments, name])
                ? new VirtualMachineActionResult(true, $"Opening the console of {name}.")
                : new VirtualMachineActionResult(false, "virt-viewer is not installed. Install it with: sudo apt install virt-viewer");
        }

        var result = await processRunner.RunAsync(Virsh, [.. _connectArguments, VirtualMachineActions.Verb(action), name], cancellationToken: cancellationToken);
        return result.Success
            ? new VirtualMachineActionResult(true, $"{name}: {Done(action)}.")
            : new VirtualMachineActionResult(false, $"{name}: {DescribeFailure(result)}");
    }

    internal static string Done(VirtualMachineAction action) => action switch
    {
        VirtualMachineAction.Start => "started",
        VirtualMachineAction.Shutdown => "shutdown requested",
        VirtualMachineAction.PowerOff => "powered off",
        VirtualMachineAction.Restart => "restart requested",
        VirtualMachineAction.Pause => "paused",
        VirtualMachineAction.Resume => "resumed",
        VirtualMachineAction.SaveState => "state saved and stopped",
        _ => "done",
    };

    /// <summary>Turns virsh's output into a sentence a footer can hold.</summary>
    internal string DescribeFailure(ProcessOutput result)
    {
        if (result.ExitCode == -1 && result.StandardError.Contains(TimeoutMarker, StringComparison.Ordinal))
        {
            return TimeoutSentence;
        }

        if (result.StandardError.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
            || result.StandardError.Contains("failed to connect", StringComparison.OrdinalIgnoreCase))
        {
            return connection == SystemConnection
                ? "libvirt refused the connection. Add your user to the libvirt group (sudo usermod -aG libvirt $USER), then log in again."
                : $"libvirt refused the connection to {connection}.";
        }

        if (result.ExitCode == -1)
        {
            return $"virsh could not be started. Install it with: sudo apt install libvirt-clients ({result.FirstErrorLine})";
        }

        return result.FirstErrorLine;
    }

    /// <summary>Starts virt-viewer without waiting for it. False when it is not installed.</summary>
    internal static bool LaunchDetached(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
