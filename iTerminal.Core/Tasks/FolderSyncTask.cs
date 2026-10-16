using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Helper;
using iTerminal.Core.Services.Compare;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Carries out a <see cref="SyncPlan"/>: the copies and deletions, in the order the comparison listed
/// them (a folder before what is inside it), under one progress window.
///
/// <para>A step that fails does not stop the others; the failures are listed and <see cref="BackgroundTask.Error"/>
/// keeps the window up. Cancel stops the run: what was done stays done (a comparison shows the rest).</para>
///
/// <para><b>A copy keeps the source's date.</b> A later comparison judges by size and date, so a copy
/// stamped "now" would come out newer than the file it was made from and be copied straight back.
/// Within one drive the drive's own copy does that; across drives the date is set on the finished
/// target when that is a local file. Deletions go to the recycle bin where the drive has one.</para>
/// </summary>
public sealed class FolderSyncTask(
    SyncPlan plan,
    Url leftFolder,
    IFileSystem left,
    Url rightFolder,
    IFileSystem right) : TransferTask
{
    private readonly List<string> _failures = [];

    public int Copied { get; private set; }

    public int Deleted { get; private set; }

    public IReadOnlyList<string> Failures => _failures;

    public override async Task ExecuteAsync()
    {
        Subject = "folder synchronisation";
        var actions = plan.Actions;
        var done = 0;
        Description = $"Synchronise{Environment.NewLine}{leftFolder.FullUrl.Shrink(60)}{Environment.NewLine}{rightFolder.FullUrl.Shrink(60)}";
        ReportItems(0, actions.Count);
        ReportBytes(0, actions.Where(a => !a.IsDelete && !a.Item.IsFolder).Sum(a => Math.Max(0, a.SourceSize ?? 0)));

        try
        {
            foreach (var action in actions)
            {
                CancellationToken.ThrowIfCancellationRequested();
                Description = $"Synchronise{Environment.NewLine}{action.Describe().Shrink(70)}";
                try
                {
                    if (action.IsDelete)
                    {
                        Delete(action);
                        Deleted++;
                    }
                    else
                    {
                        await CopyAsync(action);
                        Copied++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _failures.Add($"{action.Item.Path}: {exception.Message}");
                }

                done++;
                ReportItems(done, actions.Count);
                OnProgressChanged(actions.Count == 0 ? 100 : Math.Clamp(100L * done / actions.Count, 0, 100));
            }

            ProgressPercentage = 100;
            Description = Describe();
            if (_failures.Count > 0)
            {
                Error = $"{_failures.Count} step(s) failed.";
            }
        }
        catch (OperationCanceledException)
        {
            Description = $"Synchronise cancelled after {done} of {actions.Count} step(s){Environment.NewLine}{Summarise()}";
        }
    }

    private string Summarise() =>
        $"{Copied} copied, {Deleted} deleted" + (_failures.Count > 0 ? $", {_failures.Count} failed" : string.Empty);

    private string Describe()
    {
        var text = $"Synchronised: {Summarise()}.";
        foreach (var failure in _failures.Take(5))
        {
            text += Environment.NewLine + failure.Shrink(76);
        }

        if (_failures.Count > 5)
        {
            text += Environment.NewLine + $"… and {_failures.Count - 5} more";
        }

        return text;
    }

    private void Delete(SyncAction action)
    {
        var victim = action.Victim!;
        var fileSystem = action.Kind == SyncActionKind.DeleteLeft ? left : right;
        if (action.Item.IsFolder)
        {
            fileSystem.DeleteFolder(victim, recursive: true, moveToRecycleBin: true);
        }
        else
        {
            fileSystem.DeleteFile(victim, moveToRecycleBin: true);
        }
    }

    private async Task CopyAsync(SyncAction action)
    {
        var toRight = action.CopiesToRight;
        var sourceSystem = toRight ? left : right;
        var targetSystem = toRight ? right : left;
        var targetFolder = toRight ? rightFolder : leftFolder;
        var source = action.Source!;
        var isFolder = action.Item.IsFolder;
        var target = new Url(UrlPath.Child(targetFolder.FullUrl, action.Item.Path, isFolder));

        if (isFolder)
        {
            await CopyFolderAsync(sourceSystem, source, targetSystem, target);
        }
        else
        {
            await CopyFileAsync(sourceSystem, source, targetSystem, target, action.SourceTime);
        }
    }

    private async Task CopyFolderAsync(IFileSystem sourceSystem, Url source, IFileSystem targetSystem, Url target)
    {
        CancellationToken.ThrowIfCancellationRequested();
        if (!targetSystem.ExistsFolder(target))
        {
            await targetSystem.CreateFolderAsync(target);
        }

        foreach (var child in ContentModelFolderTaskHelper.ListChildren(sourceSystem, source).ToList())
        {
            var name = FocusedEntry.NameOf(child);
            if (child.PathType == PathType.Folder)
            {
                await CopyFolderAsync(sourceSystem, child, targetSystem, new Url(UrlPath.Child(target.FullUrl, name, isFolder: true)));
            }
            else
            {
                await CopyFileAsync(sourceSystem, child, targetSystem, new Url(UrlPath.Child(target.FullUrl, name, isFolder: false)), null);
            }
        }
    }

    private async Task CopyFileAsync(IFileSystem sourceSystem, Url source, IFileSystem targetSystem, Url target, DateTime? sourceTime)
    {
        CancellationToken.ThrowIfCancellationRequested();
        var size = Math.Max(0, source.Length);
        if (ReferenceEquals(sourceSystem, targetSystem))
        {
            await targetSystem.CopyAsync(source, target, overwrite: true);
        }
        else
        {
            var temp = Path.Combine(Path.GetTempPath(), "iTerminal-sync-" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                await using (var download = new FileStream(temp, FileMode.Create, FileAccess.Write))
                {
                    await sourceSystem.StreamToAsync(source, download, null, CancellationToken);
                }

                await using var upload = new FileStream(temp, FileMode.Open, FileAccess.Read);
                await targetSystem.StreamFromAsync(target, upload, null, CancellationToken);
            }
            finally
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                }
            }

            KeepDate(source, target, sourceTime);
        }

        AddBytesProcessed(size);
    }

    /// <summary>Gives a finished local copy the date of the file it was made from, so the next comparison calls the two the same.</summary>
    private static void KeepDate(Url source, Url target, DateTime? sourceTime)
    {
        try
        {
            if (target.ContainerType != ContainerType.FileSystem)
            {
                return;
            }

            DateTime? time = sourceTime;
            if (time is null && source.ContainerType == ContainerType.FileSystem && File.Exists(source.FullName))
            {
                time = File.GetLastWriteTime(source.FullName);
            }

            if (time is { } value && File.Exists(target.FullName))
            {
                File.SetLastWriteTime(target.FullName, value);
            }
        }
        catch
        {
            // The copy is there; only its date is wrong, and the next comparison will say so.
        }
    }
}
