using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Layouts;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Controls.StatusBar;

public class ViewerStatusBar : StatusBar
{
    private Button _findNextButton { get; set; }
    private Button _findPreviousButton { get; set; }
    private Button _searchTextDialogButton { get; set; }
    private EncodingIndicator _encodingIndicator { get; set; }
    private CursorPositionIndicator _cursorPositionIndicator { get; set; }
    private SearchResultsIndicator _searchResultsIndicator { get; set; }

    public ViewerStatusBar(
        IScrollableModel scrollableModel,
        ISearchModel searchModel,
        ITextModel textModel,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(themeService)
    {
        Name = nameof(ViewerStatusBar);
        ViewSize = new Size(0, 1);

        _searchTextDialogButton = new Button(
            new SearchTextDialogCommand(searchModel, textModel, scrollableModel, beepService, dateTimeProviderService, themeService), themeService);
        _findNextButton = new Button(
            new FindNextCommand(searchModel, scrollableModel), themeService);
        _findPreviousButton = new Button(
            new FindPreviousCommand(searchModel, scrollableModel), themeService);
        _encodingIndicator = new EncodingIndicator(textModel, themeService);
        _cursorPositionIndicator = new CursorPositionIndicator(scrollableModel, themeService);
        _searchResultsIndicator = new SearchResultsIndicator(searchModel, themeService);
        AddControl(_findNextButton);
        AddControl(_findPreviousButton);
        AddControl(_searchTextDialogButton);
        AddControl(_encodingIndicator);
        AddControl(_cursorPositionIndicator);
        AddControl(_searchResultsIndicator);
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            ViewSize = new Size(Parent.ViewSize.Width - 2, 1);
        }
        base.OnInitializeLayout();

        _findNextButton.ViewSize = new Size(13, 1);
        _findPreviousButton.ViewSize = new Size(18, 1);
        _searchTextDialogButton.ViewSize = new Size(11, 1);
        _encodingIndicator.ViewSize = new Size(10, 1);
        _cursorPositionIndicator.ViewSize = new Size(20, 1);
        _searchResultsIndicator.ViewSize = new Size(20, 1);

        StackFitting(
            [
                _findNextButton,
                _findPreviousButton,
                _searchTextDialogButton,
                _encodingIndicator,
                _cursorPositionIndicator,
                _searchResultsIndicator
            ],
            [
                _cursorPositionIndicator,
                _encodingIndicator,
                _searchTextDialogButton,
                _findNextButton,
                _searchResultsIndicator,
                _findPreviousButton
            ]);
    }
}
