using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Tasks;
using System.Threading.Channels;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Walks a source folder tree (via <see cref="ContentModelFolderTaskHelper.ListChildren"/>),
/// creating the corresponding target folders as it goes, and streams discovered files into a
/// bounded channel for a separate processing task (<see cref="ContentModelCopyFolderTask"/> /
/// <see cref="ContentModelMoveFolderTask"/>) to drain concurrently. Runs as its own
/// <see cref="BackgroundTask"/> — visible in the task manager alongside the processing task it
/// feeds — rather than as an internal phase of a single task.
/// </summary>
public class FolderDiscoveryTask(IFileSystem source, IFileSystem target, Url sourceUrl, Url folderTargetUrl) : TransferTask
{
    private readonly Channel<(Url Source, Url Target)> _channel = Channel.CreateBounded<(Url Source, Url Target)>(new BoundedChannelOptions(64)
    {
        SingleWriter = true,
        SingleReader = false,
    });

    private long _discovered;

    public ChannelReader<(Url Source, Url Target)> Reader => _channel.Reader;

    public override async Task ExecuteAsync()
    {
        try
        {
            Description = $"Discovering…{Environment.NewLine}{sourceUrl.FullName.Shrink(60)}";
            await DiscoverAsync(sourceUrl, folderTargetUrl, CancellationToken);
            _channel.Writer.Complete();
            Description = $"Discovered {_discovered} file(s){Environment.NewLine}{sourceUrl.FullName.Shrink(60)}";
            ProgressPercentage = 100;
        }
        catch (OperationCanceledException)
        {
            _channel.Writer.TryComplete();
            Description = $"Discovery of{Environment.NewLine}{sourceUrl.FullName.Shrink(60)}{Environment.NewLine}cancelled";
        }
        catch (Exception ex)
        {
            _channel.Writer.TryComplete(ex);
            Description = $"Discovery failed: {ex.Message}";
        }
    }

    private async Task DiscoverAsync(Url currentSourceUrl, Url currentTargetUrl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await target.CreateFolderAsync(currentTargetUrl);

        foreach (var childUrl in ContentModelFolderTaskHelper.ListChildren(source, currentSourceUrl))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var childTargetUrl = currentTargetUrl.Combine(Commands.FocusedEntry.NameOf(childUrl) + (childUrl.PathType == PathType.Folder ? "/" : string.Empty));

            if (childUrl.PathType == PathType.Folder)
            {
                // A link is copied as what it points to, like everywhere else — except one that
                // leads back to a folder it is inside, which would be copied into itself for ever.
                if (childUrl.ContainerType == ContainerType.FileSystem
                    && childUrl.IsLink
                    && iConsole.Helper.FileLinkHelper.LeadsToAnAncestor(childUrl.FullName))
                {
                    continue;
                }

                await DiscoverAsync(childUrl, childTargetUrl, cancellationToken);
            }
            else
            {
                _discovered++;
                ReportItems(_discovered, _discovered);
                if (childUrl.Length > 0)
                {
                    AddTotalBytes(childUrl.Length);
                }
                Description = $"Discovering…{Environment.NewLine}{_discovered} file(s) found so far";
                await _channel.Writer.WriteAsync((childUrl, childTargetUrl), cancellationToken);
            }
        }
    }
}
