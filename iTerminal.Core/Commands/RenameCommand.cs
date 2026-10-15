using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class RenameCommand(IContentModelService contentModelService) 
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
                renameDialog.ErrorMessage.Text = "Type the new name.";
                return;
            }

            if (string.Equals(renameDialog.TargetUrl.FullUrl, renameDialog.SourceUrl?.FullUrl, StringComparison.Ordinal))
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
                    await contentModel.MoveAsync(sourceUrl, targetUrl, overwrite);

                    renameDialog.Refresh();
                    renameDialog.Close();
                    renameDialog.Reset();
                }
                catch (Exception exception)
                {
                    renameDialog.ErrorMessage.Text = exception.Message;
                }
            }
        }
    }
}
