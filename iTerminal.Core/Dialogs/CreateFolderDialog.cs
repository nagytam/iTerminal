using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Controls;
using iTerminal.Core.Model.Validations;

namespace iTerminal.Core.Dialogs;

/// <summary>Create-folder dialog. All behavior lives in <see cref="CreateEntryDialog"/>.</summary>
public class CreateFolderDialog : CreateEntryDialog
{
    public string NewFolderName => NewEntryName;

    public CreateFolderDialog(
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService
        )
        : this(new FolderTextBox(beepService, dateTimeProviderService, themeService), contentModelService, themeService)
    {
    }

    private CreateFolderDialog(FolderTextBox textBoxName, IContentModelService contentModelService, IThemeService themeService)
        : base(
            nameof(CreateFolderDialog),
            "Create folder",
            textBoxName,
            new CreateFolderCommand(textBoxName.ValidationModel as FolderValidationModel, contentModelService),
            themeService)
    {
    }
}
