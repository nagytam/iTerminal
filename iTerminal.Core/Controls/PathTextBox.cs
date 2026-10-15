using iConsole.Controls;
using iConsole.Services;
using iTerminal.Core.Model.Validations;

namespace iTerminal.Core.Controls;

public class PathTextBox : TextBox
{
    public PathTextBox(IBeepService beepService, IDateTimeProviderService dateTimeProviderService, IThemeService themeService) 
        : base(beepService, dateTimeProviderService, themeService)
    {
        ValidationModel = new PathValidationModel(TextModel, dateTimeProviderService);
    }
}
