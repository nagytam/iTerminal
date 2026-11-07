namespace iTerminal.Core.Services.SystemSettings;

/// <summary>
/// The environment of this running process: what iTerminal and every program it starts (the command line,
/// Ctrl+O shells, external tools) inherit. A change lasts until iTerminal closes and reaches nothing else.
/// </summary>
public interface ISessionEnvironment
{
    /// <summary>The variables of this process, sorted by name.</summary>
    IReadOnlyList<SystemEnvironmentVariable> Read();

    SystemSettingResult Set(string name, string value);

    SystemSettingResult Remove(string name);
}

/// <summary>
/// <see cref="ISessionEnvironment"/> over <see cref="Environment"/>. Only the two-argument overloads are used:
/// the ones that take an <see cref="EnvironmentVariableTarget"/> write the Windows registry for User and
/// Machine (as plain text, which breaks an expandable <c>Path</c>) and are ignored on Unix.
/// </summary>
public sealed class SessionEnvironment : ISessionEnvironment
{
    private const string Reach = "Only iTerminal and the programs it starts see it, until it closes.";

    public IReadOnlyList<SystemEnvironmentVariable> Read() =>
    [
        .. Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Select(e => new SystemEnvironmentVariable((string)e.Key, e.Value as string ?? string.Empty))
            .Where(v => v.Name.Length > 0 && v.Name[0] != '=') // Windows keeps =C: and =ExitCode here; no shell shows them.
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase),
    ];

    public SystemSettingResult Set(string name, string value)
    {
        var problem = ValidateName(name) ?? SystemSettingsValidator.ValidateVariableValue(value, OperatingSystem.IsWindows());
        if (problem is not null)
        {
            return SystemSettingResult.Fail(problem);
        }

        Environment.SetEnvironmentVariable(name, value);
        return SystemSettingResult.Ok($"{name} was set. {Reach}");
    }

    /// <summary>
    /// A process may hold any name that is not empty and has no '=' or control character, whatever the shell
    /// allows (a Unix process can carry <c>A.B</c>), so the lenient rule is used here, not the portable one that
    /// the saved stores need.
    /// </summary>
    public static string? ValidateName(string? name) => SystemSettingsValidator.ValidateVariableName(name, windows: true);

    public SystemSettingResult Remove(string name)
    {
        Environment.SetEnvironmentVariable(name, null);
        return SystemSettingResult.Ok($"{name} was removed. {Reach}");
    }
}
