using iTerminal.Core.Model.PackageManager;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Services.PackageManager;

/// <summary>
/// <see cref="IPackageManagerService"/> backed by APT (Debian/Ubuntu Linux). Installed packages
/// come from <c>dpkg-query</c> (fast, no network) cross-referenced with <c>apt list --upgradable</c>
/// for available updates; search uses <c>apt-cache search</c>; install/uninstall shell out to
/// <c>apt-get</c> and stream output line-by-line.
/// </summary>
public sealed partial class AptPackageManagerService : IPackageManagerService
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(60);

    // apt-get must not fall back to an interactive debconf prompt — there is no TTY here, so a
    // prompt would hang until the timeout. Fail fast instead and rely on defaults.
    private static readonly Dictionary<string, string> NonInteractiveEnvironment = new() { ["DEBIAN_FRONTEND"] = "noninteractive" };

    // dpkg-query line: "<status-abbrev>|<package>|<version>", e.g. "ii |bash|5.1-6ubuntu1.1".
    // Only "ii" (installed, no pending flags) counts as actually installed.
    private const string DpkgQueryFormat = "${db:Status-Abbrev}|${Package}|${Version}\\n";

    // "bash/jammy-updates 5.1-6ubuntu1.1 amd64 [upgradable from: 5.1-6ubuntu1]"
    [GeneratedRegex(@"^(?<name>[^/\s]+)/\S+\s+(?<version>\S+)\s+\S+\s+\[upgradable from:")]
    private static partial Regex UpgradableLineRegex();

    public string DisplayName => "APT";

    public async Task<PackageQueryResult> GetInstalledPackagesAsync(CancellationToken cancellationToken = default)
    {
        var result = await PackageManagerProcess.RunCaptureAsync("dpkg-query", QueryTimeout, cancellationToken,
            "-W", $"--showformat={DpkgQueryFormat}");
        if (!result.Success)
        {
            return PackageQueryResult.Fail(PackageManagerProcess.FirstNonEmpty(result.StandardError, result.StandardOutput, "dpkg-query failed."));
        }

        var upgradable = await GetUpgradableVersionsAsync(cancellationToken);

        var packages = new List<PackageInfo>();
        foreach (var line in result.StandardOutput.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('|');
            if (fields.Length < 3 || !fields[0].Trim().Equals("ii", StringComparison.Ordinal))
            {
                continue;
            }

            var name = fields[1];
            upgradable.TryGetValue(name, out var availableVersion);
            packages.Add(new PackageInfo(name, name, fields[2], availableVersion, "apt"));
        }

        return PackageQueryResult.Ok(packages);
    }

    /// <summary>Maps package name to the newer version reported by <c>apt list --upgradable</c>. Best-effort: an empty map is returned on any failure rather than failing the whole installed-list query.</summary>
    private static async Task<Dictionary<string, string>> GetUpgradableVersionsAsync(CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var result = await PackageManagerProcess.RunCaptureAsync("apt", QueryTimeout, cancellationToken, "list", "--upgradable");
            if (!result.Success)
            {
                return map;
            }

            foreach (var line in result.StandardOutput.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var match = UpgradableLineRegex().Match(line);
                if (match.Success)
                {
                    map[match.Groups["name"].Value] = match.Groups["version"].Value;
                }
            }
        }
        catch
        {
            // Ignore: upgrade info is a nice-to-have, not required to list installed packages.
        }

        return map;
    }

    public async Task<PackageQueryResult> SearchPackagesAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return PackageQueryResult.Ok([]);
        }

        var result = await PackageManagerProcess.RunCaptureAsync("apt-cache", QueryTimeout, cancellationToken, "search", query);
        if (!result.Success)
        {
            return PackageQueryResult.Fail(PackageManagerProcess.FirstNonEmpty(result.StandardError, result.StandardOutput, "apt-cache search failed."));
        }

        var packages = new List<PackageInfo>();
        foreach (var line in result.StandardOutput.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(" - ", StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var description = line[(separator + 3)..].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            packages.Add(new PackageInfo(description.Length == 0 ? name : description, name, string.Empty, null, "apt"));
        }

        return PackageQueryResult.Ok(packages);
    }

    public Task<int> InstallAsync(string packageId, Action<string> onOutputLine, CancellationToken cancellationToken = default) =>
        PackageManagerProcess.RunStreamingAsync(
            "apt-get", ["install", "-y", packageId], onOutputLine, cancellationToken, NonInteractiveEnvironment);

    public Task<int> UninstallAsync(string packageId, Action<string> onOutputLine, CancellationToken cancellationToken = default) =>
        PackageManagerProcess.RunStreamingAsync(
            "apt-get", ["remove", "-y", packageId], onOutputLine, cancellationToken, NonInteractiveEnvironment);

}
