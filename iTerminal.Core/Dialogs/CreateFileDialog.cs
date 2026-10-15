using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Controls;
using iTerminal.Core.Model.Validations;

namespace iTerminal.Core.Dialogs;

/// <summary>Create-file dialog. All behavior lives in <see cref="CreateEntryDialog"/>.</summary>
public class CreateFileDialog : CreateEntryDialog
{
    public string NewFileName => NewEntryName;

    public CreateFileDialog(
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService
        )
        : this(new NewFileTextBox(beepService, dateTimeProviderService, themeService), contentModelService, themeService)
    {
    }

    private CreateFileDialog(NewFileTextBox textBoxName, IContentModelService contentModelService, IThemeService themeService)
        : base(
            nameof(CreateFileDialog),
            "Create file",
            textBoxName,
            new CreateFileCommand(textBoxName.ValidationModel as NewFileValidationModel, contentModelService),
            themeService)
    {
    }
}
