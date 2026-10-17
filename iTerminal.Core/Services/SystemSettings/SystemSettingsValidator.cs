using System.Text.RegularExpressions;

namespace iTerminal.Core.Services.SystemSettings;

/// <summary>
/// The rules the operating systems enforce on the values this tool writes, checked before anything
/// is run so a refusal is a sentence and not the tail of a PowerShell stack trace. Each method returns
/// null when the value is acceptable, otherwise the reason it is not.
/// </summary>
public static partial class SystemSettingsValidator
{
    /// <summary>A NetBIOS name (computer, workgroup) is 15 characters at most.</summary>
    public const int MaxNetBiosLength = 15;

    /// <summary>A DNS label — what a Linux host name is made of — is 63 characters at most.</summary>
    public const int MaxHostNameLength = 63;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]*$")]
    private static partial Regex HostLabel();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex PortableVariableName();

    public static string? ValidateComputerName(string? name, bool windows)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "The computer name cannot be empty.";
        }

        var limit = windows ? MaxNetBiosLength : MaxHostNameLength;
        if (name.Length > limit)
        {
            return $"The computer name can be at most {limit} characters.";
        }

        if (!HostLabel().IsMatch(name) || name.EndsWith('-'))
        {
            return "Use only letters, digits and hyphens, starting with a letter or digit and not ending with a hyphen.";
        }

        return windows && name.All(char.IsAsciiDigit)
            ? "A computer name cannot be made of digits only."
            : null;
    }

    public static string? ValidateWorkgroup(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "The workgroup name cannot be empty.";
        }

        if (name.Length > MaxNetBiosLength)
        {
            return $"The workgroup name can be at most {MaxNetBiosLength} characters.";
        }

        return name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|', ';', ',']) >= 0 || name.Any(char.IsControl)
            ? "The workgroup name cannot contain \\ / : * ? \" < > | ; ,"
            : null;
    }

    public static string? ValidateDescription(string? description) =>
        description is not null && description.Any(char.IsControl)
            ? "The description must be a single line."
            : description is { Length: > 256 }
                ? "The description can be at most 256 characters."
                : null;

    /// <summary>
    /// A variable name. Windows takes nearly any text without <c>=</c>; the files Linux reads at login
    /// take shell-style names only, and a name outside that is silently ignored there.
    /// </summary>
    public static string? ValidateVariableName(string? name, bool windows)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "The variable name cannot be empty.";
        }

        if (name != name.Trim())
        {
            return "The variable name cannot start or end with a space.";
        }

        if (name.Contains('=') || name.Any(char.IsControl))
        {
            return "The variable name cannot contain = or control characters.";
        }

        if (windows)
        {
            return name.Length > 255 ? "The variable name can be at most 255 characters." : null;
        }

        return PortableVariableName().IsMatch(name)
            ? null
            : "Use letters, digits and underscores, not starting with a digit.";
    }

    public static string? ValidateVariableValue(string? value, bool windows)
    {
        if (value is null)
        {
            return "The variable needs a value.";
        }

        if (value.Any(c => c is '\0' or '\r' or '\n'))
        {
            return "The value must be a single line.";
        }

        return windows && value.Length > 32766 ? "The value is too long for the registry." : null;
    }
}
