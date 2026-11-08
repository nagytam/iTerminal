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
    private readonly CheckBox _regexBox;
    private readonly CheckBox _matchCaseBox;
    private readonly Label _errorLabel;
    private static string DialogTitle => iConsole.Localization.UiText.Get("Editor.SearchText", "Search text");
    public string SearchValue => _textBoxSearch.Text;

    /// <summary>The search text is a regular expression.</summary>
    public bool UseRegex => _regexBox.IsChecked;

    /// <summary>Upper and lower case must match. On by default, as the search has always been.</summary>
    public bool MatchCase => _matchCaseBox.IsChecked;

    /// <summary>What the dialog says above its buttons: empty, or why the search could not run.</summary>
    public string ErrorText => _errorLabel.Text;

    /// <summary>
    /// Says on a line of its own why the search could not run, such as a pattern that does not compile. It used to go
    /// into the title, cut at 40 characters, where it read as part of the caption and lost the end of the reason.
    /// </summary>
    public void ShowError(string message)
    {
        _errorLabel.Text = message.Split('\n')[0].Trim();
        _errorLabel.ForegroundColor = ThemeService.CurrentColorScheme.Error;
        StateHasChanged();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", iConsole.Localization.UiText.Get("Hint.RunTheSearch", "Run the search"));
        yield return ("Tab", iConsole.Localization.UiText.Get("Hint.NextField", "Move to the next field"));
        yield return ("Esc", iConsole.Localization.UiText.Get("Hint.CloseWithoutSearching", "Close without searching"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool handled = await base.HandleKeyEventAsync(keyEvent);
        if (handled && _errorLabel.Text.Length > 0 && _textBoxSearch.IsFocused && Helper.ConsoleKeyHelper.IsTypingKey(keyEvent))
        {
            // Typing is the answer to what the message said.
            _errorLabel.Text = string.Empty;
        }

        return handled;
    }

    public SearchTextDialog(
        ISearchModel searchModel,
        ITextModel textModel,
        IScrollableModel scrollableModel,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService) 
        : base(DialogTitle, themeService)
    {
        _searchModel = searchModel;
        _textBoxSearch = new TextBox(beepService, dateTimeProviderService, themeService) { Label = iConsole.Localization.UiText.Get("Editor.Search", "Search"), HotKeyChar = 'T' };
        _searchTextButton = new Button(new SearchTextCommand(searchModel, textModel, scrollableModel), themeService) { HotKeyChar = 'S' };
        _cancelButton = new CancelDialogButton(themeService);
        _regexBox = new CheckBox(themeService) { Label = iConsole.Localization.UiText.Get("Editor.RegularExpression", "Regular expression"), IsChecked = searchModel.UseRegex };
        _matchCaseBox = new CheckBox(themeService) { Label = iConsole.Localization.UiText.Get("Editor.MatchCase", "Match case"), IsChecked = searchModel.MatchCase };
        _errorLabel = new Label(string.Empty, themeService);
        AddControl(_textBoxSearch);
        AddControl(_errorLabel);
        AddControl(_regexBox);
        AddControl(_matchCaseBox);
        AddControl(_searchTextButton);
        AddControl(_cancelButton);
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(new Size(60, 9));
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

        _regexBox.Position = new Position(3, 4);
        _regexBox.ViewSize = new Size(24, 1);
        // The two options side by side need 46 columns; a narrower dialog stacks them instead of running over its border.
        var sideBySide = ViewSize.Width >= 50;
        _matchCaseBox.Position = sideBySide ? new Position(30, 4) : new Position(3, 5);
        _matchCaseBox.ViewSize = new Size(16, 1);

        _errorLabel.Position = new Position(3, 6);
        _errorLabel.ViewSize = new Size(textBoxWidth, 1);

        var searchButtonY = Math.Max(0, ViewSize.Height - 2);
        _searchTextButton.Position = new Position(3, searchButtonY);
        _searchTextButton.ViewSize = new Size(17, 1);

        var cancelButtonX = Math.Max(0, ViewSize.Width - 17);
        _cancelButton.Position = new Position(cancelButtonX, searchButtonY);
        _cancelButton.ViewSize = new Size(15, 1);
    }
}
