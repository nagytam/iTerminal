using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Recycle;
using Serilog;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// A panel on the <c>recycle://</c> drive (Ctrl+F8): what was deleted, most recently first, each row
/// dated by when. Enter puts the row under the cursor — or the marked rows — back where they were
/// deleted from; F8 deletes them for good, through the same dialog as anywhere else, which is also
/// how the bin is emptied (mark all, F8). F3, quick view and F5 read a deleted file where it lies.
/// </summary>
public class ContentModelRecycleDrive(
    string name,
    ISettingsService? settingsService,
    IRecycleBinService recycleBinService,
    FolderHistoryService? folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    ILogger? logger = null)
    : ContentModelAsyncDrive(name, settingsService, folderHistoryService, frequentExecutionGuardService, logger)
{
    protected override string Scheme => Container.RECYCLE;

    public override ContainerType ContainerType => ContainerType.Recycle;

    public override IFileSystem FileSystem => recycleBinService;

    /// <summary>The restore started last, including the listing after it. Nothing waits on it; it exists so a test can.</summary>
    public Task RestoreTask { get; private set; } = Task.CompletedTask;

    protected override Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken) =>
        recycleBinService.ListAsync(cancellationToken);

    protected override string DescribeLocation() => "Recycle bin";

    /// <summary>The drive is one flat list: wherever a URL on it points, <c>..</c> leaves the drive.</summary>
    protected override bool IsDriveRoot(Url url) => true;

    public override string GetFooter()
    {
        // The two keys that mean something else here than on a folder are named: no row says them.
        var footer = base.GetFooter();
        return StatusMessage.Length == 0 ? $"{footer}│ Enter: restore  F8: delete for good " : footer;
    }

    /// <summary>
    /// What Enter restores: the marked rows when there are any, otherwise the row under the cursor —
    /// never the <c>..</c> row, where Enter still means "leave the drive".
    /// </summary>
    public IReadOnlyList<Url> RestoreTargets()
    {
        var marked = GetMarkedEntries();
        if (marked.Count > 0)
        {
            return [.. marked.Select(entry => entry.Url)];
        }

        return GetFocusedEntry() is RecycledEntry entry ? [entry.Url] : [];
    }

    /// <summary>
    /// Restores <paramref name="urls"/> on a background task and lists the bin again. The key handler
    /// only starts it. It asks nothing first: a restore is undone by deleting the file again, and it
    /// never overwrites — a file that has taken the deleted one's place is reported and left alone.
    /// </summary>
    public void Restore(IReadOnlyList<Url> urls)
    {
        if (urls.Count == 0)
        {
            return;
        }

        var url = Url;
        StatusMessage = urls.Count == 1 ? $"Restoring {urls[0].Name}…" : $"Restoring {urls.Count} items…";
        ClearMarks();
        Update();

        RestoreTask = Task.Run(async () =>
        {
            string outcome;
            try
            {
                var result = await recycleBinService.RestoreAsync(urls);
                outcome = Describe(urls, result);
            }
            catch (Exception exception)
            {
                outcome = exception.Message;
            }

            if (!ReferenceEquals(Url, url))
            {
                return;
            }

            // The listing clears the status as it lands, so the outcome is written after it.
            Refresh(forceRefresh: true);
            await ListingTask;
            if (ReferenceEquals(Url, url))
            {
                StatusMessage = outcome;
                Update();
            }
        });
    }

    internal static string Describe(IReadOnlyList<Url> urls, RestoreOutcome result)
    {
        if (result.Failures.Count == 0)
        {
            return urls.Count == 1 ? $"Restored {urls[0].Name}" : $"Restored {result.Restored} items";
        }

        // One failure is said in full; several are counted, with the first as the example.
        return urls.Count == 1
            ? result.Failures[0]
            : $"Restored {result.Restored} of {urls.Count} — {result.Failures[0]}";
    }
}
