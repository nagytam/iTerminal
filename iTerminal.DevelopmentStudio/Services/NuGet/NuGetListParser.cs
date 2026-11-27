using System.Text.Json;
using iConsole.Localization;
using iTerminal.DevelopmentStudio.Model.NuGet;

namespace iTerminal.DevelopmentStudio.Services.NuGet;

/// <summary>
/// Reads the JSON that <c>dotnet list &lt;target&gt; package --format json</c> prints, and the same with <c>--outdated</c>, into
/// <see cref="NuGetListing"/>. Pure: no process runs and no file is read, so the parsing is tested on fixtures.
/// </summary>
public static class NuGetListParser
{
    /// <summary>
    /// Combines the listing of the installed packages with the listing of the outdated ones. <paramref name="outdatedJson"/> is
    /// null when that command could not be run; the rows then have no latest version, and the problem says so.
    /// </summary>
    public static NuGetListing Combine(string installedJson, string? outdatedJson)
    {
        var problems = new List<string>();
        var installed = ReadPackages(installedJson, problems, out bool succeeded);
        if (!succeeded)
        {
            return new NuGetListing([], problems, Succeeded: false);
        }

        var latest = new Dictionary<(string, string), string>(new KeyComparer());
        if (outdatedJson is null)
        {
            problems.Add(UiText.Get("NuGet.CouldNotCheckNewer", "Could not check the sources for newer versions."));
        }
        else
        {
            var outdatedProblems = new List<string>();
            foreach (var usage in ReadPackages(outdatedJson, outdatedProblems, out _))
            {
                if (usage.LatestVersion is not null)
                {
                    latest.TryAdd((usage.ProjectPath, usage.PackageId), usage.LatestVersion);
                }
            }

            problems.AddRange(outdatedProblems);
        }

        var packages = installed
            .Select(usage => usage with
            {
                LatestVersion = latest.TryGetValue((usage.ProjectPath, usage.PackageId), out var newer) ? newer : null,
            })
            .ToList();
        return new NuGetListing(packages, problems, Succeeded: true);
    }

    /// <summary>
    /// The top-level packages of a listing, one per project and package id. A project with several target frameworks lists its
    /// packages once per framework; the first one is kept, and a latest version found under any framework is kept too.
    /// </summary>
    private static List<NuGetPackageUsage> ReadPackages(string json, List<string> problems, out bool succeeded)
    {
        var packages = new List<NuGetPackageUsage>();
        var seen = new Dictionary<(string, string), int>(new KeyComparer());
        succeeded = false;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            problems.Add(UiText.Get("NuGet.ListingUnreadable", "dotnet printed no package listing."));
            return packages;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                problems.Add(UiText.Get("NuGet.ListingUnreadable", "dotnet printed no package listing."));
                return packages;
            }

            if (root.TryGetProperty("problems", out var problemList) && problemList.ValueKind == JsonValueKind.Array)
            {
                foreach (var problem in problemList.EnumerateArray())
                {
                    if (problem.ValueKind == JsonValueKind.Object
                        && problem.TryGetProperty("text", out var text)
                        && text.ValueKind == JsonValueKind.String
                        && text.GetString() is { Length: > 0 } message)
                    {
                        problems.Add(message);
                    }
                }
            }

            if (!root.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Array)
            {
                // A listing without a projects array did not come from a restored project or solution; an empty one has an empty array.
                return packages;
            }

            foreach (var project in projects.EnumerateArray())
            {
                if (!TryGetString(project, "path", out var path))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(path);
                if (!project.TryGetProperty("frameworks", out var frameworks) || frameworks.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var framework in frameworks.EnumerateArray())
                {
                    if (!framework.TryGetProperty("topLevelPackages", out var topLevel) || topLevel.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var package in topLevel.EnumerateArray())
                    {
                        if (!TryGetString(package, "id", out var id))
                        {
                            continue;
                        }

                        TryGetString(package, "requestedVersion", out var requested);
                        TryGetString(package, "resolvedVersion", out var resolved);
                        string? latest = TryGetString(package, "latestVersion", out var candidate) && IsVersion(candidate) ? candidate : null;

                        var key = (fullPath, id);
                        if (seen.TryGetValue(key, out int index))
                        {
                            // Another framework of the same project: keep the first, but take a latest version it lacks.
                            var kept = packages[index];
                            if (kept.LatestVersion is null && latest is not null)
                            {
                                packages[index] = kept with { LatestVersion = latest };
                            }

                            continue;
                        }

                        seen[key] = packages.Count;
                        packages.Add(new NuGetPackageUsage(fullPath, id, requested, resolved, latest));
                    }
                }
            }
        }

        succeeded = true;
        return packages;
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
            && property.GetString() is { } text)
        {
            value = text;
            return true;
        }

        return false;
    }

    /// <summary>
    /// A version NuGet can install starts with a digit. <c>dotnet list</c> puts text such as "Not found at the sources" in the
    /// latest-version field when a package has no newer version it could find, and that text is not a version.
    /// </summary>
    public static bool IsVersion(string? text) => !string.IsNullOrEmpty(text) && char.IsDigit(text[0]);

    /// <summary>Project paths compare without case (Windows paths); package ids never do (NuGet ids are case-insensitive).</summary>
    private sealed class KeyComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) x, (string, string) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string, string) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2));
    }
}
