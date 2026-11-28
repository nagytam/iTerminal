using System.Text.Json;

namespace iTerminal.Core.Model.Dotnet;

/// <summary>
/// The <c>global.json</c> that pins the .NET SDK of a folder's builds: its <c>sdk.version</c>, the <c>rollForward</c> policy
/// that says which installed SDKs satisfy that version, and <c>allowPrerelease</c>. The <c>dotnet</c> CLI reads the nearest
/// file from the working directory upward, so the studio does the same to pick the SDK that runs each project's commands.
/// </summary>
public sealed record DotnetGlobalJson(string FilePath, string Version, string RollForward, bool AllowPrerelease)
{
    /// <summary>The file's name.</summary>
    public const string FileName = "global.json";

    /// <summary>The roll-forward policy the SDK CLI uses when the file does not name one.</summary>
    public const string DefaultRollForward = "patch";

    /// <summary>
    /// The global.json that applies to <paramref name="folder"/>: the nearest one in it or above it. Null when there is none,
    /// or when the nearest one does not pin an SDK version (the CLI does not look further up past it).
    /// </summary>
    public static DotnetGlobalJson? Find(string folder)
    {
        string? current = Path.GetFullPath(folder);
        while (current is not null)
        {
            string candidate = Path.Combine(current, FileName);
            if (File.Exists(candidate)) return Read(candidate);
            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    /// <summary>Reads a global.json file; null when it cannot be read or does not pin an SDK version.</summary>
    public static DotnetGlobalJson? Read(string path)
    {
        try
        {
            return Parse(path, File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads the text of a global.json (<paramref name="path"/> is kept for the messages); null when it pins no SDK version.</summary>
    public static DotnetGlobalJson? Parse(string path, string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("sdk", out JsonElement sdk)
                || sdk.ValueKind != JsonValueKind.Object
                || !sdk.TryGetProperty("version", out JsonElement version)
                || version.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(version.GetString()))
            {
                return null;
            }

            string rollForward = sdk.TryGetProperty("rollForward", out JsonElement policy) && policy.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(policy.GetString())
                ? policy.GetString()!.Trim()
                : DefaultRollForward;
            bool allowPrerelease = sdk.TryGetProperty("allowPrerelease", out JsonElement prerelease) && prerelease.ValueKind == JsonValueKind.True;
            return new DotnetGlobalJson(path, version.GetString()!.Trim(), rollForward, allowPrerelease);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The installed SDK this file selects, under its <c>rollForward</c> policy, or null when none satisfies it. The policies
    /// follow the SDK CLI: <c>disable</c> takes the exact version; <c>patch</c> the lowest patch of the same feature band that
    /// is not older than the pinned one; <c>feature</c> the same within the minor version, rolling to the lowest higher feature
    /// band; <c>minor</c> and <c>major</c> roll the same way across minor and major versions. The <c>latest</c> forms take the
    /// newest candidate in their scope instead of the lowest. A prerelease is only a candidate when the file allows one or
    /// pins one.
    /// </summary>
    public DotnetSdk? Match(IEnumerable<DotnetSdk> installed)
    {
        if (!SdkVersionParts.TryParse(Version, out var requested)) return null;

        bool pinnedPrerelease = Version.Contains('-');
        List<DotnetSdk> candidates = [.. installed.Where(sdk => !sdk.IsPrerelease || AllowPrerelease || pinnedPrerelease)];
        string policy = RollForward.ToLowerInvariant();
        if (policy == "disable") return candidates.FirstOrDefault(sdk => sdk.Version.Equals(Version, StringComparison.Ordinal));

        bool latest = policy.StartsWith("latest", StringComparison.Ordinal);
        string scope = policy.Replace("latest", string.Empty, StringComparison.Ordinal);
        List<(DotnetSdk Sdk, (int Major, int Minor, int Band, int Patch) Parts)> pool = [];
        foreach (DotnetSdk sdk in candidates)
        {
            if (!SdkVersionParts.TryParse(sdk.Version, out var parts)) continue;
            if (parts.CompareTo(requested) < 0 || !InScope(parts, requested, scope)) continue;
            pool.Add((sdk, parts));
        }

        if (pool.Count == 0) return null;

        // Among equal versions the stable one wins; the ordering is by version, lowest or highest as the policy asks.
        return latest
            ? pool.OrderByDescending(candidate => candidate.Parts).ThenBy(candidate => candidate.Sdk.IsPrerelease).First().Sdk
            : pool.OrderBy(candidate => candidate.Parts).ThenBy(candidate => candidate.Sdk.IsPrerelease).First().Sdk;
    }

    /// <summary>Whether a candidate may stand in for the pinned version under a policy's scope (the parts are major, minor, feature band, patch).</summary>
    private static bool InScope((int Major, int Minor, int Band, int Patch) candidate, (int Major, int Minor, int Band, int Patch) requested, string scope) =>
        scope switch
        {
            "minor" => candidate.Major == requested.Major,
            "major" => true,
            "feature" => candidate.Major == requested.Major && candidate.Minor == requested.Minor,
            _ => candidate.Major == requested.Major && candidate.Minor == requested.Minor && candidate.Band == requested.Band,
        };
}

/// <summary>
/// An SDK version split into the parts the roll-forward policies compare: the major and minor versions, the feature band
/// (the hundreds of the patch number: 100 to 199 is band 1) and the patch within the band.
/// </summary>
internal static class SdkVersionParts
{
    /// <summary>Reads <c>11.0.100</c> or <c>11.0.100-preview.1</c> (the label is not compared); false for anything else.</summary>
    public static bool TryParse(string version, out (int Major, int Minor, int Band, int Patch) parts)
    {
        parts = default;
        if (!System.Version.TryParse(version.Split('-')[0], out System.Version? parsed) || parsed.Build < 0)
        {
            return false;
        }

        parts = (parsed.Major, parsed.Minor, parsed.Build / 100, parsed.Build % 100);
        return true;
    }
}
