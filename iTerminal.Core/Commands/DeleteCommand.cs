using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Factories;
using iTerminal.Core.Helper;
using iTerminal.Core.Services;
using iTerminal.Core.Tasks;

namespace iTerminal.Core.Commands;

public class DeleteCommand(
    IContentModelService contentModelService,
    TaskManager taskManager,
    ProgressWindowFactory progressWindowFactory,
    IErrorPromptService errorPromptService,
    FileSystemResolver fileSystemResolver)
    : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override async Task ExecuteAsync(object? args)
    {
        if (Control?.Parent is DeleteDialog deleteDialog)
        {
            // A delete is only meaningful with a panel behind it: the dialog's Url is whatever that
            // panel's cursor was on. Without one Enter used to do nothing at all, with the dialog left
            // open and no word why.
            if (contentModelService.GetLastContentModel() is null)
            {
                deleteDialog.ErrorMessage.Text = "There is no panel to delete from.";
                return;
            }

            if (deleteDialog.IsMultiple)
            {
                try
                {
                    await RunBatchAsync(deleteDialog);
                    deleteDialog.Close();
                    deleteDialog.Reset();
                }
                catch (Exception exception)
                {
                    deleteDialog.ErrorMessage.Text = exception.Message;
                }
            }
            else
            {
                var deleteUrl = new Url(deleteDialog.Url);
                var moveToRecycleBin = deleteDialog.MoveToRecycleBin;
                var recursive = deleteDialog.Recursive;

                try
                {
                    var target = ResolveFileSystem(deleteUrl);
                    var (discoveryTask, task) = CreateTasks(target, deleteUrl, recursive, moveToRecycleBin);

                    // Re-read the folder rather than moving the cursor up a row and calling it
                    // deleted: the row above is not where the cursor belongs (the entry that took
                    // the deleted one's place is), and the guess was made even when the delete
                    // failed. Update() clamps the cursor against the shorter listing on its own.
                    // Subscribed before the task is queued — a single small file is regularly gone
                    // before Add returns.
                    PanelRefreshHelper.RefreshWhenFinished(task, contentModelService, deleteUrl);

                    if (discoveryTask is not null)
                    {
                        await taskManager.Add(discoveryTask);
                    }
                    await taskManager.Add(task);
                    var progressWindow = progressWindowFactory.Create(task);
                    await deleteDialog.ShowPopUpWindowAsync(progressWindow);

                    TransferCommand.ConsumeMarks(deleteDialog.SourceModel);
                    deleteDialog.Close();
                    deleteDialog.Reset();
                }
                catch (Exception exception)
                {
                    deleteDialog.ErrorMessage.Text = exception.Message;
                }
            }
        }
    }

    /// <summary>
    /// The marked rows, one delete step each, run in order as one <see cref="BatchOperationTask"/>.
    /// A folder is deleted recursively only when the box says so; otherwise only an empty one goes,
    /// and the rest are listed as failures rather than stopping the files after them.
    /// </summary>
    private async Task RunBatchAsync(DeleteDialog deleteDialog)
    {
        var moveToRecycleBin = deleteDialog.MoveToRecycleBin;
        var recursive = deleteDialog.Recursive;
        var steps = new List<BatchStep>();
        foreach (var url in deleteDialog.Urls)
        {
            var target = ResolveFileSystem(url);
            var (discoveryTask, task) = CreateTasks(target, url, recursive && url.PathType == PathType.Folder, moveToRecycleBin);
            steps.Add(new BatchStep(FocusedEntry.NameOf(url), task, discoveryTask));
        }

        var batch = new BatchOperationTask("Delete", steps);
        PanelRefreshHelper.RefreshWhenFinished(batch, contentModelService, deleteDialog.Urls[0]);
        TransferCommand.ConsumeMarks(deleteDialog.SourceModel);

        await taskManager.Add(batch);
        var progressWindow = progressWindowFactory.Create(batch);
        await deleteDialog.ShowPopUpWindowAsync(progressWindow);
    }

    private (FolderDeleteDiscoveryTask? DiscoveryTask, ContentModelDeleteTask PrimaryTask) CreateTasks(IFileSystem target, Url deleteUrl, bool recursive, bool moveToRecycleBin)
    {
        if (deleteUrl.PathType != PathType.Folder || !recursive)
        {
            return (null, new ContentModelDeleteTask(target, deleteUrl, recursive, moveToRecycleBin, discoveryTask: null, errorPromptService));
        }

        var discoveryTask = new FolderDeleteDiscoveryTask(target, deleteUrl);
        var primaryTask = new ContentModelDeleteTask(target, deleteUrl, recursive, moveToRecycleBin, discoveryTask, errorPromptService);
        return (discoveryTask, primaryTask);
    }

    private IFileSystem ResolveFileSystem(Url url) => fileSystemResolver.Resolve(url, operation: "Delete");
}
