using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.VirtualMachines;

/// <summary>
/// The VirtualBox backend, through <c>VBoxManage</c>. VirtualBox runs on Linux and Windows (and macOS, where the
/// dialog is not offered yet), so this one service serves every platform the dialog runs on.
///
/// <list type="bullet">
/// <item>The list is <c>VBoxManage list --long vms</c>; <see cref="VirtualBoxListParser"/> reads each machine's state.</item>
/// <item>Start runs headless, so the machine starts without a window; console opens <c>VirtualBoxVM --startvm</c>
/// detached, which attaches a window to a machine that is already running.</item>
/// <item><b>Restart is a hard reset.</b> <c>VBoxManage</c> has no graceful reboot, so the dialog's y/n says
/// "Restart" and the guest is reset without a shutdown. Shutdown is the ACPI power button, as in the other backends.</item>
/// <item>Names are passed as separate arguments; a name that starts with a dash is refused so it cannot be read as an option.</item>
/// </list>
/// The commands are written from the VBoxManage documentation and have not been run against a real VirtualBox install.
/// </summary>
public sealed class VirtualBoxVirtualMachineService(
    IExternalProcessRunner processRunner,
    Func<string, IReadOnlyList<string>, bool>? launchDetached = null) : IVirtualMachineBackend
{
    public const string VBoxManage = "VBoxManage";
    public const string VirtualBoxVm = "VirtualBoxVM";
    public const string Label = "VirtualBox";

    private readonly Func<string, IReadOnlyList<string>, bool> _launchDetached = launchDetached ?? LibvirtVirtualMachineService.LaunchDetached;

    public string Hypervisor => Label;

    public async Task<IReadOnlyList<VirtualMachine>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = await processRunner.RunAsync(VBoxManage, ["list", "--long", "vms"], cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(DescribeFailure(result));
        }

        return VirtualBoxListParser.Parse(result.StandardOutput)
            .Select(machine => machine with { Hypervisor = Label })
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
            return _launchDetached(VirtualBoxVm, ["--startvm", name])
                ? new VirtualMachineActionResult(true, $"Opening the console of {name}.")
                : new VirtualMachineActionResult(false, "VirtualBoxVM was not found. It comes with VirtualBox.");
        }

        var result = await processRunner.RunAsync(VBoxManage, Arguments(name, action), cancellationToken: cancellationToken);
        return result.Success
            ? new VirtualMachineActionResult(true, $"{name}: {Done(action)}.")
            : new VirtualMachineActionResult(false, $"{name}: {DescribeFailure(result)}");
    }

    /// <summary>The VBoxManage arguments for an action. Console is not a VBoxManage command.</summary>
    internal static IReadOnlyList<string> Arguments(string name, VirtualMachineAction action) => action switch
    {
        VirtualMachineAction.Start => ["startvm", name, "--type", "headless"],
        VirtualMachineAction.Shutdown => ["controlvm", name, "acpipowerbutton"],
        VirtualMachineAction.PowerOff => ["controlvm", name, "poweroff"],
        VirtualMachineAction.Restart => ["controlvm", name, "reset"],
        VirtualMachineAction.Pause => ["controlvm", name, "pause"],
        VirtualMachineAction.Resume => ["controlvm", name, "resume"],
        VirtualMachineAction.SaveState => ["controlvm", name, "savestate"],
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Console is not a VBoxManage command."),
    };

    internal static string Done(VirtualMachineAction action) => action switch
    {
        VirtualMachineAction.Start => "started",
        VirtualMachineAction.Shutdown => "shutdown requested",
        VirtualMachineAction.PowerOff => "powered off",
        VirtualMachineAction.Restart => "reset",
        VirtualMachineAction.Pause => "paused",
        VirtualMachineAction.Resume => "resumed",
        VirtualMachineAction.SaveState => "state saved and stopped",
        _ => "done",
    };

    /// <summary>Turns VBoxManage's output into a sentence a footer can hold.</summary>
    internal static string DescribeFailure(ProcessOutput result)
    {
        if (result.ExitCode == -1 && result.StandardError.Contains(LibvirtVirtualMachineService.TimeoutMarker, StringComparison.Ordinal))
        {
            return LibvirtVirtualMachineService.TimeoutSentence;
        }

        if (result.ExitCode == -1)
        {
            return "VBoxManage could not be started. Install VirtualBox, which provides VBoxManage.";
        }

        return result.FirstErrorLine;
    }
}
