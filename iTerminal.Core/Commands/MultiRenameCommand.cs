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
    IContentModelService contentModelService,
    RenameUndoService? renameUndo = null)
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
        var renames = new List<(Url Source, Url Target)>();
        foreach (var row in dialog.Plan.Where(row => row.Status == RenameStatus.Ok))
        {
            var sourceUrl = dialog.SourceUrls[row.Index];
            var isFolder = sourceUrl.PathType == PathType.Folder;
            var targetUrl = new Url(UrlPath.Child(folderUrl.FullUrl, row.NewName, isFolder));
            // The panel is passed: a per-connection drive (S3, OneDrive, MTP) must use this panel's own connection.
            var fileSystem = fileSystemResolver.Resolve(sourceUrl, dialog.SourceModel, UiText.Get("Menu.Rename", "Rename"));
            BackgroundTask task = isFolder
                ? new ContentModelMoveFolderTask(fileSystem, fileSystem, sourceUrl, targetUrl, overwrite: false, discoveryTask: null, errorPromptService)
                : new ContentModelMoveFileTask(fileSystem, fileSystem, sourceUrl, targetUrl, resolver, errorPromptService);
            steps.Add(new BatchStep(row.OldName, task));
            renames.Add((sourceUrl, targetUrl));
        }

        try
        {
            var batch = new BatchOperationTask(UiText.Get("Menu.Rename", "Rename"), steps);
            resolver.CancelRun = () => batch.CancellationTokenSource.Cancel();
            PanelRefreshHelper.RefreshWhenFinished(batch, contentModelService, folderUrl);
            RememberForUndoWhenFinished(batch, dialog.SourceModel, renames);

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

    /// <summary>
    /// Once the batch has ended (finished, failed or cancelled), hands the planned pairs to Ctrl+Z as one step. Pairs whose
    /// step never ran are harmless: the undo moves an entry back only if its new name is there and its old name is free.
    /// Subscribed before the batch is handed to the task manager, because a short run can end before <c>Add</c> returns.
    /// </summary>
    private void RememberForUndoWhenFinished(BatchOperationTask batch, iConsole.Interfaces.IContentModel? model, IReadOnlyList<(Url Source, Url Target)> renames)
    {
        if (renameUndo is null || model is null || renames.Count == 0)
        {
            return;
        }

        void OnBatchChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BackgroundTask.Status) || batch.Status == iConsole.Data.TaskStatus.Started)
            {
                return;
            }

            batch.PropertyChanged -= OnBatchChanged;
            renameUndo.RememberBatch(model, renames);
        }

        batch.PropertyChanged += OnBatchChanged;
    }
}
