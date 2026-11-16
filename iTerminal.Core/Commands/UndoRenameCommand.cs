using iConsole.Commands;
using iConsole.Data;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Services;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Z on a file panel: puts back the entry the last rename moved. It moves the entry back only when the old name
/// is free and the new name is still there; otherwise it says why, and keeps nothing to undo.
/// </summary>
public class UndoRenameCommand(RenameUndoService renameUndo, INotificationService? notificationService = null) : Command
{
    public override string Title => UiText.Title("Undo rename");
    public override string Description => UiText.Describe("Put back the name the last rename changed.");

    public override Key? Key { get; init; } = new Key(ConsoleKey.Z, false, false, true);

    public override bool CanExecute() => renameUndo.Peek() is not null;

    public override async Task ExecuteAsync(object? args)
    {
        var rename = renameUndo.Take();
        if (rename is null)
        {
            return;
        }

        try
        {
            if (!rename.Model.ExistsFile(rename.Target) && !rename.Model.ExistsFolder(rename.Target))
            {
                Say(UiText.Get("Undo.RenameGone", "The renamed entry is no longer there, so there is nothing to put back."), NotificationSeverity.Warning);
                return;
            }

            if (rename.Model.ExistsFile(rename.Source) || rename.Model.ExistsFolder(rename.Source))
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

    private void Say(string message, NotificationSeverity severity) =>
        notificationService?.Notify(UiText.Get("Undo.RenameTitle", "Undo rename"), message, severity);
}
