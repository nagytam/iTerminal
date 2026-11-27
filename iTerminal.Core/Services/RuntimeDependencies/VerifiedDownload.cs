using iConsole.Localization;
using System.Diagnostics;
using System.Security.Cryptography;

namespace iTerminal.Core.Services.RuntimeDependencies;

/// <summary>
/// Downloads one file to a <c>.partial</c> sibling, checks its size and SHA-256 against values the build carries,
/// and only then renames it into place. A file that does not match is deleted, so a half or a tampered download
/// is never taken for an install.
/// </summary>
internal static class VerifiedDownload
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(1);

    public static Task DownloadAsync(
        HttpClient httpClient,
        string address,
        string destinationPath,
        long expectedSize,
        string expectedSha256,
        IProgress<string>? progress,
        CancellationToken cancellationToken) =>
        DownloadAsync(httpClient, address, destinationPath, expectedSize, expectedSha256, HashAlgorithmName.SHA256, progress, cancellationToken);

    /// <summary>
    /// The same check with the digest's algorithm given (SHA-256 or SHA-512) and the size optional: a publisher that lists
    /// no size is checked by the digest alone.
    /// </summary>
    public static async Task DownloadAsync(
        HttpClient httpClient,
        string address,
        string destinationPath,
        long? expectedSize,
        string expectedDigest,
        HashAlgorithmName algorithm,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        string name = Path.GetFileName(destinationPath);
        string partial = destinationPath + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, address);
            request.Headers.Add("User-Agent", "iTerminal");
            using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
            long received = 0;
            await using (FileStream target = File.Create(partial))
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
                        progress.Report(expectedSize is { } total
                            ? string.Format(UiText.Get("Dep.DownloadingOf", "Downloading {0}: {1} of {2} MB"), name, Megabytes(received), Megabytes(total))
                            : string.Format(UiText.Get("Dep.Downloading", "Downloading {0}: {1} MB"), name, Megabytes(received)));
                    }
                }
            }

            if (expectedSize is { } size && received != size)
            {
                throw new InvalidDataException($"The download of {name} is {received} bytes, not the {size} expected.");
            }

            string actual;
            await using (FileStream written = File.OpenRead(partial))
            {
                byte[] digest = algorithm == HashAlgorithmName.SHA512
                    ? await SHA512.HashDataAsync(written, cancellationToken)
                    : await SHA256.HashDataAsync(written, cancellationToken);
                actual = Convert.ToHexString(digest).ToLowerInvariant();
            }

            if (actual != expectedDigest.ToLowerInvariant())
            {
                throw new InvalidDataException($"The download of {name} does not match its published checksum.");
            }

            File.Move(partial, destinationPath, overwrite: true);
            progress?.Report(string.Format(UiText.Get("Dep.Downloaded", "Downloaded {0} ({1} MB, checksum verified)"), name, Megabytes(received)));
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    private static string Megabytes(long bytes) => (bytes / 1048576.0).ToString("0.0");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Intentionally empty: there is nothing to do for this case.
        }
    }
}
