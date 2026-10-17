namespace iTerminal.Core.Services.SystemSettings;

/// <summary>Whose environment a variable belongs to: the signed-in user, or every user of the machine.</summary>
public enum EnvironmentScope
{
    User,
    Machine,
}

/// <summary>One environment variable as stored — a Windows value keeps its unexpanded <c>%NAME%</c> references.</summary>
public sealed record SystemEnvironmentVariable(string Name, string Value);

/// <summary>
/// The machine's identity. <see cref="Membership"/> is the workgroup, or the domain when
/// <see cref="IsDomainMember"/>; it is empty where the system has no such notion.
/// </summary>
public sealed record ComputerIdentity(
    string ComputerName,
    string Membership,
    bool IsDomainMember,
    string Description,
    bool CanChangeMembership,
    bool CanChangeDescription);

/// <summary>A time zone the system offers, by the identifier the system's own tools take.</summary>
public sealed record TimeZoneChoice(string Id, string DisplayName);

/// <summary>What a change did. <see cref="RestartRequired"/> is for a change the system applies only at its next start.</summary>
public sealed record SystemSettingResult(bool Success, string Message, bool RestartRequired = false)
{
    public static SystemSettingResult Ok(string message, bool restartRequired = false) => new(true, message, restartRequired);

    public static SystemSettingResult Fail(string message) => new(false, message);
}

/// <summary>
/// The settings a system administrator reaches for first — computer name, workgroup, description,
/// environment variables and time zone — read and changed through the operating system's own
/// mechanisms (the registry, <c>Rename-Computer</c>, <c>hostnamectl</c>, <c>/etc/environment</c>,
/// <c>tzutil</c>, <c>timedatectl</c>). Nothing here touches iTerminal's own settings.
///
/// <para>A change that needs administrator rights and finds none is refused with a message that says
/// so, rather than attempted and failed half way: <see cref="IsElevated"/> is what the dialog shows.</para>
/// </summary>
public interface ISystemSettingsService
{
    /// <summary>Whether this process may change machine-wide settings: an Administrator token on Windows, root elsewhere.</summary>
    bool IsElevated { get; }

    /// <summary>Whether the environment of <paramref name="scope"/> can be read and changed on this system.</summary>
    bool SupportsEnvironment(EnvironmentScope scope);

    /// <summary>Whether the time zone can be changed on this system.</summary>
    bool SupportsTimeZone { get; }

    Task<ComputerIdentity> GetComputerAsync(CancellationToken cancellationToken = default);

    Task<SystemSettingResult> SetComputerNameAsync(string name, CancellationToken cancellationToken = default);

    Task<SystemSettingResult> SetMembershipAsync(string workgroup, CancellationToken cancellationToken = default);

    Task<SystemSettingResult> SetDescriptionAsync(string description, CancellationToken cancellationToken = default);

    /// <summary>The variables of <paramref name="scope"/>, sorted by name.</summary>
    Task<IReadOnlyList<SystemEnvironmentVariable>> GetEnvironmentAsync(EnvironmentScope scope, CancellationToken cancellationToken = default);

    Task<SystemSettingResult> SetEnvironmentVariableAsync(EnvironmentScope scope, string name, string value, CancellationToken cancellationToken = default);

    Task<SystemSettingResult> RemoveEnvironmentVariableAsync(EnvironmentScope scope, string name, CancellationToken cancellationToken = default);

    /// <summary>The identifier of the zone the system is in now.</summary>
    string CurrentTimeZoneId { get; }

    IReadOnlyList<TimeZoneChoice> GetTimeZones();

    Task<SystemSettingResult> SetTimeZoneAsync(string id, CancellationToken cancellationToken = default);
}
