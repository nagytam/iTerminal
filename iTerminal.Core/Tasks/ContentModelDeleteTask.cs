using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Deletes a file or folder on an <see cref="IFileSystem"/> as a background task instead of
/// blocking the caller's thread. A non-recursive folder delete keeps the existing single-call
/// semantics (only succeeds if empty); a recursive folder delete is fed by a separate
/// <see cref="FolderDeleteDiscoveryTask"/> (constructed and added to the
/// <see cref="iConsole.Tasks.TaskManager"/> alongside this task, running concurrently) that this
/// task drains (see <see cref="FolderDeletePipeline"/>) — two visibly separate tasks rather than
/// internal phases of one, so large trees show live progress and can be cancelled instead of
/// blocking on one opaque native call.
/// </summary>
public class ContentModelDeleteTask(
    IFileSystem target,
    Url url,
    bool recursive,
    bool moveToRecycleBin,
    FolderDeleteDiscoveryTask? discoveryTask,
    IErrorPromptService errorPromptService) : TransferTask
{
    public override async Task ExecuteAsync()
    {
        try
        {
            Subject = iTerminal.Core.Commands.FocusedEntry.NameOf(url);
            Description = $"Delete{Environment.NewLine}{url.FullName.Shrink(60)}{Environment.NewLine}started";

            if (target.IsReadOnly)
            {
                throw new NotSupportedException("Cannot delete from a read-only source.");
            }

            IReadOnlyList<(Url Url, string Error)> failedItems = [];

            if (url.PathType == PathType.Folder && recursive)
            {
                ArgumentNullException.ThrowIfNull(discoveryTask);
                // The discovery task has its own CancellationTokenSource — cancelling this task
                // (e.g. the progress window's Cancel button) must also cancel discovery, or it
                // could block forever writing to a channel nobody's draining anymore.
                using var cancelDiscoveryOnCancel = CancellationToken.Register(discoveryTask.CancellationTokenSource.Cancel);
                failedItems = await FolderDeletePipeline.RunAsync(target, discoveryTask, moveToRecycleBin, errorPromptService, CancellationToken, (deleted, discovered) =>
                {
                    ReportItems(deleted, discovered);
                    Description = deleted == 0
                        ? $"Scanning…{Environment.NewLine}{discovered} file(s) found so far"
                        : $"Deleting {deleted}/{discovered} file(s){Environment.NewLine}{url.FullName.Shrink(60)}";
                });
            }
            else if (url.PathType == PathType.Folder)
            {
                target.DeleteFolder(url, recursive: false, moveToRecycleBin);
            }
            else
            {
                target.DeleteFile(url, moveToRecycleBin);
            }

            Description = FormatFinishedDescription(failedItems);
            ProgressPercentage = 100;
            // A run the user chose to continue past still did not delete everything, so the
            // progress window has to stay up with the list rather than dismiss itself.
            if (failedItems.Count > 0)
            {
                Error = $"{failedItems.Count} item(s) failed.";
            }
        }
        catch (OperationCanceledException)
        {
            Description = $"Delete{Environment.NewLine}{url.FullName.Shrink(60)}{Environment.NewLine}cancelled";
        }
        catch (Exception ex)
        {
            Description = $"Delete failed: {ex.Message}";
            Error = ex.Message;
        }
    }

    private string FormatFinishedDescription(IReadOnlyList<(Url Url, string Error)> failedItems)
    {
        var baseDescription = $"Delete{Environment.NewLine}{url.FullName.Shrink(60)}{Environment.NewLine}finished";
        if (failedItems.Count == 0)
        {
            return baseDescription;
        }

        var details = string.Join(", ", failedItems.Select(f => $"{f.Url.Name} ({f.Error})"));
        return $"{baseDescription} with {failedItems.Count} error(s): {details}";
    }
}
