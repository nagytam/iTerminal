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
            var isSuccess = false;
            var newUrl = createFolderDialog.BaseUrl.Combine(createFolderDialog.NewFolderName);
            if (!newUrl.FullUrl.EndsWith('/'))
            {
                newUrl += '/';
            }
            try
            {
                var contentModel = contentModelService.GetLastContentModel();
                if (contentModel is not null)
                {
                    await contentModel.CreateFolderAsync(newUrl);
                    contentModel.Update();
                    isSuccess = true;
                }
            }
            catch (Exception) 
            {
                // TODO handle error
            }
            if (isSuccess)
            {
                createFolderDialog.Close();
                createFolderDialog.Reset();
            }
        }
    }
}
