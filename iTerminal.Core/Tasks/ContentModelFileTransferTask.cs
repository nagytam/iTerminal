using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Helper;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Shared engine for transferring a single file between two <see cref="IFileSystem"/>
/// implementations (e.g. a real disk and the memory drive). When <paramref name="source"/> and
/// <paramref name="target"/> are the same drive it delegates to the drive's native operation via
/// <see cref="SameFileSystemTransferAsync"/>; otherwise it streams the content through a
/// <see cref="System.IO.Pipelines.Pipe"/> via <see cref="PipelinedFileCopyHelper"/> rather than
/// buffering the whole file in memory, so it works regardless of which drive types are involved.
/// Copy and move differ only in the verb, the native fast path, and whether the source is deleted
/// afterwards (<see cref="DeletesSource"/>).
/// </summary>
public abstract class ContentModelFileTransferTask(IFileSystem source, IFileSystem target, Url sourceUrl, Url targetUrl, bool overwrite) : BackgroundTask
{
    /// <summary>Verb for task descriptions, e.g. "Copy".</summary>
    protected abstract string Verb { get; }

    /// <summary>Progressive verb for streaming updates, e.g. "Copying".</summary>
    protected abstract string ProgressVerb { get; }

    /// <summary>True when the source file is removed after a successful transfer (move).</summary>
    protected abstract bool DeletesSource { get; }

    /// <summary>Native fast path used when source and target are the same drive.</summary>
    protected abstract Task SameFileSystemTransferAsync(IFileSystem fileSystem, Url sourceUrl, Url targetUrl, bool overwrite);

    public override async Task ExecuteAsync()
    {
        try
        {
            Subject = TaskNotificationHelper.TransferSubject(sourceUrl, targetUrl);
            Description = $"{Verb} file{Environment.NewLine}{sourceUrl.FullName.Shrink(60)}{Environment.NewLine}to{Environment.NewLine}{targetUrl.FullName.Shrink(60)}{Environment.NewLine}started";

            if (target.IsReadOnly)
            {
                throw new NotSupportedException($"Cannot {Verb.ToLowerInvariant()} to a read-only destination.");
            }
            if (DeletesSource && source.IsReadOnly)
            {
                throw new NotSupportedException($"Cannot {Verb.ToLowerInvariant()} from a read-only source; use copy instead.");
            }

            if (!overwrite && target.ExistsFile(targetUrl))
            {
                throw new IOException($"The file '{targetUrl.FullName}' already exists.");
            }

            if (ReferenceEquals(source, target))
            {
                await SameFileSystemTransferAsync(source, sourceUrl, targetUrl, overwrite);
            }
            else
            {
                var totalBytes = sourceUrl.Length;
                var progress = new SynchronousProgress<long>(bytesCopied =>
                {
                    if (totalBytes > 0)
                    {
                        OnProgressChanged(Math.Clamp(100L * bytesCopied / totalBytes, 0, 100));
                        Description = $"{ProgressVerb}{Environment.NewLine}{sourceUrl.Name}{Environment.NewLine}{bytesCopied.ToFileSize()} / {totalBytes.ToFileSize()}";
                    }
                    else
                    {
                        Description = $"{ProgressVerb}{Environment.NewLine}{sourceUrl.Name}{Environment.NewLine}{bytesCopied.ToFileSize()} copied";
                    }
                });

                try
                {
                    await PipelinedFileCopyHelper.CopyAsync(source, target, sourceUrl, targetUrl, progress, CancellationToken);
                }
                catch (OperationCanceledException)
                {
                    TryDeletePartialTarget();
                    throw;
                }

                if (DeletesSource)
                {
                    source.DeleteFile(sourceUrl, moveToRecycleBin: false);
                }
            }

            Description = $"{Verb} file{Environment.NewLine}{sourceUrl.FullName.Shrink(60)}{Environment.NewLine}to{Environment.NewLine}{targetUrl.FullName.Shrink(60)}{Environment.NewLine}finished";
            ProgressPercentage = 100;
        }
        catch (OperationCanceledException)
        {
            Description = $"{Verb} file{Environment.NewLine}{sourceUrl.FullName.Shrink(60)}{Environment.NewLine}cancelled";
        }
        catch (Exception ex)
        {
            Description = $"{Verb} file failed: {ex.Message}";
            Error = ex.Message;
        }
    }

    private void TryDeletePartialTarget()
    {
        try
        {
            target.DeleteFile(targetUrl, moveToRecycleBin: false);
        }
        catch
        {
            // Best-effort cleanup only — the transfer already failed/was cancelled.
        }
    }
}
