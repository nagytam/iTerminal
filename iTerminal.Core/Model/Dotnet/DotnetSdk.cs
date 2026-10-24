namespace iTerminal.Core.Model.Dotnet;

/// <summary>
/// One installed .NET SDK, as <c>dotnet --list-sdks</c> reports it: its version and the root of the install it belongs to.
/// The <c>dotnet</c> host of that install runs builds and tests with this SDK.
/// </summary>
public sealed record DotnetSdk(string Version, string Root)
{
    /// <summary>True for a preview or release candidate (its version carries a label after the dash).</summary>
    public bool IsPrerelease => Version.Contains('-');

    /// <summary>The <c>dotnet</c> host of this install, which runs commands with this SDK.</summary>
    public string HostPath => Path.Combine(Root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

    /// <summary>The version without its label: <c>10.0.200</c> for <c>10.0.200-preview.1</c>, for ordering.</summary>
    private Version BaseVersion =>
        System.Version.TryParse(Version.Split('-')[0], out Version? parsed) ? parsed : new Version(0, 0);

    /// <summary>
    /// The SDK a new build uses by default: the newest stable one, or the newest of any kind when only previews are
    /// installed. Null when there are none.
    /// </summary>
    public static DotnetSdk? Default(IEnumerable<DotnetSdk> sdks)
    {
        List<DotnetSdk> all = [.. sdks];
        return all.Where(sdk => !sdk.IsPrerelease).OrderByDescending(sdk => sdk.BaseVersion).ThenByDescending(sdk => sdk.Version, StringComparer.Ordinal).FirstOrDefault()
            ?? all.OrderByDescending(sdk => sdk.BaseVersion).ThenByDescending(sdk => sdk.Version, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>
    /// Reads the output of <c>dotnet --list-sdks</c>, one <c>10.0.113 [/usr/lib/dotnet/sdk]</c> line per SDK, newest first.
    /// Lines that do not match are skipped.
    /// </summary>
    public static IReadOnlyList<DotnetSdk> ParseListing(IEnumerable<string> lines)
    {
        List<DotnetSdk> sdks = [];
        foreach (string line in lines)
        {
            int open = line.LastIndexOf(" [", StringComparison.Ordinal);
            if (open <= 0 || !line.EndsWith(']'))
            {
                continue;
            }

            string version = line[..open].Trim();
            string sdkDirectory = line[(open + 2)..^1];
            // The directory is <root>/sdk; the install is the root above it.
            string? root = Path.GetDirectoryName(sdkDirectory.TrimEnd('/', '\\'));
            if (version.Length == 0 || string.IsNullOrEmpty(root))
            {
                continue;
            }

            sdks.Add(new DotnetSdk(version, root));
        }

        return [.. sdks.OrderByDescending(sdk => sdk.BaseVersion).ThenByDescending(sdk => sdk.Version, StringComparer.Ordinal)];
    }
}
