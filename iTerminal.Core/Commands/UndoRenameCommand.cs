using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Services;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Z on a file panel: puts back the names the last rename, Multi-rename run or move changed. An entry is
/// moved back only when the old name is free and the new name is still there; otherwise it says why, and keeps
/// nothing to undo.
/// </summary>
public class UndoRenameCommand(
    RenameUndoService renameUndo,
    INotificationService? notificationService = null,
    IContentModelService? contentModelService = null) : Command
{
    public override string Title => UiText.Title("Undo rename or move");
    public override string Description => UiText.Describe("Put back the names the last rename, multi-rename or move changed.");

    public override Key? Key { get; init; } = new Key(ConsoleKey.Z, false, false, true);

    /// <summary>
    /// With a content model service (the Edit menu's entry, which is bound globally) it also needs a file panel in focus, so Ctrl+Z
    /// typed into a dialog or a text box does not undo a rename. The panel's own copy of the command passes none: it only runs there.
    /// </summary>
    public override bool CanExecute() =>
        renameUndo.Peek() is not null && (contentModelService is null || contentModelService.GetFocusedModel() is not null);

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
                    UiText.Get("Undo.RenameBatchPartial", "Put back {0} of {1} entries. The rest were not put back: the new name is gone or the old name is taken again."),
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
                Say(UiText.Get("Undo.RenameGone", "The entry is no longer there, so there is nothing to put back."), NotificationSeverity.Warning);
                return;
            }

            if (Exists(rename.Model, rename.Source))
            {
                Say(UiText.Get("Undo.RenameNameTaken", "The old name is taken now, so it was not put back."), NotificationSeverity.Warning);
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
        notificationService?.Notify(UiText.Get("Undo.RenameTitle", "Undo rename or move"), message, severity);
}
