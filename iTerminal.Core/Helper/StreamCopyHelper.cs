namespace iTerminal.Core.Helper;

/// <summary>
/// Buffered <see cref="Stream"/>-to-<see cref="Stream"/> copy with cumulative-bytes progress
/// reporting and cancellation support. Generalizes the copy loop that used to live in
/// <c>AsyncFileCopyHelper</c> so it works against any stream pair, not just real files.
/// </summary>
public static class StreamCopyHelper
{
    public const int DefaultBufferSize = 1024 * 128;

    public static async Task CopyWithProgressAsync(
        Stream source,
        Stream destination,
        IProgress<long>? progress,
        CancellationToken cancellationToken,
        int bufferSize = DefaultBufferSize)
    {
        var buffer = new byte[bufferSize];
        long totalBytesCopied = 0;
        int bytesRead;
        progress?.Report(totalBytesCopied);
        while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            totalBytesCopied += bytesRead;
            progress?.Report(totalBytesCopied);
        }
    }
}
