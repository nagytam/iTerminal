using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.Validations;

namespace iTerminal.Core.Commands;

public class CreateFolderCommand(
    FolderValidationModel folderValidator,
    IContentModelService contentModelService
    ) : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute()
    {
        if (Control?.Parent is CreateFolderDialog createFolderDialog)
        {
            return folderValidator.EnsureCurrent();
        }
        return base.CanExecute();
    }

    public override async Task ExecuteAsync(object? arg)
    {
        if (Control?.Parent is CreateFolderDialog createFolderDialog)
        {
            var newUrl = createFolderDialog.BaseUrl.Combine(createFolderDialog.NewFolderName);
            if (!newUrl.FullUrl.EndsWith('/'))
            {
                newUrl += '/';
            }

            var contentModel = contentModelService.GetLastContentModel();
            if (contentModel is null)
            {
                createFolderDialog.ReportFailure("There is no folder to create the folder in.");
                return;
            }

            try
            {
                await contentModel.CreateFolderAsync(newUrl);
                contentModel.Update();
            }
            catch (Exception exception)
            {
                createFolderDialog.ReportFailure($"Could not create the folder: {exception.Message}");
                return;
            }

            createFolderDialog.Close();
            createFolderDialog.Reset();
        }
    }
}
