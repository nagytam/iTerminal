using System.Security.Cryptography;
using System.Text.Json;

namespace iTerminal.Core.Services.Sdk;

/// <summary>
/// Eclipse Adoptium's JDK feed (Temurin): the feature versions the API reports, and for each the newest general-availability
/// builds for the platform. An archive is checked against the SHA-256 Adoptium lists for it.
/// </summary>
public sealed class JdkFeed(HttpClient httpClient, string apiAddress = "https://api.adoptium.net/v3/")
{
    public const string DefaultApiAddress = "https://api.adoptium.net/v3/";

    /// <summary>How many GA builds of each feature version are listed, newest first.</summary>
    public const int BuildsPerFeature = 3;

    private static readonly TimeSpan ListingTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The builds of the LTS feature versions and of the most recent feature release, newest feature first, for one
    /// platform (<c>linux</c>, <c>windows</c> or <c>mac</c>) and architecture (<c>x64</c> or <c>aarch64</c>).
    /// </summary>
    public async Task<IReadOnlyList<AvailableSdk>> ListAsync(string os, string architecture, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ListingTimeout);
        using JsonDocument info = await GetJsonAsync(apiAddress + "info/available_releases", timeout.Token);

        List<int> features = [];
        if (info.RootElement.TryGetProperty("available_lts_releases", out JsonElement lts))
        {
            features.AddRange(lts.EnumerateArray().Select(element => element.GetInt32()));
        }

        if (info.RootElement.TryGetProperty("most_recent_feature_release", out JsonElement recent))
        {
            features.Add(recent.GetInt32());
        }

        var result = new List<AvailableSdk>();
        foreach (int feature in features.Distinct().OrderByDescending(feature => feature))
        {
            string address = $"{apiAddress}assets/feature_releases/{feature}/ga?architecture={architecture}&heap_size=normal&image_type=jdk"
                + $"&jvm_impl=hotspot&os={os}&page=0&page_size={BuildsPerFeature}&project=jdk&sort_method=DEFAULT&sort_order=DESC&vendor=eclipse";
            using JsonDocument assets = await GetJsonAsync(address, timeout.Token);
            result.AddRange(Parse(assets.RootElement, feature, os, architecture));
        }

        return result;
    }

    /// <summary>
    /// The JDK archives of one feature version's assets for the platform, in the API's order (newest first). The archive is
    /// the <c>jdk</c> image (not the <c>jre</c>), normal heap, and only one with a published SHA-256 is kept.
    /// </summary>
    internal static IReadOnlyList<AvailableSdk> Parse(JsonElement assets, int feature, string os, string architecture)
    {
        var result = new List<AvailableSdk>();
        bool isLatest = true;
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("binaries", out JsonElement binaries)) continue;
            string releaseName = asset.TryGetProperty("release_name", out JsonElement nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
            if (releaseName.Length == 0) continue;

            foreach (JsonElement binary in binaries.EnumerateArray())
            {
                if (!Text(binary, "os").Equals(os, StringComparison.Ordinal)
                    || !Text(binary, "architecture").Equals(architecture, StringComparison.Ordinal)
                    || !Text(binary, "image_type").Equals("jdk", StringComparison.Ordinal)
                    || !Text(binary, "heap_size").Equals("normal", StringComparison.Ordinal)
                    || !binary.TryGetProperty("package", out JsonElement package))
                {
                    continue;
                }

                string checksum = Text(package, "checksum");
                string link = Text(package, "link");
                string fileName = Text(package, "name");
                if (checksum.Length == 0 || link.Length == 0 || fileName.Length == 0) continue;

                long? size = package.TryGetProperty("size", out JsonElement sizeElement) && sizeElement.TryGetInt64(out long bytes) ? bytes : null;
                result.Add(new AvailableSdk(
                    SdkKind.Jdk,
                    releaseName,
                    feature.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    link,
                    fileName,
                    checksum.ToLowerInvariant(),
                    HashAlgorithmName.SHA256,
                    size,
                    IsLatest: isLatest));
                isLatest = false;
                break;
            }
        }

        return result;
    }

    /// <summary>The platform name Adoptium uses for this computer.</summary>
    public static string CurrentOs
    {
        get
        {
            if (OperatingSystem.IsWindows()) return "windows";
            return OperatingSystem.IsMacOS() ? "mac" : "linux";
        }
    }

    /// <summary>The architecture name Adoptium uses for this computer, or null when it publishes no JDK for it.</summary>
    public static string? CurrentArchitecture => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        System.Runtime.InteropServices.Architecture.Arm64 => "aarch64",
        _ => null,
    };

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private async Task<JsonDocument> GetJsonAsync(string address, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, address);
        request.Headers.Add("User-Agent", "iTerminal");
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
