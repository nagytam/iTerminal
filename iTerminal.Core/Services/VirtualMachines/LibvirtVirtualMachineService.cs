using System.ComponentModel;
using System.Diagnostics;
using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.VirtualMachines;

/// <summary>
/// The Linux backend: libvirt, through the <c>virsh</c> command-line client on the system connection
/// (<c>qemu:///system</c>, which needs membership of the <c>libvirt</c> group). Like the docker drive,
/// it goes through a CLI rather than a binding, so nothing native is packaged.
///
/// <para>Names are passed as arguments, never quoted by hand; a name that starts with a dash is refused
/// so it cannot be read as an option. Console opens <c>virt-viewer</c> detached and does not wait for it.</para>
/// </summary>
public sealed class LibvirtVirtualMachineService(
    IExternalProcessRunner processRunner,
    Func<string, IReadOnlyList<string>, bool>? launchDetached = null) : IVirtualMachineService
{
    public const string Virsh = "virsh";
    public const string VirtViewer = "virt-viewer";
    public const string Connection = "qemu:///system";

    private static readonly string[] ConnectArguments = ["-c", Connection];

    /// <summary>The message the runner gives when a command outlives its 30-second limit.</summary>
    internal const string TimeoutMarker = "did not answer within";

    /// <summary>What the user is told when a hypervisor call outlives that limit; shared with the Hyper-V backend.</summary>
    internal const string TimeoutSentence = "the hypervisor did not answer in time. A save or a shutdown can take longer; press F5 to see the state.";

    private readonly Func<string, IReadOnlyList<string>, bool> _launchDetached = launchDetached ?? LaunchDetached;

    public async Task<IReadOnlyList<VirtualMachine>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = await processRunner.RunAsync(Virsh, [.. ConnectArguments, "list", "--all"], cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(DescribeFailure(result));
        }

        return VirshListParser.Parse(result.StandardOutput);
    }

    public async Task<VirtualMachineActionResult> RunAsync(string name, VirtualMachineAction action, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('-'))
        {
            return new VirtualMachineActionResult(false, $"Not a virtual machine name: {name}");
        }

        if (action == VirtualMachineAction.Console)
        {
            return _launchDetached(VirtViewer, [.. ConnectArguments, name])
                ? new VirtualMachineActionResult(true, $"Opening the console of {name}.")
                : new VirtualMachineActionResult(false, "virt-viewer is not installed. Install it with: sudo apt install virt-viewer");
        }

        var result = await processRunner.RunAsync(Virsh, [.. ConnectArguments, VirtualMachineActions.Verb(action), name], cancellationToken: cancellationToken);
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
    internal static string DescribeFailure(ProcessOutput result)
    {
        if (result.ExitCode == -1 && result.StandardError.Contains(TimeoutMarker, StringComparison.Ordinal))
        {
            return TimeoutSentence;
        }

        if (result.StandardError.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
            || result.StandardError.Contains("failed to connect", StringComparison.OrdinalIgnoreCase))
        {
            return "libvirt refused the connection. Add your user to the libvirt group (sudo usermod -aG libvirt $USER), then log in again.";
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
