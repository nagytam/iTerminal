using iTerminal.Core.Services.Processes;
using iTerminal.Core.Services.Signing;

namespace iTerminal.Core.Services.VirtualMachines;

/// <summary>
/// The Windows backend: Hyper-V, through the cmdlets of Windows PowerShell's Hyper-V module, run through
/// <see cref="IExternalProcessRunner"/> so they can be faked. Like the libvirt backend it goes through a CLI
/// rather than a binding, so nothing native is packaged. The user needs to be an administrator or a member
/// of the <c>Hyper-V Administrators</c> group; this service does not elevate.
///
/// <list type="bullet">
/// <item><b>Nothing typed becomes code.</b> A machine name is embedded only as a single-quoted literal
/// (<see cref="SignatureService.Literal"/>), and a name that starts with <c>-</c> or holds a control
/// character is refused. The machine is found with <c>Where-Object { $_.Name -eq … }</c> and must be exactly one:
/// <c>Get-VM -Name</c> would read <c>*</c> and <c>?</c> in a name as wildcards and act on several machines.</item>
/// <item><b>Windows PowerShell 5.1, deliberately.</b> The Hyper-V module is native there; <c>pwsh</c> would load
/// it through a compatibility layer.</item>
/// <item><b>Failures are told by markers, not by wording.</b> PowerShell's messages are localised, so the script
/// writes a fixed word to the error stream for "Hyper-V is not installed", "not allowed" and "no such machine",
/// and <see cref="Describe"/> turns each into a sentence.</item>
/// <item>The console opens <c>vmconnect.exe</c> on this computer, detached.</item>
/// </list>
/// The commands are written from the cmdlets' documentation and have not been run against a real host.
/// </summary>
public sealed class HyperVVirtualMachineService(
    IExternalProcessRunner processRunner,
    Func<string, IReadOnlyList<string>, bool>? launchDetached = null) : IVirtualMachineService
{
    public const string PowerShell = "powershell";
    public const string VmConnect = "vmconnect";

    internal const string MissingMarker = "HYPERV_MISSING";
    internal const string DeniedMarker = "HYPERV_DENIED";
    internal const string NoMachineMarker = "HYPERV_NOVM";

    private readonly Func<string, IReadOnlyList<string>, bool> _launchDetached = launchDetached ?? LibvirtVirtualMachineService.LaunchDetached;

    public async Task<IReadOnlyList<VirtualMachine>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunScriptAsync(
            "ConvertTo-Json -InputObject @(Get-VM | Select-Object Name, @{n='State';e={[string]$_.State}}) -Compress",
            cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(Describe(result));
        }

        return HyperVListParser.Parse(result.StandardOutput);
    }

    public async Task<VirtualMachineActionResult> RunAsync(string name, VirtualMachineAction action, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('-') || name.Any(char.IsControl))
        {
            return new VirtualMachineActionResult(false, $"Not a virtual machine name: {name}");
        }

        if (action == VirtualMachineAction.Console)
        {
            return _launchDetached(VmConnect, ["localhost", name])
                ? new VirtualMachineActionResult(true, $"Opening the console of {name}.")
                : new VirtualMachineActionResult(false, "vmconnect.exe was not found. It comes with the Hyper-V management tools (Windows Features > Hyper-V > Hyper-V Management Tools).");
        }

        var script = $"$vm = @(Get-VM | Where-Object {{ $_.Name -eq {SignatureService.Literal(name)} }}); " +
            $"if ($vm.Count -ne 1) {{ throw '{NoMachineMarker}' }}; $vm[0] | {Cmdlet(action)}";
        var result = await RunScriptAsync(script, cancellationToken);
        return result.Success
            ? new VirtualMachineActionResult(true, $"{name}: {LibvirtVirtualMachineService.Done(action)}.")
            : new VirtualMachineActionResult(false, $"{name}: {Describe(result)}");
    }

    /// <summary>
    /// The cmdlet, with the prompts switched off: the dialog has already asked. Shutdown is the graceful one
    /// (it needs the guest's integration services); power-off turns the machine off at once.
    /// </summary>
    internal static string Cmdlet(VirtualMachineAction action) => action switch
    {
        VirtualMachineAction.Start => "Start-VM -Confirm:$false",
        VirtualMachineAction.Shutdown => "Stop-VM -Confirm:$false",
        VirtualMachineAction.PowerOff => "Stop-VM -TurnOff -Confirm:$false",
        VirtualMachineAction.Restart => "Restart-VM -Force -Confirm:$false",
        VirtualMachineAction.Pause => "Suspend-VM -Confirm:$false",
        VirtualMachineAction.Resume => "Resume-VM -Confirm:$false",
        VirtualMachineAction.SaveState => "Save-VM -Confirm:$false",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Console is not a cmdlet."),
    };

    /// <summary>
    /// Wraps <paramref name="body"/> so that every failure ends as one marker or one message line on the error stream.
    /// The output is switched to UTF-8 first: the runner reads UTF-8, but PowerShell writes the console's OEM code
    /// page, which would garble a machine name with accents and make it impossible to act on.
    /// </summary>
    internal static string Wrap(string body) =>
        "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); $ErrorActionPreference = 'Stop'; try { " +
        $"if (-not (Get-Command Get-VM -ErrorAction SilentlyContinue)) {{ throw '{MissingMarker}' }}; {body} " +
        "} catch { if ($_.CategoryInfo.Category -eq 'PermissionDenied') { " +
        $"[Console]::Error.WriteLine('{DeniedMarker}') }} else {{ [Console]::Error.WriteLine($_.Exception.Message) }}; exit 1 }}";

    private Task<ProcessOutput> RunScriptAsync(string body, CancellationToken cancellationToken) =>
        processRunner.RunAsync(PowerShell, ["-NoProfile", "-NonInteractive", "-Command", Wrap(body)], cancellationToken: cancellationToken);

    /// <summary>Turns the tool's output into a sentence a footer can hold.</summary>
    internal static string Describe(ProcessOutput result)
    {
        if (result.ExitCode == -1 && result.StandardError.Contains(LibvirtVirtualMachineService.TimeoutMarker, StringComparison.Ordinal))
        {
            return LibvirtVirtualMachineService.TimeoutSentence;
        }

        if (result.StandardError.Contains(MissingMarker, StringComparison.Ordinal))
        {
            return "Hyper-V is not available on this computer. Turn on the Hyper-V feature in Windows Features, then restart Windows.";
        }

        if (result.StandardError.Contains(DeniedMarker, StringComparison.Ordinal))
        {
            return "Hyper-V refused the request. Run iTerminal as an administrator, or add your user to the Hyper-V Administrators group and sign in again.";
        }

        if (result.StandardError.Contains(NoMachineMarker, StringComparison.Ordinal))
        {
            return "there is no such virtual machine (any more). Press F5 to read the list again.";
        }

        if (result.ExitCode == -1)
        {
            return $"PowerShell could not be started ({result.FirstErrorLine})";
        }

        return result.FirstErrorLine;
    }
}
