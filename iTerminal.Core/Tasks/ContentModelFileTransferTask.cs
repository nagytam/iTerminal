using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
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
public abstract class ContentModelFileTransferTask(IFileSystem source, IFileSystem target, Url sourceUrl, Url requestedTargetUrl, bool overwrite, IErrorPromptService? errorPromptService = null) : BackgroundTask
{
    private Url _targetUrl = requestedTargetUrl;

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
            // A file sent to a folder goes inside it under its own name, as in every commander, rather
            // than being written to a path that is the folder's. Asked here, on the task's thread: on a
            // remote drive it is a round trip.
            var asFolder = new Url(UrlPath.TrimFolder(requestedTargetUrl.FullUrl) + UrlPath.SeparatorOf(requestedTargetUrl.FullUrl));
            if (target.ExistsFolder(asFolder) && !target.ExistsFile(requestedTargetUrl))
            {
                _targetUrl = new Url(UrlPath.Child(requestedTargetUrl.FullUrl, FocusedEntry.NameOf(sourceUrl), isFolder: false));
            }

            Subject = TaskNotificationHelper.TransferSubject(sourceUrl, _targetUrl);
            Description = $"{Verb} file{Environment.NewLine}{sourceUrl.FullName.Shrink(60)}{Environment.NewLine}to{Environment.NewLine}{_targetUrl.FullName.Shrink(60)}{Environment.NewLine}started";

            if (target.IsReadOnly)
            {
                throw new NotSupportedException($"Cannot {Verb.ToLowerInvariant()} to a read-only destination.");
            }
            if (DeletesSource && source.IsReadOnly)
            {
                throw new NotSupportedException($"Cannot {Verb.ToLowerInvariant()} from a read-only source; use copy instead.");
            }

            if (!overwrite && target.ExistsFile(_targetUrl))
            {
                throw new IOException($"The file '{_targetUrl.FullName}' already exists.");
            }

            if (sourceUrl.Length > 0)
            {
                // Before anything is written: a file that does not fit warns instead of failing half way.
                var spaceGuard = TransferSpaceGuard.For(source, sourceUrl, target, _targetUrl, DeletesSource, errorPromptService);
                if (spaceGuard is not null)
                {
                    await spaceGuard.EnsureAsync(() => sourceUrl.Length, CancellationToken);
                }
            }

            if (ReferenceEquals(source, target))
            {
                await SameFileSystemTransferAsync(source, sourceUrl, _targetUrl, overwrite);
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
                    await PipelinedFileCopyHelper.CopyAsync(source, target, sourceUrl, _targetUrl, progress, CancellationToken, PauseGate);
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

            Description = $"{Verb} file{Environment.NewLine}{sourceUrl.FullName.Shrink(60)}{Environment.NewLine}to{Environment.NewLine}{_targetUrl.FullName.Shrink(60)}{Environment.NewLine}finished";
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
            target.DeleteFile(_targetUrl, moveToRecycleBin: false);
        }
        catch
        {
            // Best-effort cleanup only — the transfer already failed/was cancelled.
        }
    }
}
