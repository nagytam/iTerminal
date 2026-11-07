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

    public static async Task DownloadAsync(
        HttpClient httpClient,
        string address,
        string destinationPath,
        long expectedSize,
        string expectedSha256,
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
                        progress.Report($"Downloading {name}: {Megabytes(received)} of {Megabytes(expectedSize)} MB");
                    }
                }
            }

            if (received != expectedSize)
            {
                throw new InvalidDataException($"The download of {name} is {received} bytes, not the {expectedSize} expected.");
            }

            string actual;
            await using (FileStream written = File.OpenRead(partial))
            {
                actual = Convert.ToHexString(await SHA256.HashDataAsync(written, cancellationToken)).ToLowerInvariant();
            }

            if (actual != expectedSha256.ToLowerInvariant())
            {
                throw new InvalidDataException($"The download of {name} does not match its published checksum.");
            }

            File.Move(partial, destinationPath, overwrite: true);
            progress?.Report($"Downloaded {name} ({Megabytes(received)} MB, checksum verified)");
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
        }
    }
}
