using iConsole.Localization;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;

namespace iTerminal.Core.Commands;

public class RenameCommand(IContentModelService contentModelService, RenameUndoService? renameUndo = null) 
    : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override async Task ExecuteAsync(object? args)
    {
        if (Control?.Parent is RenameDialog renameDialog)
        {
            if (string.IsNullOrWhiteSpace(renameDialog.NewName))
            {
                renameDialog.ErrorMessage.Text = UiText.Get("Msg.TypeNewName", "Type the new name.");
                return;
            }

            // Compared without trailing separators: a folder is listed with one and a name typed in the box has none.
            if (string.Equals(
                    Helper.UrlPath.TrimFolder(renameDialog.TargetUrl.FullUrl),
                    Helper.UrlPath.TrimFolder(renameDialog.SourceUrl?.FullUrl ?? string.Empty),
                    StringComparison.Ordinal))
            {
                // Unchanged: nothing to do, and a move onto itself would only produce an error.
                renameDialog.Close();
                renameDialog.Reset();
                return;
            }

            var overwrite = renameDialog.Overwrite;
            var contentModel = contentModelService.GetLastContentModel();
            if (contentModel != null)
            {
                var sourceUrl = renameDialog.SourceUrl;
                var targetUrl = renameDialog.TargetUrl;

                try
                {
                    await contentModel.MoveAsync(sourceUrl!, targetUrl, overwrite);

                    renameUndo?.Remember(contentModel, sourceUrl!, targetUrl);

                    renameDialog.Refresh();
                    renameDialog.Close();
                    renameDialog.Reset();
                }
                catch (Exception exception)
                {
                    // An existing name with Overwrite unticked is the one failure the dialog can say something useful about: the .NET text
                    // ("Cannot create a file when that file already exists.") names no file and does not mention the box that would fix it.
                    renameDialog.ErrorMessage.Text = !overwrite && TargetExists(contentModel, targetUrl)
                        ? string.Format(
                            UiText.Get("Msg.RenameTargetExists", "'{0}' already exists. Tick \"{1}\" to replace it."),
                            targetUrl.Name,
                            UiText.Get("Label.Overwrite", "Overwrite"))
                        : exception.Message;
                }
            }
        }
    }

    private static bool TargetExists(iConsole.Interfaces.IContentModel contentModel, Url target)
    {
        try
        {
            return contentModel.ExistsFile(target) || contentModel.ExistsFolder(target);
        }
        catch
        {
            return false;
        }
    }
}
