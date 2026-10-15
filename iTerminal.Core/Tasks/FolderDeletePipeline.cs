using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Drains a <see cref="FolderDeleteDiscoveryTask"/>'s channel with a small pool of delete workers
/// — the delete-side half of the two-task pipeline (<see cref="FolderDeleteDiscoveryTask"/> is the
/// other half, running concurrently as its own visible task). Once every file is gone, folders are
/// removed bottom-up using the discovery task's visitation-order list (see
/// <see cref="FolderDeleteDiscoveryTask.Folders"/>) walked in reverse.
///
/// On a per-file failure, prompts the user via <see cref="IErrorPromptService"/> to continue (skip
/// the file, keep going) or cancel (abort the rest); a folder that can't be removed afterward
/// (typically because a skipped file is still inside it) is recorded as a failure too but doesn't
/// prompt again — the user already decided to continue past the file that caused it.
/// </summary>
internal static class FolderDeletePipeline
{
    private static readonly int DeleteWorkerCount = Math.Clamp(Environment.ProcessorCount, 2, 4);

    public static async Task<IReadOnlyList<(Url Url, string Error)>> RunAsync(
        IFileSystem target,
        FolderDeleteDiscoveryTask discoveryTask,
        bool moveToRecycleBin,
        IErrorPromptService errorPromptService,
        CancellationToken cancellationToken,
        Action<long, long> reportProgress)
    {
        long deletedFiles = 0;
        Exception? firstError = null;
        int userCancelledAfterError = 0;
        var progressThrottle = System.Diagnostics.Stopwatch.StartNew();
        var failedItems = new List<(Url Url, string Error)>();
        var failedItemsLock = new Lock();

        void Report()
        {
            var discovered = discoveryTask.ItemsProcessed;
            var deleted = Interlocked.Read(ref deletedFiles);
            if (deleted < discovered && progressThrottle.ElapsedMilliseconds < 200)
            {
                return;
            }
            progressThrottle.Restart();
            reportProgress(deleted, discovered);
        }

        void HandleException(Exception ex, CancellationTokenSource linkedCts)
        {
            var isRealUserCancellation = ex is OperationCanceledException && cancellationToken.IsCancellationRequested;
            if (!isRealUserCancellation && ex is not OperationCanceledException)
            {
                Interlocked.CompareExchange(ref firstError, ex, null);
            }
            linkedCts.Cancel();
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        async Task RunDeleteWorkerAsync()
        {
            try
            {
                await foreach (var fileUrl in discoveryTask.Reader.ReadAllAsync(linkedCts.Token))
                {
                    try
                    {
                        target.DeleteFile(fileUrl, moveToRecycleBin);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        var shouldContinue = await errorPromptService.ConfirmContinueAsync(
                            "Delete error",
                            $"Failed to delete{Environment.NewLine}{fileUrl.FullName}:{Environment.NewLine}{ex.Message}",
                            linkedCts.Token);
                        if (!shouldContinue)
                        {
                            Interlocked.Exchange(ref userCancelledAfterError, 1);
                            linkedCts.Cancel();
                            throw new OperationCanceledException("Cancelled after an error.", ex);
                        }
                        lock (failedItemsLock)
                        {
                            failedItems.Add((fileUrl, ex.Message));
                        }
                    }

                    Interlocked.Increment(ref deletedFiles);
                    Report();
                }
            }
            catch (Exception ex)
            {
                HandleException(ex, linkedCts);
            }
        }

        var deleteWorkers = new Task[DeleteWorkerCount];
        for (var i = 0; i < DeleteWorkerCount; i++)
        {
            deleteWorkers[i] = Task.Run(RunDeleteWorkerAsync, linkedCts.Token);
        }

        try
        {
            await Task.WhenAll(deleteWorkers);
        }
        catch
        {
            // The real failure (if any) is captured in firstError and surfaced below.
        }

        if (firstError is not null)
        {
            throw firstError;
        }

        if (Interlocked.CompareExchange(ref userCancelledAfterError, 0, 0) == 1)
        {
            throw new OperationCanceledException("Cancelled after an error.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var folders = discoveryTask.Folders;
        for (var i = folders.Count - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                target.DeleteFolder(folders[i], recursive: false, moveToRecycleBin);
            }
            catch (Exception ex)
            {
                failedItems.Add((folders[i], ex.Message));
            }
        }

        return failedItems;
    }
}
