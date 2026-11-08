using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Controls.StatusBar;

public class EditorStatusBar : StatusBar
{
    private Button _saveButton { get; set; }
    private Button _findNextButton { get; set; }
    private Button _findPreviousButton { get; set; }
    private Button _searchTextDialogButton { get; set; }
    private Button? _replaceButton { get; set; }
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
        IThemeService themeService,
        TextEditorControl? editor = null)
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
        if (editor is not null)
        {
            _replaceButton = new Button(
                new ReplaceTextDialogCommand(editor, beepService, dateTimeProviderService, themeService), themeService);
        }

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
        if (_replaceButton is not null)
        {
            AddControl(_replaceButton);
        }

        AddControl(_encodingIndicator);
        AddControl(_cursorPositionIndicator);
        AddControl(_readOnlyIndicator);
        AddControl(_dirtyIndicator);
        AddControl(_searchResultsIndicator);
    }

    public override void OnInitializeLayout()
    {
        // The width the dialog gave the bar (or, before it has, the parent's interior). The base layout would
        // replace it with the parent's full width, which put the last items on or past the dialog's border and let
        // too many of them count as fitting.
        var width = ViewSize.Width > 0 ? ViewSize.Width : Math.Max(0L, (Parent?.ViewSize.Width ?? 0) - 2);
        base.OnInitializeLayout();
        ViewSize = new Size(width, 1);

        // Wide enough for the caption in the chosen language ("[F2-Speichern]"), never narrower than the English one needed.
        static Size ForTitle(Button button, int minimum) => new(Math.Max(minimum, button.Title.Length + 2), 1);
        static Size ForWords(int minimum, params string[] words) => new(Math.Max(minimum, words.Max(w => w.Length) + 1), 1);
        _saveButton.ViewSize = ForTitle(_saveButton, 9);
        _findNextButton.ViewSize = ForTitle(_findNextButton, 13);
        _findPreviousButton.ViewSize = ForTitle(_findPreviousButton, 18);
        _toggleReadOnlyButton.ViewSize = ForTitle(_toggleReadOnlyButton, 13);
        _searchTextDialogButton.ViewSize = ForTitle(_searchTextDialogButton, 11);
        if (_replaceButton is not null)
        {
            _replaceButton.ViewSize = ForTitle(_replaceButton, 13);
        }

        _encodingIndicator.ViewSize = new Size(10, 1);
        _cursorPositionIndicator.ViewSize = new Size(20, 1);
        _readOnlyIndicator.ViewSize = ForWords(10, UiText.Get("Status.ReadOnly", "Read-Only"), UiText.Get("Status.Writable", "Writable"));
        _dirtyIndicator.ViewSize = ForWords(10, UiText.Get("Status.Changed", "Changed"), UiText.Get("Status.Unchanged", "Unchanged"));
        _searchResultsIndicator.ViewSize = new Size(20, 1);

        List<Control> order =
        [
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
        ];
        List<Control> priority =
        [
            _saveButton,
            _cursorPositionIndicator,
            _dirtyIndicator,
            _readOnlyIndicator,
            _encodingIndicator,
            _searchTextDialogButton,
            _findNextButton,
            _toggleReadOnlyButton,
            _searchResultsIndicator,
            _findPreviousButton
        ];
        if (_replaceButton is not null)
        {
            order.Insert(5, _replaceButton);
            priority.Insert(7, _replaceButton);
        }

        StackFitting(order, priority);
    }
}
