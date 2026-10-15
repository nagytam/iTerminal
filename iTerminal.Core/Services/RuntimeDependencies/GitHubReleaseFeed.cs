using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace iTerminal.Core.Services.RuntimeDependencies;

/// <summary>
/// Reads a GitHub repository's published releases and downloads one of their assets. The versions are the
/// release tags (<c>v1.2.3</c>, without pre-releases and drafts); an asset is checked against the
/// <c>sha256</c> digest GitHub records for it, and a download that does not match is deleted.
/// </summary>
public sealed class GitHubReleaseFeed(HttpClient httpClient, string apiAddress = "https://api.github.com/")
{
    private static readonly TimeSpan ListingTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(1);

    /// <summary>Every stable release version of <paramref name="repository"/> among the most recent releases, as published.</summary>
    public async Task<IReadOnlyList<string>> ListVersionsAsync(string repository, CancellationToken cancellationToken)
    {
        using JsonDocument document = await GetJsonAsync($"{apiAddress}repos/{repository}/releases?per_page=100", cancellationToken);
        return document.RootElement.EnumerateArray()
            .Where(release => !release.GetProperty("draft").GetBoolean() && !release.GetProperty("prerelease").GetBoolean())
            .Select(release => release.GetProperty("tag_name").GetString() ?? string.Empty)
            .Select(tag => tag.TrimStart('v'))
            .Where(version => !version.Contains('-') && Version.TryParse(version, out _))
            .ToList();
    }

    /// <summary>The release tagged <c>v{version}</c>, with its assets.</summary>
    public async Task<GitHubRelease> GetReleaseAsync(string repository, string version, CancellationToken cancellationToken)
    {
        using JsonDocument document = await GetJsonAsync($"{apiAddress}repos/{repository}/releases/tags/v{version}", cancellationToken);
        IReadOnlyList<GitHubAsset> assets = document.RootElement.GetProperty("assets").EnumerateArray()
            .Select(asset => new GitHubAsset(
                asset.GetProperty("name").GetString() ?? string.Empty,
                asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
                asset.GetProperty("size").GetInt64(),
                asset.TryGetProperty("digest", out JsonElement digest) ? ParseSha256(digest.GetString()) : null))
            .ToList();
        return new GitHubRelease($"v{version}", assets);
    }

    /// <summary>
    /// Downloads one asset to <paramref name="destinationPath"/> and checks it against the digest GitHub
    /// published for it. An asset with no published digest is refused: a binary that cannot be verified
    /// is not run.
    /// </summary>
    public async Task DownloadAsync(GitHubAsset asset, string destinationPath, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        string expected = asset.Sha256
            ?? throw new InvalidDataException($"GitHub publishes no checksum for {asset.Name}, so it is not installed.");

        using HttpRequestMessage request = new(HttpMethod.Get, asset.DownloadUrl);
        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength ?? (asset.Size > 0 ? asset.Size : null);
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        long received = 0;
        await using (FileStream target = File.Create(destinationPath))
        {
            byte[] buffer = new byte[81920];
            var clock = Stopwatch.StartNew();
            TimeSpan? lastReport = null;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;

                if (progress is not null && (lastReport is null || clock.Elapsed - lastReport.Value >= ProgressInterval))
                {
                    lastReport = clock.Elapsed;
                    progress.Report(total is > 0
                        ? $"Downloading {asset.Name}: {Megabytes(received)} of {Megabytes(total.Value)} MB"
                        : $"Downloading {asset.Name}: {Megabytes(received)} MB");
                }
            }
        }

        await using (FileStream written = File.OpenRead(destinationPath))
        {
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(written, cancellationToken)).ToLowerInvariant();
            if (actual != expected)
            {
                written.Close();
                File.Delete(destinationPath);
                throw new InvalidDataException($"The download of {asset.Name} does not match the checksum GitHub publishes for it.");
            }
        }

        progress?.Report($"Downloaded {asset.Name} ({Megabytes(received)} MB, checksum verified)");
    }

    private async Task<JsonDocument> GetJsonAsync(string address, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ListingTimeout);

        // GitHub refuses requests without a user agent.
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Add("User-Agent", "iTerminal");
        request.Headers.Add("Accept", "application/vnd.github+json");
        using HttpResponseMessage response = await httpClient.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
    }

    /// <summary>The digest GitHub records for an asset, as the hex of its SHA-256; null when none is published.</summary>
    internal static string? ParseSha256(string? digest) =>
        digest is not null && digest.StartsWith("sha256:", StringComparison.Ordinal)
            ? digest["sha256:".Length..].ToLowerInvariant()
            : null;

    private static string Megabytes(long bytes) => (bytes / 1048576.0).ToString("0.0");
}

/// <summary>One published release: its tag and the files it carries.</summary>
public sealed record GitHubRelease(string Tag, IReadOnlyList<GitHubAsset> Assets);

/// <param name="Sha256">The SHA-256 of the file as GitHub records it, lower-case hex; null when GitHub has none.</param>
public sealed record GitHubAsset(string Name, string DownloadUrl, long Size, string? Sha256);
