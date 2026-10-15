using iConsole.Controls;
using iConsole.Services;
using iTerminal.Core.Model.Validations;

namespace iTerminal.Core.Controls;

public class NewFileTextBox : TextBox
{
    public NewFileTextBox(IBeepService beepService, IDateTimeProviderService dateTimeProviderService, IThemeService themeService) 
        : base(beepService, dateTimeProviderService, themeService)
    {
        ValidationModel = new NewFileValidationModel(TextModel, dateTimeProviderService);
    }
}
