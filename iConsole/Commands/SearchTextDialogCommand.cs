using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Commands;

public class SearchTextDialogCommand(
    ISearchModel searchModel, 
    ITextModel textModel,
    IScrollableModel scrollableModel,
    IBeepService beepService, 
    IDateTimeProviderService dateTimeProviderService,
    IThemeService themeService) 
    : Command
{
    public override string Title => "F7-Search";
    private SearchTextDialog? _searchTextDialog { get; set; } 

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F7, false, false, false);

    public override bool CanExecute()
    {
        return true;
    }

    public override async Task ExecuteAsync(object? arg)
    {
        _searchTextDialog = new SearchTextDialog(
            searchModel, 
            textModel, 
            scrollableModel, 
            beepService, 
            dateTimeProviderService,
            themeService);
        var textFileViewerDialog = Control?.Parent?.Parent;
        if (Control is not null)
        {
            await Control.ShowDialogAsync(_searchTextDialog, textFileViewerDialog);
        }
    }
}
