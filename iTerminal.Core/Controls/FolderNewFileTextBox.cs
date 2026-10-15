using iConsole.Controls;
using iConsole.Services;
using iTerminal.Core.Model.Validations;

namespace iTerminal.Core.Controls;

public class FolderOrNewFileTextBox : TextBox
{
    public FolderOrNewFileTextBox(IBeepService beepService, IDateTimeProviderService dateTimeProviderService, IThemeService themeService) 
        : base(beepService, dateTimeProviderService, themeService)
    {
        ValidationModel = new FolderOrNewFilesValidationModel(TextModel, dateTimeProviderService);
    }
}
