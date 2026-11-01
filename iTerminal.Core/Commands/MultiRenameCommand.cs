using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Factories;
using iTerminal.Core.Helper;
using iTerminal.Core.Rename;
using iTerminal.Core.Services;
using iTerminal.Core.Tasks;

namespace iTerminal.Core.Commands;

/// <summary>
/// Enter in <see cref="MultiRenameDialog"/>: runs every row the planner marked
/// <see cref="RenameStatus.Ok"/> as one <see cref="BatchOperationTask"/>.
///
/// <para>Each step is a move to a new name in the same folder, so source and target share one
/// <see cref="IFileSystem"/>. The conflict resolver is <see cref="FileConflictMode.Legacy"/>: a target
/// that exists when the step runs (it appeared after planning) is an error for that step, never an
/// overwrite and never a prompt. Rows that are blocked, duplicated or taken are not submitted.</para>
/// </summary>
public class MultiRenameCommand(
    TaskManager taskManager,
    ProgressWindowFactory progressWindowFactory,
    IErrorPromptService errorPromptService,
    IConflictPromptService conflictPromptService,
    FileSystemResolver fileSystemResolver,
    IContentModelService contentModelService)
    : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute() =>
        Control?.Parent is MultiRenameDialog dialog && dialog.OkCount > 0;

    public override async Task ExecuteAsync(object? args)
    {
        if (Control?.Parent is not MultiRenameDialog dialog || dialog.OkCount == 0 || dialog.FolderUrl is not { } folderUrl)
        {
            return;
        }

        var resolver = new FileConflictResolver(FileConflictMode.Legacy, conflictPromptService);
        var steps = new List<BatchStep>();
        foreach (var row in dialog.Plan.Where(row => row.Status == RenameStatus.Ok))
        {
            var sourceUrl = dialog.SourceUrls[row.Index];
            var isFolder = sourceUrl.PathType == PathType.Folder;
            var targetUrl = new Url(UrlPath.Child(folderUrl.FullUrl, row.NewName, isFolder));
            var fileSystem = fileSystemResolver.Resolve(sourceUrl, operation: UiText.Get("Menu.Rename", "Rename"));
            BackgroundTask task = isFolder
                ? new ContentModelMoveFolderTask(fileSystem, fileSystem, sourceUrl, targetUrl, overwrite: false, discoveryTask: null, errorPromptService)
                : new ContentModelMoveFileTask(fileSystem, fileSystem, sourceUrl, targetUrl, resolver, errorPromptService);
            steps.Add(new BatchStep(row.OldName, task));
        }

        try
        {
            var batch = new BatchOperationTask(UiText.Get("Menu.Rename", "Rename"), steps);
            resolver.CancelRun = () => batch.CancellationTokenSource.Cancel();
            PanelRefreshHelper.RefreshWhenFinished(batch, contentModelService, folderUrl);

            // The marks described what to rename; left standing they would rename the same rows again.
            TransferCommand.ConsumeMarks(dialog.SourceModel);
            await taskManager.Add(batch);
            await dialog.ShowPopUpWindowAsync(progressWindowFactory.Create(batch));

            dialog.Close();
            dialog.Reset();
        }
        catch (Exception exception)
        {
            dialog.ErrorMessage.Text = exception.Message;
        }
    }
}
