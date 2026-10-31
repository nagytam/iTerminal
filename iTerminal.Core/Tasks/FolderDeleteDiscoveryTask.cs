using iConsole.Localization;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Tasks;
using System.Threading.Channels;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Walks a folder tree (via <see cref="ContentModelFolderTaskHelper.ListChildren"/>) to be
/// deleted, streaming discovered files into a bounded channel for a separate processing task
/// (<see cref="ContentModelDeleteTask"/>) to delete concurrently, and recording every folder in
/// visitation order for that task's bottom-up removal pass once every file is gone. Runs as its
/// own <see cref="BackgroundTask"/> — visible in the task manager alongside the processing task it
/// feeds — rather than as an internal phase of a single task.
/// </summary>
public class FolderDeleteDiscoveryTask(IFileSystem target, Url folderUrl) : TransferTask
{
    private readonly Channel<Url> _channel = Channel.CreateBounded<Url>(new BoundedChannelOptions(64)
    {
        SingleWriter = true,
        SingleReader = false,
    });

    private readonly List<Url> _folders = [];
    private long _discovered;

    public ChannelReader<Url> Reader => _channel.Reader;

    /// <summary>Folders in visitation (pre-)order — safe to read only after this task completes.</summary>
    public IReadOnlyList<Url> Folders => _folders;

    public override async Task ExecuteAsync()
    {
        try
        {
            Description = string.Join(Environment.NewLine, UiText.Get("Task.Scanning", "Scanning…"), folderUrl.FullName.Shrink(60));
            await DiscoverAsync(folderUrl, CancellationToken);
            _channel.Writer.Complete();
            Description = string.Join(Environment.NewLine, string.Format(UiText.Get("Task.Scanned", "Scanned {0} file(s)"), _discovered), folderUrl.FullName.Shrink(60));
            ProgressPercentage = 100;
        }
        catch (OperationCanceledException)
        {
            _channel.Writer.TryComplete();
            Description = string.Join(Environment.NewLine, UiText.Get("Task.ScanOf", "Scan of"), folderUrl.FullName.Shrink(60), UiText.Get("Task.Cancelled", "cancelled"));
        }
        catch (Exception ex)
        {
            _channel.Writer.TryComplete(ex);
            Description = string.Format(UiText.Get("Task.ScanFailed", "Scan failed: {0}"), ex.Message);
        }
    }

    /// <summary>
    /// A folder link met on the way down is handed on like a file — one entry to delete — and
    /// <c>DeleteFile</c> removes the link and nothing else.
    /// </summary>
    private static bool IsLocalLink(Url url) =>
        url.ContainerType == ContainerType.FileSystem
        && (url.IsLink || iConsole.Helper.FileLinkHelper.IsLink(url.FullName));

    private async Task DiscoverAsync(Url currentUrl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _folders.Add(currentUrl);

        // The folder asked for is itself a link: there is nothing of its own inside it. What a
        // listing of it shows belongs to the folder it points to, and walking in would delete that
        // folder's files. The link is removed with the folders, by DeleteFolder, which takes only it.
        if (IsLocalLink(currentUrl))
        {
            return;
        }

        foreach (var childUrl in ContentModelFolderTaskHelper.ListChildren(target, currentUrl))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (childUrl.PathType == PathType.Folder && !IsLocalLink(childUrl))
            {
                await DiscoverAsync(childUrl, cancellationToken);
            }
            else
            {
                _discovered++;
                ReportItems(_discovered, _discovered);
                Description = string.Join(Environment.NewLine, UiText.Get("Task.Scanning", "Scanning…"), string.Format(UiText.Get("Task.FoundSoFar", "{0} file(s) found so far"), _discovered));
                await _channel.Writer.WriteAsync(childUrl, cancellationToken);
            }
        }
    }
}
