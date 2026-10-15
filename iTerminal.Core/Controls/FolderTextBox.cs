using iConsole.Controls;
using iConsole.Services;
using iTerminal.Core.Model.Validations;

namespace iTerminal.Core.Controls;

public class FolderTextBox : TextBox
{
    public FolderTextBox(IBeepService beepService, IDateTimeProviderService dateTimeProviderService, IThemeService themeService) 
        : base(beepService, dateTimeProviderService, themeService)
    {
        ValidationModel = new FolderValidationModel(TextModel, dateTimeProviderService);
    }
}
