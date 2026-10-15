using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Controls.StatusBar;

public class EditorStatusBar : StatusBar
{
    private Button _saveButton { get; set; }
    private Button _findNextButton { get; set; }
    private Button _findPreviousButton { get; set; }
    private Button _searchTextDialogButton { get; set; }
    private Button _toggleReadOnlyButton { get; set; }
    private EncodingIndicator _encodingIndicator { get; set; }
    private CursorPositionIndicator _cursorPositionIndicator { get; set; }
    private ReadOnlyIndicator _readOnlyIndicator { get; set; }
    private DirtyIndicator _dirtyIndicator { get; set; }
    private SearchResultsIndicator _searchResultsIndicator { get; set; }

    public EditorStatusBar(
        IReadOnly readOnly,
        IDirtyModel dirtyModel,
        IDirtyReadOnlyModel dirtyReadOnlyModel,
        IScrollableModel scrollableModel,
        ISearchModel searchModel,
        ITextModel textModel,
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(themeService)
    {
        Name = nameof(EditorStatusBar);

        ViewSize = new Size(0, 1);

        _saveButton = new Button(new SaveCommand(textModel, contentModelService), themeService);
        _findNextButton = new Button(new FindNextCommand(searchModel, scrollableModel), themeService);
        _findPreviousButton = new Button(new FindPreviousCommand(searchModel, scrollableModel), themeService);
        _toggleReadOnlyButton = new Button(new ToggleReadOnlyCommand(readOnly), themeService);
        _searchTextDialogButton = new Button(
            new SearchTextDialogCommand(searchModel, textModel, scrollableModel, beepService, dateTimeProviderService, themeService), themeService);
        _encodingIndicator = new EncodingIndicator(textModel, themeService);
        _cursorPositionIndicator = new CursorPositionIndicator(scrollableModel, themeService);
        _readOnlyIndicator = new ReadOnlyIndicator(readOnly, themeService);
        _dirtyIndicator = new DirtyIndicator(dirtyReadOnlyModel, themeService);
        _searchResultsIndicator = new SearchResultsIndicator(searchModel, themeService);
        AddControl(_saveButton);
        AddControl(_findNextButton);
        AddControl(_findPreviousButton);
        AddControl(_toggleReadOnlyButton);
        AddControl(_searchTextDialogButton);
        AddControl(_encodingIndicator);
        AddControl(_cursorPositionIndicator);
        AddControl(_readOnlyIndicator);
        AddControl(_dirtyIndicator);
        AddControl(_searchResultsIndicator);
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            var width = Math.Max(0L, Parent.ViewSize.Width - 2);
            ViewSize = new Size(width, 1);
        }
        base.OnInitializeLayout();

        _saveButton.ViewSize = new Size(9, 1);
        _findNextButton.ViewSize = new Size(13, 1);
        _findPreviousButton.ViewSize = new Size(18, 1);
        _toggleReadOnlyButton.ViewSize = new Size(13, 1);
        _searchTextDialogButton.ViewSize = new Size(11, 1);
        _encodingIndicator.ViewSize = new Size(10, 1);
        _cursorPositionIndicator.ViewSize = new Size(20, 1);
        _readOnlyIndicator.ViewSize = new Size(10, 1);
        _dirtyIndicator.ViewSize = new Size(10, 1);
        _searchResultsIndicator.ViewSize = new Size(20, 1);

        Layout.StackHorizontal([
            _saveButton,
            _findNextButton,
            _findPreviousButton,
            _toggleReadOnlyButton,
            _searchTextDialogButton,
            _encodingIndicator,
            _cursorPositionIndicator,
            _readOnlyIndicator,
            _dirtyIndicator,
            _searchResultsIndicator
        ]);
    }
}
