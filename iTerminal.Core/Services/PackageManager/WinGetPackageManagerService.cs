using iTerminal.Core.Model.PackageManager;

namespace iTerminal.Core.Services.PackageManager;

/// <summary>
/// <see cref="IPackageManagerService"/> backed by the winget CLI (Windows). Queries
/// (<c>list</c>/<c>search</c>) read the whole output and parse winget's fixed-width table;
/// install/uninstall stream output line-by-line and kill the whole process tree on cancellation so
/// a long download can be stopped from the UI.
/// </summary>
public sealed class WinGetPackageManagerService : IPackageManagerService
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(60);

    public string DisplayName => "WinGet";

    public async Task<PackageQueryResult> GetInstalledPackagesAsync(CancellationToken cancellationToken = default)
    {
        var result = await PackageManagerProcess.RunCaptureAsync("winget", QueryTimeout, cancellationToken,
            "list", "--accept-source-agreements", "--disable-interactivity");
        return result.Success
            ? PackageQueryResult.Ok(ParseTable(result.StandardOutput))
            : PackageQueryResult.Fail(PackageManagerProcess.FirstNonEmpty(result.StandardError, result.StandardOutput, "winget list failed."));
    }

    public async Task<PackageQueryResult> SearchPackagesAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return PackageQueryResult.Ok([]);
        }

        var result = await PackageManagerProcess.RunCaptureAsync("winget", QueryTimeout, cancellationToken,
            "search", query, "--accept-source-agreements", "--disable-interactivity");
        return result.Success
            ? PackageQueryResult.Ok(ParseTable(result.StandardOutput))
            : PackageQueryResult.Fail(PackageManagerProcess.FirstNonEmpty(result.StandardError, result.StandardOutput, "winget search failed."));
    }

    public Task<int> InstallAsync(string packageId, Action<string> onOutputLine, CancellationToken cancellationToken = default) =>
        PackageManagerProcess.RunStreamingAsync(
            "winget",
            ["install", "--id", packageId, "-e", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"],
            onOutputLine, cancellationToken);

    public Task<int> UninstallAsync(string packageId, Action<string> onOutputLine, CancellationToken cancellationToken = default) =>
        PackageManagerProcess.RunStreamingAsync(
            "winget",
            ["uninstall", "--id", packageId, "-e", "--disable-interactivity"],
            onOutputLine, cancellationToken);

    /// <summary>
    /// Parses a winget table (shared by <c>list</c> and <c>search</c>): a header row naming its
    /// columns ("Name", "Id", "Version", optionally "Available", "Source"), immediately followed by
    /// a row of dashes, then one data row per package. Column boundaries are taken from where each
    /// header word starts, since winget pads columns with spaces rather than emitting a delimiter.
    /// </summary>
    private static List<PackageInfo> ParseTable(string output)
    {
        var lines = output.Replace("\r", string.Empty).Split('\n');

        var headerIndex = -1;
        for (var i = 0; i < lines.Length - 1; i++)
        {
            var next = lines[i + 1];
            if (next.Length > 0 && next.All(c => c == '-'))
            {
                headerIndex = i;
                break;
            }
        }

        if (headerIndex < 0)
        {
            return [];
        }

        var header = lines[headerIndex];
        var columns = new[] { "Name", "Id", "Version", "Available", "Source" }
            .Select(name => (Name: name, Start: header.IndexOf(name, StringComparison.Ordinal)))
            .Where(column => column.Start >= 0)
            .OrderBy(column => column.Start)
            .ToArray();
        if (columns.Length == 0)
        {
            return [];
        }

        string ColumnValue(string line, int columnIndex)
        {
            var start = columns[columnIndex].Start;
            if (start >= line.Length)
            {
                return string.Empty;
            }

            var end = columnIndex + 1 < columns.Length ? Math.Min(columns[columnIndex + 1].Start, line.Length) : line.Length;
            return end > start ? line[start..end].Trim() : string.Empty;
        }

        string Get(string line, string name)
        {
            var index = Array.FindIndex(columns, column => column.Name == name);
            return index >= 0 ? ColumnValue(line, index) : string.Empty;
        }

        var packages = new List<PackageInfo>();
        for (var i = headerIndex + 2; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.Length <= columns[0].Start)
            {
                continue;
            }

            var id = Get(line, "Id");
            if (string.IsNullOrWhiteSpace(id))
            {
                // Trailing summary lines ("N package(s) found.") and wrapped continuation rows have no Id.
                continue;
            }

            var available = Get(line, "Available");
            packages.Add(new PackageInfo(
                Get(line, "Name"),
                id,
                Get(line, "Version"),
                string.IsNullOrWhiteSpace(available) ? null : available,
                Get(line, "Source")));
        }

        return packages;
    }
}
