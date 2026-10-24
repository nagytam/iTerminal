using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using System.IO.Pipelines;

namespace iTerminal.Core.Helper;

/// <summary>
/// Connects a source <see cref="IFileSystem"/> entry to a target <see cref="IFileSystem"/> entry
/// through a <see cref="Pipe"/>, running the read side (<see cref="IFileSystem.StreamToAsync"/>)
/// and the write side (<see cref="IFileSystem.StreamFromAsync"/>) as genuinely concurrent tasks.
/// The pipe is bounded so memory use stays flat regardless of file size, and works uniformly for
/// any pair of drive types since every <see cref="IFileSystem"/> exposes the same primitives.
/// </summary>
public static class PipelinedFileCopyHelper
{
    private const int PauseWriterThreshold = 1 << 20; // 1 MB
    private const int ResumeWriterThreshold = 1 << 19; // 512 KB

    public static async Task CopyAsync(
        IFileSystem source,
        IFileSystem target,
        Url sourceUrl,
        Url targetUrl,
        IProgress<long>? progress,
        CancellationToken cancellationToken,
        PauseGate? pauseGate = null)
    {
        var pipe = new Pipe(new PipeOptions(
            pauseWriterThreshold: PauseWriterThreshold,
            resumeWriterThreshold: ResumeWriterThreshold));

        // Linked so that if either side fails, the other side's blocked Read/WriteAsync on the
        // pipe is cancelled promptly instead of hanging forever waiting for data that will never
        // arrive (producer failed) or backpressure that will never clear (consumer failed).
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var linkedToken = linkedCts.Token;

        var produce = Task.Run(async () =>
        {
            Exception? error = null;
            // A paused copy waits at its next chunk: the writer is the side that reads the source.
            Stream writerStream = pauseGate is null ? pipe.Writer.AsStream() : new GatedStream(pipe.Writer.AsStream(), pauseGate);
            try
            {
                await source.StreamToAsync(sourceUrl, writerStream, progress, linkedToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                error = ex;
                linkedCts.Cancel();
                throw;
            }
            finally
            {
                await pipe.Writer.CompleteAsync(error).ConfigureAwait(false);
            }
        }, cancellationToken);

        var consume = Task.Run(async () =>
        {
            Exception? error = null;
            var readerStream = pipe.Reader.AsStream();
            try
            {
                await target.StreamFromAsync(targetUrl, readerStream, null, linkedToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                error = ex;
                linkedCts.Cancel();
                throw;
            }
            finally
            {
                await pipe.Reader.CompleteAsync(error).ConfigureAwait(false);
            }
        }, cancellationToken);

        await Task.WhenAll(produce, consume).ConfigureAwait(false);
    }
}
