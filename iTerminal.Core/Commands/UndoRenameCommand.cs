using iConsole.Commands;
using iConsole.Data;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Services;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Z on a file panel: puts back the names the last rename, or the last Multi-rename run, changed. An entry is
/// moved back only when the old name is free and the new name is still there; otherwise it says why, and keeps
/// nothing to undo.
/// </summary>
public class UndoRenameCommand(RenameUndoService renameUndo, INotificationService? notificationService = null) : Command
{
    public override string Title => UiText.Title("Undo rename");
    public override string Description => UiText.Describe("Put back the name the last rename changed.");

    public override Key? Key { get; init; } = new Key(ConsoleKey.Z, false, false, true);

    public override bool CanExecute() => renameUndo.Peek() is not null;

    public override async Task ExecuteAsync(object? args)
    {
        var renames = renameUndo.TakeAll();
        if (renames.Count == 0)
        {
            return;
        }

        if (renames.Count == 1)
        {
            await UndoOneAsync(renames[0]);
            return;
        }

        // A batch goes back in the reverse of the order it ran, and each entry is checked on its own: one that
        // cannot be put back must not stop the others.
        int putBack = 0;
        foreach (var rename in renames.Reverse())
        {
            try
            {
                if (IsSafeToUndo(rename) && await MoveBackAsync(rename))
                {
                    putBack++;
                }
            }
            catch (Exception exception)
            {
                Say(exception.Message, NotificationSeverity.Error);
            }
        }

        renames[0].Model.Refresh();
        if (putBack < renames.Count)
        {
            Say(
                string.Format(
                    UiText.Get("Undo.RenameBatchPartial", "Put back {0} of {1} renames. The rest were not put back: the new name is gone or the old name is taken again."),
                    putBack,
                    renames.Count),
                NotificationSeverity.Warning);
        }
    }

    private async Task UndoOneAsync(RenameUndoService.Rename rename)
    {
        try
        {
            if (!Exists(rename.Model, rename.Target))
            {
                Say(UiText.Get("Undo.RenameGone", "The renamed entry is no longer there, so there is nothing to put back."), NotificationSeverity.Warning);
                return;
            }

            if (Exists(rename.Model, rename.Source))
            {
                Say(UiText.Get("Undo.RenameNameTaken", "The old name is taken now, so the rename was not undone."), NotificationSeverity.Warning);
                return;
            }

            await rename.Model.MoveAsync(rename.Target, rename.Source, overwrite: false);
            rename.Model.Refresh();
        }
        catch (Exception exception)
        {
            Say(exception.Message, NotificationSeverity.Error);
        }
    }

    private static bool IsSafeToUndo(RenameUndoService.Rename rename) =>
        Exists(rename.Model, rename.Target) && !Exists(rename.Model, rename.Source);

    private static async Task<bool> MoveBackAsync(RenameUndoService.Rename rename)
    {
        await rename.Model.MoveAsync(rename.Target, rename.Source, overwrite: false);
        return true;
    }

    private static bool Exists(iConsole.Interfaces.IContentModel model, Url url) =>
        model.ExistsFile(url) || model.ExistsFolder(url);

    private void Say(string message, NotificationSeverity severity) =>
        notificationService?.Notify(UiText.Get("Undo.RenameTitle", "Undo rename"), message, severity);
}
