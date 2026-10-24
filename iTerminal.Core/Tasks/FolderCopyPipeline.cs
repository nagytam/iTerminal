using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Helper;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Drains a <see cref="FolderDiscoveryTask"/>'s channel with a small pool of copy workers,
/// streaming each file's content through <see cref="PipelinedFileCopyHelper"/> as files are found
/// — the copy-side half of the two-task pipeline (<see cref="FolderDiscoveryTask"/> is the other
/// half, running concurrently as its own visible task). Shared by
/// <see cref="ContentModelCopyFolderTask"/> and <see cref="ContentModelMoveFolderTask"/> (which
/// passes <c>onItemSucceeded</c> to delete each source file right after it's copied).
///
/// On a per-file failure, prompts the user via <see cref="IErrorPromptService"/> to continue (skip
/// the file, keep going) or cancel (abort the rest); the returned list reports every file that was
/// skipped this way, for the caller to summarize.
/// </summary>
internal static class FolderCopyPipeline
{
    private static readonly int CopyWorkerCount = Math.Clamp(Environment.ProcessorCount, 2, 4);

    public static async Task<IReadOnlyList<(Url Url, string Error)>> RunAsync(
        IFileSystem source,
        IFileSystem target,
        FolderDiscoveryTask discoveryTask,
        IErrorPromptService errorPromptService,
        string operationName,
        CancellationToken cancellationToken,
        Action<long, long> reportProgress,
        Action<Url>? onItemSucceeded = null,
        Action<long>? reportBytes = null,
        Action<Url>? onFileStarted = null,
        Func<CancellationToken, Task>? beforeFile = null,
        PauseGate? pauseGate = null)
    {
        long processedFiles = 0;
        long processedBytes = 0;
        Exception? firstError = null;
        int userCancelledAfterError = 0;
        var progressThrottle = System.Diagnostics.Stopwatch.StartNew();
        var failedItems = new List<(Url Url, string Error)>();
        var failedItemsLock = new Lock();

        void Report()
        {
            var discovered = discoveryTask.ItemsProcessed;
            var processed = Interlocked.Read(ref processedFiles);
            if (processed < discovered && progressThrottle.ElapsedMilliseconds < 200)
            {
                return;
            }
            progressThrottle.Restart();
            reportProgress(processed, discovered);
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

        async Task RunCopyWorkerAsync()
        {
            try
            {
                await foreach (var item in discoveryTask.Reader.ReadAllAsync(linkedCts.Token))
                {
                    try
                    {
                        if (beforeFile is not null)
                        {
                            // May hold this worker (and, behind one prompt, the others) while the user frees space.
                            await beforeFile(linkedCts.Token);
                        }

                        onFileStarted?.Invoke(item.Source);

                        // Each file reports its own running total; the delta since its last report
                        // is what joins the shared count, so concurrent workers add up correctly.
                        long reportedForFile = 0;
                        var fileProgress = reportBytes is null
                            ? null
                            : new SynchronousProgress<long>(bytesSoFar =>
                            {
                                var delta = bytesSoFar - reportedForFile;
                                reportedForFile = bytesSoFar;
                                reportBytes(Interlocked.Add(ref processedBytes, delta));
                            });

                        await PipelinedFileCopyHelper.CopyAsync(source, target, item.Source, item.Target, fileProgress, linkedCts.Token, pauseGate);
                        onItemSucceeded?.Invoke(item.Source);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        var shouldContinue = await errorPromptService.ConfirmContinueAsync(
                            $"{operationName} error",
                            $"Failed to {operationName.ToLowerInvariant()}{Environment.NewLine}{item.Source.FullName}:{Environment.NewLine}{ex.Message}",
                            linkedCts.Token);
                        if (!shouldContinue)
                        {
                            Interlocked.Exchange(ref userCancelledAfterError, 1);
                            linkedCts.Cancel();
                            throw new OperationCanceledException("Cancelled after an error.", ex);
                        }
                        lock (failedItemsLock)
                        {
                            failedItems.Add((item.Source, ex.Message));
                        }
                        TryDeletePartialTarget(target, item.Target);
                    }

                    Interlocked.Increment(ref processedFiles);
                    Report();
                }
            }
            catch (Exception ex)
            {
                HandleException(ex, linkedCts);
            }
        }

        var copyWorkers = new Task[CopyWorkerCount];
        for (var i = 0; i < CopyWorkerCount; i++)
        {
            copyWorkers[i] = Task.Run(RunCopyWorkerAsync, linkedCts.Token);
        }

        try
        {
            await Task.WhenAll(copyWorkers);
        }
        catch
        {
            // The real failure (if any) is captured in firstError and surfaced below, so a peer
            // worker's cascading OperationCanceledException here doesn't mask it.
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

        return failedItems;
    }

    private static void TryDeletePartialTarget(IFileSystem target, Url targetUrl)
    {
        try
        {
            target.DeleteFile(targetUrl, moveToRecycleBin: false);
        }
        catch
        {
            // Best-effort cleanup only — the file already failed to copy.
        }
    }
}
