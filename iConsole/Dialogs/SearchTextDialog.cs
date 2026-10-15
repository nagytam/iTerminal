using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Dialogs;

public class SearchTextDialog : Dialog
{
    private ISearchModel _searchModel { get; set; }
    private TextBox _textBoxSearch;
    private Button _searchTextButton { get; set; }
    private Button _cancelButton { get; set; }
    public string SearchValue => _textBoxSearch.Text;

    public SearchTextDialog(
        ISearchModel searchModel,
        ITextModel textModel,
        IScrollableModel scrollableModel,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService) 
        : base("Search text", themeService)
    {
        _searchModel = searchModel;
        _textBoxSearch = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Search", HotKeyChar = 'T' };
        _searchTextButton = new Button(new SearchTextCommand(searchModel, textModel, scrollableModel), themeService) { HotKeyChar = 'S' };
        _cancelButton = new CancelDialogButton(themeService);
        AddControl(_textBoxSearch);
        AddControl(_searchTextButton);
        AddControl(_cancelButton);
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(new Size(60, 7));
        }

        // Ensure sizes and positions are non-negative so tests that initialize dialogs
        // without a parent (and therefore default ViewSize) do not produce negative
        // dimensions which would trigger assertions in Size.
        _textBoxSearch.Position = new Position(3, 2);
        var textBoxWidth = Math.Max(0, ViewSize.Width - 6);
        _textBoxSearch.ViewSize = new Size(textBoxWidth, 1);
        _textBoxSearch.ScrollableModel.ScrollableSize = new Size(_textBoxSearch.MaxLength, 1);
        var editableWidth = Math.Max(0, _textBoxSearch.EditableWidth);
        _textBoxSearch.ScrollableModel.ViewSize = new Size(editableWidth, 1);

        var searchButtonY = Math.Max(0, ViewSize.Height - 2);
        _searchTextButton.Position = new Position(3, searchButtonY);
        _searchTextButton.ViewSize = new Size(17, 1);

        var cancelButtonX = Math.Max(0, ViewSize.Width - 17);
        _cancelButton.Position = new Position(cancelButtonX, searchButtonY);
        _cancelButton.ViewSize = new Size(15, 1);
    }
}
