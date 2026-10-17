using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.SystemSettings;

/// <summary>
/// Windows through the registry and PowerShell (<c>Get-CimInstance</c>, <c>Rename-Computer</c>,
/// <c>Add-Computer -WorkgroupName</c>, <c>tzutil</c>); Linux through <c>hostnamectl</c>,
/// <c>timedatectl</c>, <c>/etc/environment</c> and the user's <c>environment.d</c>. Every external tool
/// goes through <see cref="IExternalProcessRunner"/>, so the service is tested against recorded output.
///
/// <para>Joining a <em>domain</em> is not offered: it takes a directory account's credentials, and
/// credentials come from the vault, never from a dialog that is not a vault. Leaving one for a workgroup
/// is the same request. The domain is shown, read-only.</para>
/// </summary>
public sealed class SystemSettingsService : ISystemSettingsService
{
    private readonly IExternalProcessRunner _runner;
    private readonly Func<bool> _isElevated;
    private readonly bool _windows;
    private readonly bool _linux;
    private readonly IEnvironmentVariableStore? _userStore;
    private readonly IEnvironmentVariableStore? _machineStore;

    public SystemSettingsService(IExternalProcessRunner runner, Elevation.IElevationService elevationService)
        : this(runner, () => elevationService.IsElevated, OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), null, null)
    {
    }

    /// <summary>For a test: the platform and the places the variables live are chosen, not discovered.</summary>
    internal SystemSettingsService(
        IExternalProcessRunner runner,
        Func<bool> isElevated,
        bool windows,
        bool linux,
        IEnvironmentVariableStore? userStore,
        IEnvironmentVariableStore? machineStore)
    {
        _runner = runner;
        _isElevated = isElevated;
        _windows = windows;
        _linux = linux;
        _userStore = userStore ?? DefaultStore(EnvironmentScope.User, windows, linux);
        _machineStore = machineStore ?? DefaultStore(EnvironmentScope.Machine, windows, linux);
    }

    private static IEnvironmentVariableStore? DefaultStore(EnvironmentScope scope, bool windows, bool linux)
    {
        if (windows && OperatingSystem.IsWindows())
        {
            return new RegistryEnvironmentStore(scope);
        }

        if (!linux)
        {
            return null;
        }

        if (scope == EnvironmentScope.Machine)
        {
            return new EnvironmentFileStore("/etc/environment");
        }

        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(config))
        {
            config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        // systemd reads environment.d for the user's session; the 60- keeps this after the distribution's own files.
        return new EnvironmentFileStore(Path.Combine(config, "environment.d", "60-iterminal.conf"));
    }

    private const string NeedsAdministrator =
        "This changes the whole machine, so it needs administrator rights. Restart iTerminal as administrator (Ctrl+Shift+A) and try again.";

    public bool IsElevated => _isElevated();

    public bool SupportsEnvironment(EnvironmentScope scope) => (scope == EnvironmentScope.User ? _userStore : _machineStore) is not null;

    public bool SupportsTimeZone => _windows || _linux;

    // ── Computer ────────────────────────────────────────────────────────────

    public async Task<ComputerIdentity> GetComputerAsync(CancellationToken cancellationToken = default)
    {
        if (_windows)
        {
            const string script =
                "$c = Get-CimInstance Win32_ComputerSystem; $o = Get-CimInstance Win32_OperatingSystem; " +
                "Write-Output $c.Name; Write-Output $c.PartOfDomain; Write-Output $c.Domain; Write-Output $o.Description";
            var output = await RunPowerShellAsync(script, cancellationToken).ConfigureAwait(false);
            if (output.Success)
            {
                var lines = output.StandardOutput.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
                if (lines.Length >= 3)
                {
                    var domain = string.Equals(lines[1].Trim(), "True", StringComparison.OrdinalIgnoreCase);
                    return new ComputerIdentity(
                        lines[0].Trim(),
                        lines[2].Trim(),
                        domain,
                        lines.Length > 3 ? lines[3].Trim() : string.Empty,
                        CanChangeMembership: !domain,
                        CanChangeDescription: true);
                }
            }

            return new ComputerIdentity(Environment.MachineName, string.Empty, false, string.Empty, false, false);
        }

        var name = Environment.MachineName;
        var pretty = string.Empty;
        var domainName = string.Empty;
        if (_linux)
        {
            var hostname = await _runner.RunAsync("hostnamectl", ["--static"], null, cancellationToken).ConfigureAwait(false);
            if (hostname.Success && hostname.StandardOutput.Trim().Length > 0)
            {
                name = hostname.StandardOutput.Trim();
            }

            var prettyName = await _runner.RunAsync("hostnamectl", ["--pretty"], null, cancellationToken).ConfigureAwait(false);
            if (prettyName.Success)
            {
                pretty = prettyName.StandardOutput.Trim();
            }

            var dns = await _runner.RunAsync("dnsdomainname", [], null, cancellationToken).ConfigureAwait(false);
            if (dns.Success)
            {
                domainName = dns.StandardOutput.Trim();
            }
        }

        return new ComputerIdentity(name, domainName, domainName.Length > 0, pretty, CanChangeMembership: false, CanChangeDescription: _linux);
    }

    public async Task<SystemSettingResult> SetComputerNameAsync(string name, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        if (SystemSettingsValidator.ValidateComputerName(name, _windows) is { } problem)
        {
            return SystemSettingResult.Fail(problem);
        }

        if (!_windows && !_linux)
        {
            return SystemSettingResult.Fail("Changing the computer name is not supported on this system.");
        }

        if (!IsElevated)
        {
            return SystemSettingResult.Fail(NeedsAdministrator);
        }

        if (_windows)
        {
            var output = await RunPowerShellAsync($"Rename-Computer -NewName '{Quote(name)}' -Force -ErrorAction Stop", cancellationToken).ConfigureAwait(false);
            return output.Success
                ? SystemSettingResult.Ok($"The computer will be called {name} after the next restart.", restartRequired: true)
                : SystemSettingResult.Fail(output.FirstErrorLine);
        }

        var result = await _runner.RunAsync("hostnamectl", ["set-hostname", name], null, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? SystemSettingResult.Ok($"The computer is now called {name}.")
            : SystemSettingResult.Fail(result.FirstErrorLine);
    }

    public async Task<SystemSettingResult> SetMembershipAsync(string workgroup, CancellationToken cancellationToken = default)
    {
        workgroup = workgroup.Trim();
        if (!_windows)
        {
            return SystemSettingResult.Fail("A workgroup is a Windows setting.");
        }

        if (SystemSettingsValidator.ValidateWorkgroup(workgroup) is { } problem)
        {
            return SystemSettingResult.Fail(problem);
        }

        if (!IsElevated)
        {
            return SystemSettingResult.Fail(NeedsAdministrator);
        }

        var identity = await GetComputerAsync(cancellationToken).ConfigureAwait(false);
        if (identity.IsDomainMember)
        {
            return SystemSettingResult.Fail($"This computer is a member of the domain {identity.Membership}. Leaving a domain needs a directory account; use Windows' own System Properties.");
        }

        var output = await RunPowerShellAsync($"Add-Computer -WorkgroupName '{Quote(workgroup)}' -Force -ErrorAction Stop", cancellationToken).ConfigureAwait(false);
        return output.Success
            ? SystemSettingResult.Ok($"The workgroup will be {workgroup} after the next restart.", restartRequired: true)
            : SystemSettingResult.Fail(output.FirstErrorLine);
    }

    public async Task<SystemSettingResult> SetDescriptionAsync(string description, CancellationToken cancellationToken = default)
    {
        description = description.Trim();
        if (SystemSettingsValidator.ValidateDescription(description) is { } problem)
        {
            return SystemSettingResult.Fail(problem);
        }

        if (!_windows && !_linux)
        {
            return SystemSettingResult.Fail("Changing the computer description is not supported on this system.");
        }

        if (!IsElevated)
        {
            return SystemSettingResult.Fail(NeedsAdministrator);
        }

        if (_windows)
        {
            var output = await RunPowerShellAsync(
                $"Get-CimInstance Win32_OperatingSystem | Set-CimInstance -Property @{{Description='{Quote(description)}'}} -ErrorAction Stop",
                cancellationToken).ConfigureAwait(false);
            return output.Success
                ? SystemSettingResult.Ok("The description was saved.")
                : SystemSettingResult.Fail(output.FirstErrorLine);
        }

        var result = await _runner.RunAsync("hostnamectl", ["set-hostname", "--pretty", description], null, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? SystemSettingResult.Ok("The description was saved.")
            : SystemSettingResult.Fail(result.FirstErrorLine);
    }

    // ── Environment ─────────────────────────────────────────────────────────

    private IEnvironmentVariableStore? StoreOf(EnvironmentScope scope) => scope == EnvironmentScope.User ? _userStore : _machineStore;

    public Task<IReadOnlyList<SystemEnvironmentVariable>> GetEnvironmentAsync(EnvironmentScope scope, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            try
            {
                return StoreOf(scope)?.Read() ?? [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return [];
            }
        }, cancellationToken);

    public Task<SystemSettingResult> SetEnvironmentVariableAsync(EnvironmentScope scope, string name, string value, CancellationToken cancellationToken = default)
    {
        if (SystemSettingsValidator.ValidateVariableName(name, _windows) is { } nameProblem)
        {
            return Task.FromResult(SystemSettingResult.Fail(nameProblem));
        }

        if (SystemSettingsValidator.ValidateVariableValue(value, _windows) is { } valueProblem)
        {
            return Task.FromResult(SystemSettingResult.Fail(valueProblem));
        }

        return ChangeEnvironmentAsync(scope, store => store.Set(name, value), $"{name} was saved.", cancellationToken);
    }

    public Task<SystemSettingResult> RemoveEnvironmentVariableAsync(EnvironmentScope scope, string name, CancellationToken cancellationToken = default) =>
        ChangeEnvironmentAsync(scope, store => store.Remove(name), $"{name} was removed.", cancellationToken);

    private Task<SystemSettingResult> ChangeEnvironmentAsync(EnvironmentScope scope, Action<IEnvironmentVariableStore> change, string done, CancellationToken cancellationToken)
    {
        var store = StoreOf(scope);
        if (store is null)
        {
            return Task.FromResult(SystemSettingResult.Fail("Environment variables cannot be changed on this system."));
        }

        if (scope == EnvironmentScope.Machine && !IsElevated)
        {
            return Task.FromResult(SystemSettingResult.Fail(NeedsAdministrator));
        }

        return Task.Run(() =>
        {
            try
            {
                change(store);
                return SystemSettingResult.Ok(done + " Programs started from now on see it; running ones keep what they had.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return SystemSettingResult.Fail(exception.Message);
            }
        }, cancellationToken);
    }

    // ── Time zone ───────────────────────────────────────────────────────────

    public string CurrentTimeZoneId => TimeZoneInfo.Local.Id;

    public IReadOnlyList<TimeZoneChoice> GetTimeZones() =>
        [.. TimeZoneInfo.GetSystemTimeZones().Select(zone => new TimeZoneChoice(zone.Id, zone.DisplayName))];

    public async Task<SystemSettingResult> SetTimeZoneAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!SupportsTimeZone)
        {
            return SystemSettingResult.Fail("Changing the time zone is not supported on this system.");
        }

        if (string.IsNullOrWhiteSpace(id) || id.Any(char.IsControl) || id.StartsWith('-'))
        {
            return SystemSettingResult.Fail("That is not a time zone.");
        }

        if (!IsElevated)
        {
            return SystemSettingResult.Fail(NeedsAdministrator);
        }

        var output = _windows
            ? await _runner.RunAsync("tzutil", ["/s", id], null, cancellationToken).ConfigureAwait(false)
            : await _runner.RunAsync("timedatectl", ["set-timezone", id], null, cancellationToken).ConfigureAwait(false);
        if (!output.Success)
        {
            return SystemSettingResult.Fail(output.FirstErrorLine);
        }

        TimeZoneInfo.ClearCachedData();
        return SystemSettingResult.Ok($"The time zone is now {id}.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private Task<ProcessOutput> RunPowerShellAsync(string script, CancellationToken cancellationToken) =>
        _runner.RunAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script], null, cancellationToken);

    /// <summary>The text for inside a PowerShell single-quoted string: the only character that ends one is the quote itself.</summary>
    internal static string Quote(string text) => text.Replace("'", "''", StringComparison.Ordinal);
}
