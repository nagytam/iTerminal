using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Helper;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Shared engine for recursively transferring a folder between two <see cref="IFileSystem"/>
/// implementations. Same-drive transfers use the drive's native operation via
/// <see cref="SameFileSystemTransferAsync"/>; cross-drive transfers drain a separate
/// <see cref="FolderDiscoveryTask"/> (constructed and added to the
/// <see cref="iConsole.Tasks.TaskManager"/> alongside this task) through
/// <see cref="FolderCopyPipeline"/> — two visibly separate tasks rather than internal phases of
/// one. A move (<see cref="DeletesSource"/>) deletes each source file right after it's copied; if
/// any file failed (the user chose to continue past the error), the source root is deliberately
/// left in place — it still holds the failed file(s) — rather than risking deleting data that was
/// never actually moved; only when every file succeeds is the whole source folder removed in one
/// call.
/// </summary>
public abstract class ContentModelFolderTransferTask(
    IFileSystem source,
    IFileSystem target,
    Url sourceUrl,
    Url targetUrl,
    bool overwrite,
    FolderDiscoveryTask? discoveryTask,
    IErrorPromptService errorPromptService) : TransferTask
{
    /// <summary>Verb for task descriptions, e.g. "Copy".</summary>
    protected abstract string Verb { get; }

    /// <summary>Progressive verb for streaming updates, e.g. "Copying".</summary>
    protected abstract string ProgressVerb { get; }

    /// <summary>True when source files are removed after a successful transfer (move).</summary>
    protected abstract bool DeletesSource { get; }

    /// <summary>Native fast path used when source and target are the same drive.</summary>
    protected abstract Task SameFileSystemTransferAsync(IFileSystem fileSystem, Url sourceUrl, Url targetUrl, bool overwrite);

    public override async Task ExecuteAsync()
    {
        try
        {
            Subject = TaskNotificationHelper.TransferSubject(sourceUrl, targetUrl);
            Description = $"{Verb} folder{Environment.NewLine}{sourceUrl.FullName.Shrink(60)}{Environment.NewLine}to{Environment.NewLine}{targetUrl.FullName.Shrink(60)}{Environment.NewLine}started";

            if (target.IsReadOnly)
            {
                throw new NotSupportedException($"Cannot {Verb.ToLowerInvariant()} to a read-only destination.");
            }
            if (DeletesSource && source.IsReadOnly)
            {
                throw new NotSupportedException($"Cannot {Verb.ToLowerInvariant()} from a read-only source; use copy instead.");
            }

            IReadOnlyList<(Url Url, string Error)> failedItems = [];

            // Room for everything written; none is needed for a move within one volume (a rename).
            var spaceGuard = TransferSpaceGuard.For(source, sourceUrl, target, targetUrl, DeletesSource, errorPromptService);

            if (ReferenceEquals(source, target))
            {
                if (spaceGuard is not null)
                {
                    var totalBytes = TreeSize(source, sourceUrl, CancellationToken);
                    await spaceGuard.EnsureAsync(() => totalBytes, CancellationToken);
                }

                await SameFileSystemTransferAsync(source, sourceUrl, targetUrl, overwrite);
            }
            else
            {
                ArgumentNullException.ThrowIfNull(discoveryTask);
                // The discovery task has its own CancellationTokenSource — cancelling this task
                // (e.g. the progress window's Cancel button) must also cancel discovery, or it
                // could block forever writing to a channel nobody's draining anymore.
                using var cancelDiscoveryOnCancel = CancellationToken.Register(discoveryTask.CancellationTokenSource.Cancel);
                var bytesThrottle = System.Diagnostics.Stopwatch.StartNew();
                string currentFile = string.Empty;

                void UpdateProgress()
                {
                    var discovered = discoveryTask.ItemsProcessed;
                    var processed = ItemsProcessed;
                    var bytes = Interlocked.Read(ref _bytesCopied);
                    ReportBytes(bytes, discoveryTask.TotalBytes);
                    UpdatePercentage(discoveryTask, processed, discovered);
                    Description = processed == 0 && bytes == 0
                        ? $"Discovering…{Environment.NewLine}{discovered} file(s) found so far"
                        : $"{ProgressVerb} {processed}/{discovered} file(s){Environment.NewLine}{currentFile}";
                }

                failedItems = await FolderCopyPipeline.RunAsync(
                    source, target, discoveryTask, errorPromptService, Verb, CancellationToken,
                    (processed, discovered) =>
                    {
                        ReportItems(processed, discovered);
                        UpdateProgress();
                    },
                    onItemSucceeded: DeletesSource
                        ? url => source.DeleteFile(url, moveToRecycleBin: false)
                        : null,
                    reportBytes: bytes =>
                    {
                        // Raised for every buffer of every file: throttled so a fast copy does not
                        // flood the progress window with change notifications.
                        Interlocked.Exchange(ref _bytesCopied, bytes);
                        if (bytesThrottle.ElapsedMilliseconds < 100)
                        {
                            return;
                        }
                        bytesThrottle.Restart();
                        ReportBytes(bytes, discoveryTask.TotalBytes);
                        UpdatePercentage(discoveryTask, ItemsProcessed, discoveryTask.ItemsProcessed);
                    },
                    beforeFile: spaceGuard is null
                        ? null
                        : token => spaceGuard.EnsureAsync(
                            () => discoveryTask.TotalBytes - Interlocked.Read(ref _bytesCopied), token),
                    onFileStarted: url =>
                    {
                        currentFile = url.FullName.Shrink(60) ?? string.Empty;
                        Description = $"{ProgressVerb} {ItemsProcessed}/{discoveryTask.ItemsProcessed} file(s){Environment.NewLine}{currentFile}";
                    });
                ReportBytes(Interlocked.Read(ref _bytesCopied), discoveryTask.TotalBytes);

                if (DeletesSource && failedItems.Count == 0)
                {
                    Description = $"Removing source folder{Environment.NewLine}{sourceUrl.FullName.Shrink(60)}";
                    source.DeleteFolder(sourceUrl, recursive: true, moveToRecycleBin: false);
                }
            }

            Description = FormatFinishedDescription(failedItems);
            ProgressPercentage = 100;
            // A run the user chose to continue past still did not transfer everything, so the
            // progress window has to stay up with the list rather than dismiss itself.
            if (failedItems.Count > 0)
            {
                Error = $"{failedItems.Count} item(s) failed.";
            }
        }
        catch (OperationCanceledException)
        {
            Description = $"{Verb} folder{Environment.NewLine}{sourceUrl.FullName.Shrink(60)}{Environment.NewLine}cancelled";
        }
        catch (Exception ex)
        {
            Description = $"{Verb} folder failed: {ex.Message}";
            Error = ex.Message;
        }
    }

    private long _bytesCopied;

    /// <summary>Total size in bytes of the files under <paramref name="folderUrl"/>.</summary>
    private static long TreeSize(IFileSystem fileSystem, Url folderUrl, CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var child in ContentModelFolderTaskHelper.ListChildren(fileSystem, folderUrl))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (child.PathType == PathType.Folder)
            {
                // A link back to a folder it is inside would be walked for ever.
                if (child.ContainerType == ContainerType.FileSystem
                    && child.IsLink
                    && iConsole.Helper.FileLinkHelper.LeadsToAnAncestor(child.FullName))
                {
                    continue;
                }

                total += TreeSize(fileSystem, child, cancellationToken);
            }
            else if (child.Length > 0)
            {
                total += child.Length;
            }
        }

        return total;
    }

    /// <summary>
    /// Bar position from bytes when the sizes are known, else from files. While discovery is still
    /// running the total keeps growing, so the bar never goes backwards and stops short of 100
    /// until the whole tree has been found and copied.
    /// </summary>
    private void UpdatePercentage(FolderDiscoveryTask discoveryTask, long processedFiles, long discoveredFiles)
    {
        var totalBytes = discoveryTask.TotalBytes;
        var percentage = totalBytes > 0
            ? 100L * Interlocked.Read(ref _bytesCopied) / totalBytes
            : discoveredFiles > 0 ? 100L * processedFiles / discoveredFiles : 0;
        percentage = Math.Clamp(percentage, 0, 99);
        if (percentage > ProgressPercentage)
        {
            OnProgressChanged(percentage);
        }
    }

    private string FormatFinishedDescription(IReadOnlyList<(Url Url, string Error)> failedItems)
    {
        var baseDescription = $"{Verb} folder{Environment.NewLine}{sourceUrl.FullName.Shrink(60)}{Environment.NewLine}to{Environment.NewLine}{targetUrl.FullName.Shrink(60)}{Environment.NewLine}finished";
        if (failedItems.Count == 0)
        {
            return baseDescription;
        }

        var details = string.Join(", ", failedItems.Select(f => $"{f.Url.Name} ({f.Error})"));
        var suffix = DeletesSource ? " — source not fully removed" : string.Empty;
        return $"{baseDescription} with {failedItems.Count} error(s){suffix}: {details}";
    }
}
