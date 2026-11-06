using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;

namespace iTerminal.Core.Services.Checksums;

/// <summary>
/// A copy that finished but is not what the source holds. Not an I/O failure: the bad copy is
/// deliberately left where it is, flagged to the user, and a move keeps its source.
/// </summary>
public sealed class VerificationFailedException(string message) : IOException(message);

/// <summary>
/// Checks a finished copy against its source (the "Verify after copy" option): both files are read
/// once through their own <see cref="IFileSystem"/> and hashed by <see cref="IChecksumService"/>, so
/// any drive on either side works. Long-running by nature — call it from a background task only.
/// </summary>
public class TransferVerifier(IChecksumService checksumService)
{
    /// <param name="progress">Bytes read so far in the pass that is running (source first, then the copy).</param>
    /// <exception cref="VerificationFailedException">The two files differ in size or content.</exception>
    public async Task VerifyAsync(
        IFileSystem source,
        IFileSystem target,
        Url sourceUrl,
        Url targetUrl,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ChecksumResult expected = await checksumService.ComputeAsync(sourceUrl, source, progress, cancellationToken);
        ChecksumResult actual = await checksumService.ComputeAsync(targetUrl, target, progress, cancellationToken);

        if (expected.Bytes != actual.Bytes
            || !string.Equals(expected.Hashes[ChecksumAlgorithm.Sha256], actual.Hashes[ChecksumAlgorithm.Sha256], StringComparison.OrdinalIgnoreCase))
        {
            throw new VerificationFailedException(
                string.Format(UiText.Get("Task.VerifyFailed", "Verification failed: {0} differs from the source; the copy was kept"), targetUrl.FullName));
        }
    }
}
