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

/// <summary>
/// Shared engine for the Copy and Move dialog commands: reads source/target/overwrite from the
/// containing <see cref="TransferDialog"/>, resolves the <see cref="IFileSystem"/> for each end
/// from the URL's <see cref="ContainerType"/>, and enqueues the file or folder transfer task
/// (plus a <see cref="FolderDiscoveryTask"/> for cross-drive folder transfers). Subclasses only
/// supply the operation name and the concrete task types.
/// </summary>
public abstract class TransferCommand(
    TaskManager taskManager,
    ProgressWindowFactory progressWindowFactory,
    IErrorPromptService errorPromptService,
    FileSystemResolver fileSystemResolver,
    IContentModelService contentModelService)
    : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    protected IErrorPromptService ErrorPromptService => errorPromptService;

    /// <summary>Operation name for error messages, e.g. "Copy".</summary>
    protected abstract string OperationName { get; }

    /// <summary>
    /// True when the source is gone once the operation succeeds, which is what decides whether the
    /// source panel has anything new to draw. A copy leaves that folder exactly as it was, and
    /// re-listing it would be a wasted round trip on a remote drive. (A copy back into the source
    /// folder is still covered: source and target then share one parent.)
    /// </summary>
    protected abstract bool RemovesSource { get; }

    protected abstract BackgroundTask CreateFileTask(IFileSystem source, IFileSystem target, Url sourceUrl, Url targetUrl, bool overwrite);

    protected abstract TransferTask CreateFolderTask(IFileSystem source, IFileSystem target, Url sourceUrl, Url targetUrl, bool overwrite, FolderDiscoveryTask? discoveryTask);

    public override async Task ExecuteAsync(object? args)
    {
        if (Control?.Parent is TransferDialog dialog)
        {
            bool isSuccess = false;
            if (dialog.IsMultiple)
            {
                try
                {
                    await RunBatchAsync(dialog);
                    isSuccess = true;
                }
                catch (Exception exception)
                {
                    dialog.ErrorMessage.Text = exception.Message;
                }

                if (isSuccess)
                {
                    dialog.Close();
                    dialog.Reset();
                }

                return;
            }

            try
            {
                var (discoveryTask, task) = CreateTasks(dialog.SourceUrl, dialog.TargetUrl, dialog.Overwrite);
                if (task is not null)
                {
                    // Subscribed before the task is queued: a small local file copy can be finished
                    // before Add returns, and the panels would then never hear about it.
                    PanelRefreshHelper.RefreshWhenFinished(
                        task,
                        contentModelService,
                        RemovesSource ? dialog.SourceUrl : null,
                        dialog.TargetUrl);

                    // The discovery task (if any) is added first so it starts walking the tree
                    // immediately; both are independently visible in the task manager, but only
                    // the primary (transfer) task gets a progress window shown here.
                    if (discoveryTask is not null)
                    {
                        await taskManager.Add(discoveryTask);
                    }
                    await taskManager.Add(task);
                    var progressWindow = progressWindowFactory.Create(task);
                    await dialog.ShowPopUpWindowAsync(progressWindow);
                }
                isSuccess = true;
            }
            catch (Exception exception)
            {
                dialog.ErrorMessage.Text = exception.Message;
            }

            if (isSuccess)
            {
                // A single marked row (the cursor elsewhere) is consumed like several are.
                ConsumeMarks(dialog.SourceModel);
                dialog.Close();
                dialog.Reset();
            }
        }
    }

    /// <summary>
    /// Several marked entries into the folder in the box: one step per entry, run in order as one
    /// <see cref="BatchOperationTask"/> under one progress window. The target folder is created
    /// first when it does not exist — on the task's thread, since asking a remote drive is a round
    /// trip this key handler cannot afford. The marks are consumed: they described what to copy,
    /// and left standing they would quietly make the next F5 copy the same rows again.
    /// </summary>
    private async Task RunBatchAsync(TransferDialog dialog)
    {
        var targetFolder = dialog.TargetUrl;
        var steps = new List<BatchStep>();
        foreach (var sourceUrl in dialog.SourceUrls)
        {
            var targetUrl = dialog.TargetUrlFor(sourceUrl);
            var (discoveryTask, task) = CreateTasks(sourceUrl, targetUrl, dialog.Overwrite);
            if (task is not null)
            {
                steps.Add(new BatchStep(FocusedEntry.NameOf(sourceUrl), task, discoveryTask));
            }
        }

        if (steps.Count == 0)
        {
            return;
        }

        var target = ResolveFileSystem(targetFolder);
        var batch = new BatchOperationTask(OperationName, steps, async () =>
        {
            var folderUrl = targetFolder.FullUrl.EndsWith('/') || targetFolder.FullUrl.EndsWith('\\')
                ? targetFolder
                : new Url(targetFolder.FullUrl + "/");
            if (!target.ExistsFolder(folderUrl))
            {
                await target.CreateFolderAsync(folderUrl);
            }
        });

        PanelRefreshHelper.RefreshWhenFinished(
            batch,
            contentModelService,
            RemovesSource ? dialog.SourceUrls[0] : null,
            dialog.TargetUrlFor(dialog.SourceUrls[0]));

        ConsumeMarks(dialog.SourceModel);

        await taskManager.Add(batch);
        var progressWindow = progressWindowFactory.Create(batch);
        await dialog.ShowPopUpWindowAsync(progressWindow);
    }

    internal static void ConsumeMarks(IContentModel? model)
    {
        if (model is Model.ContentModels.IMarkableContentModel { MarkedCount: > 0 } marks)
        {
            marks.ClearMarks();
            model.Update();
        }
    }

    private (FolderDiscoveryTask? DiscoveryTask, BackgroundTask? PrimaryTask) CreateTasks(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        // Streams via IFileSystem.StreamToAsync/StreamFromAsync for every drive-type pair (using
        // the drive's native CopyAsync/MoveAsync as a fast path when source and target are the
        // same IFileSystem instance), so there's no need to special-case local-to-local here.
        var source = ResolveFileSystem(sourceUrl);
        var target = ResolveFileSystem(targetUrl);

        if (sourceUrl.PathType == PathType.File)
        {
            return (null, CreateFileTask(source, target, sourceUrl, targetUrl, overwrite));
        }

        if (sourceUrl.PathType != PathType.Folder)
        {
            return (null, null);
        }

        if (ReferenceEquals(source, target))
        {
            // One atomic native call — nothing meaningful to discover separately.
            return (null, CreateFolderTask(source, target, sourceUrl, targetUrl, overwrite, discoveryTask: null));
        }

        var folderTargetUrl = targetUrl.FullUrl.EndsWith('/') || targetUrl.FullUrl.EndsWith('\\')
            ? targetUrl
            : new Url(targetUrl.FullUrl + "/");
        var discoveryTask = new FolderDiscoveryTask(source, target, sourceUrl, folderTargetUrl);
        var primaryTask = CreateFolderTask(source, target, sourceUrl, targetUrl, overwrite, discoveryTask);
        return (discoveryTask, primaryTask);
    }

    private IFileSystem ResolveFileSystem(Url url) => fileSystemResolver.Resolve(url, operation: OperationName);
}
