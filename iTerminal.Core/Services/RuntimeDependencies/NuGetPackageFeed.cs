using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace iTerminal.Core.Services.RuntimeDependencies;

/// <summary>
/// Reads a package's published versions and downloads a package from a NuGet feed: the versions from its flat
/// container (<c>{base}/{id}/index.json</c>), the package from the same place, and the checksum the package
/// was published with from its registration (<c>{registration}/{id}/{version}.json</c>, then the catalog entry).
/// </summary>
public sealed class NuGetPackageFeed(
    HttpClient httpClient,
    string baseAddress = "https://api.nuget.org/v3-flatcontainer/",
    string registrationAddress = "https://api.nuget.org/v3/registration5-semver1/")
{
    private static readonly TimeSpan ListingTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(1);

    public async Task<IReadOnlyList<string>> ListVersionsAsync(string packageId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ListingTimeout);

        string id = packageId.ToLowerInvariant();
        using HttpResponseMessage response = await httpClient.GetAsync($"{baseAddress}{id}/index.json", timeout.Token);
        response.EnsureSuccessStatusCode();

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        return document.RootElement.GetProperty("versions").EnumerateArray()
            .Select(version => version.GetString())
            .OfType<string>()
            .ToList();
    }

    /// <summary>
    /// Downloads one package and checks it against the SHA-512 the feed published for it. The package is run
    /// from the folder it is unpacked into, so a download that does not match is deleted, not kept.
    /// </summary>
    public async Task DownloadAsync(string packageId, string version, string destinationPath, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        string id = packageId.ToLowerInvariant();
        string address = $"{baseAddress}{id}/{version}/{id}.{version}.nupkg";
        string expectedHash = await PublishedSha512Async(packageId, version, cancellationToken);

        using HttpResponseMessage response = await httpClient.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
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
                        ? $"Downloading {packageId} {version}: {Megabytes(received)} of {Megabytes(total.Value)} MB"
                        : $"Downloading {packageId} {version}: {Megabytes(received)} MB");
                }
            }
        }

        await using (FileStream written = File.OpenRead(destinationPath))
        {
            string actualHash = Convert.ToBase64String(await SHA512.HashDataAsync(written, cancellationToken));
            if (actualHash != expectedHash)
            {
                written.Close();
                File.Delete(destinationPath);
                throw new InvalidDataException($"The download of {packageId} {version} does not match the checksum the feed publishes for it.");
            }
        }

        progress?.Report($"Downloaded {packageId} {version} ({Megabytes(received)} MB, checksum verified)");
    }

    /// <summary>The package hash the catalog records for this version. The flat container does not publish one.</summary>
    internal async Task<string> PublishedSha512Async(string packageId, string version, CancellationToken cancellationToken)
    {
        string registration = await httpClient.GetStringAsync(
            $"{registrationAddress}{packageId.ToLowerInvariant()}/{version}.json", cancellationToken);
        using JsonDocument entry = JsonDocument.Parse(registration);
        string catalogUrl = entry.RootElement.GetProperty("catalogEntry").GetString()
            ?? throw new InvalidDataException($"The feed has no catalog entry for {packageId} {version}.");

        using JsonDocument catalog = JsonDocument.Parse(await httpClient.GetStringAsync(catalogUrl, cancellationToken));
        if (catalog.RootElement.GetProperty("packageHashAlgorithm").GetString() != "SHA512")
        {
            throw new InvalidDataException($"The feed publishes {packageId} {version} with a hash other than SHA-512.");
        }

        return catalog.RootElement.GetProperty("packageHash").GetString()
            ?? throw new InvalidDataException($"The feed publishes no hash for {packageId} {version}.");
    }

    private static string Megabytes(long bytes) => (bytes / 1048576.0).ToString("0.0");
}
