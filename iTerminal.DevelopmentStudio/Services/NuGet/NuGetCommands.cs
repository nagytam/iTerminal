using System.Text.RegularExpressions;

namespace iTerminal.DevelopmentStudio.Services.NuGet;

/// <summary>
/// The <c>dotnet</c> arguments of the package commands, and the checks that keep a typed package id or a version from being read
/// as an option. Arguments are passed as a list, never through a shell, so these checks guard the CLI's option parsing, not an
/// injection.
/// </summary>
public static class NuGetCommands
{
    // NuGet ids start with a letter or digit and hold letters, digits, dots, dashes and underscores.
    private static readonly Regex PackageIdPattern = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // A version starts with a digit, so it cannot be read as an option, and holds no spaces.
    private static readonly Regex VersionPattern = new(@"^\d[0-9A-Za-z.+-]{0,63}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Whether <paramref name="text"/> is a package id NuGet accepts.</summary>
    public static bool IsPackageId(string? text) => text is not null && PackageIdPattern.IsMatch(text);

    /// <summary>Whether <paramref name="text"/> is a version this app passes on the command line.</summary>
    public static bool IsVersionText(string? text) => text is not null && VersionPattern.IsMatch(text);

    /// <summary><c>dotnet list &lt;target&gt; package [--outdated] --format json</c>.</summary>
    public static IReadOnlyList<string> ListArguments(string target, bool outdated) =>
        outdated
            ? ["list", target, "package", "--outdated", "--format", "json"]
            : ["list", target, "package", "--format", "json"];

    /// <summary><c>dotnet add &lt;project&gt; package &lt;id&gt; [--version &lt;version&gt;]</c>. Without a version the newest is added.</summary>
    public static IReadOnlyList<string> AddArguments(string project, string packageId, string? version) =>
        version is null
            ? ["add", project, "package", packageId]
            : ["add", project, "package", packageId, "--version", version];

    /// <summary><c>dotnet remove &lt;project&gt; package &lt;id&gt;</c>.</summary>
    public static IReadOnlyList<string> RemoveArguments(string project, string packageId) =>
        ["remove", project, "package", packageId];
}
