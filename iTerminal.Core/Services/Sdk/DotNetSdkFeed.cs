using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace iTerminal.Core.Services.Sdk;

/// <summary>
/// Microsoft's .NET release feed: the index of channels, then each channel's <c>releases.json</c>, which lists the SDKs of
/// every release with an archive per platform and its SHA-512. Only the channels still supported are read (the newest
/// <see cref="MaxChannels"/>), so the list is what a new build can use.
/// </summary>
public sealed class DotNetSdkFeed(HttpClient httpClient, string indexAddress = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json")
{
    public const string DefaultIndexAddress = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json";

    /// <summary>How many supported channels are listed, newest first.</summary>
    public const int MaxChannels = 3;

    private static readonly TimeSpan ListingTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The SDKs of the supported channels for the platform, newest channel first.</summary>
    /// <param name="runtimeIdentifier">The platform, such as <c>linux-x64</c> or <c>win-x64</c>.</param>
    /// <param name="zip">True for the Windows zip archive; false for the <c>tar.gz</c> of the other platforms.</param>
    public async Task<IReadOnlyList<AvailableSdk>> ListAsync(string runtimeIdentifier, bool zip, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ListingTimeout);
        using JsonDocument index = await GetJsonAsync(indexAddress, timeout.Token);

        var sdks = new List<AvailableSdk>();
        foreach (JsonElement channel in index.RootElement.GetProperty("releases-index").EnumerateArray()
                     .Where(IsSupported)
                     .Take(MaxChannels))
        {
            string channelVersion = channel.GetProperty("channel-version").GetString() ?? string.Empty;
            string latest = channel.TryGetProperty("latest-sdk", out JsonElement latestSdk) ? latestSdk.GetString() ?? string.Empty : string.Empty;
            string releasesAddress = channel.GetProperty("releases.json").GetString() ?? string.Empty;
            if (channelVersion.Length == 0 || releasesAddress.Length == 0) continue;

            using JsonDocument releases = await GetJsonAsync(releasesAddress, timeout.Token);
            sdks.AddRange(Parse(releases.RootElement, channelVersion, latest, runtimeIdentifier, zip));
        }

        return sdks;
    }

    /// <summary>
    /// The SDKs of one channel's <c>releases.json</c> that have an archive for the platform, in the file's order (newest
    /// release first). A release that lists no archive for the platform contributes nothing.
    /// </summary>
    internal static IReadOnlyList<AvailableSdk> Parse(JsonElement releases, string channel, string latestSdk, string runtimeIdentifier, bool zip)
    {
        string extension = zip ? ".zip" : ".tar.gz";
        var result = new List<AvailableSdk>();
        if (!releases.TryGetProperty("releases", out JsonElement releaseList)) return result;

        foreach (JsonElement release in releaseList.EnumerateArray())
        {
            if (!release.TryGetProperty("sdks", out JsonElement sdks)) continue;
            foreach (JsonElement sdk in sdks.EnumerateArray())
            {
                string version = sdk.TryGetProperty("version", out JsonElement versionElement) ? versionElement.GetString() ?? string.Empty : string.Empty;
                if (version.Length == 0 || !sdk.TryGetProperty("files", out JsonElement files)) continue;

                foreach (JsonElement file in files.EnumerateArray())
                {
                    string rid = file.TryGetProperty("rid", out JsonElement ridElement) ? ridElement.GetString() ?? string.Empty : string.Empty;
                    string name = file.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                    string url = file.TryGetProperty("url", out JsonElement urlElement) ? urlElement.GetString() ?? string.Empty : string.Empty;
                    string hash = file.TryGetProperty("hash", out JsonElement hashElement) ? hashElement.GetString() ?? string.Empty : string.Empty;
                    if (rid != runtimeIdentifier || !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) || url.Length == 0 || hash.Length == 0) continue;

                    result.Add(new AvailableSdk(
                        SdkKind.DotNet,
                        version,
                        channel,
                        url,
                        Path.GetFileName(new Uri(url).LocalPath),
                        hash.ToLowerInvariant(),
                        HashAlgorithmName.SHA512,
                        Size: null,
                        IsLatest: version == latestSdk));
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The platform as the release feed names it (<c>linux-x64</c>, <c>linux-musl-x64</c>, <c>win-x64</c>, <c>osx-arm64</c>).
    /// <see cref="RuntimeInformation.RuntimeIdentifier"/> is not used: on Linux it names the distribution (<c>ubuntu.24.04-x64</c>),
    /// which the feed does not list.
    /// </summary>
    public static string CurrentRuntimeIdentifier()
    {
        string os = "linux";
        if (OperatingSystem.IsWindows()) os = "win";
        else if (OperatingSystem.IsMacOS()) os = "osx";
        string architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => "x64",
        };
        string runtime = RuntimeInformation.RuntimeIdentifier;
        bool musl = os == "linux" && (runtime.Contains("musl", StringComparison.OrdinalIgnoreCase) || runtime.StartsWith("alpine", StringComparison.OrdinalIgnoreCase));
        return musl ? $"linux-musl-{architecture}" : $"{os}-{architecture}";
    }

    private static bool IsSupported(JsonElement channel) =>
        channel.TryGetProperty("support-phase", out JsonElement phase) && phase.GetString() is "active" or "maintenance";

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
