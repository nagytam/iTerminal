using iConsole.Localization;
using iConsole.Controls;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Services;

namespace iConsole.Dialogs;

/// <summary>
/// F5 in the viewer: a line number to jump to. Enter goes there (the line lands a third of the way down the view, and the
/// cursor is at its start), Esc closes. A number past the end goes to the last line; anything that is not a number says so.
/// </summary>
public class GoToLineDialog : Dialog
{
    private readonly TextViewerControl _viewer;
    private readonly TextBox _lineBox;
    private readonly Label _messageLabel;
    private string _message = string.Empty;

    public GoToLineDialog(
        TextViewerControl viewer,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(iConsole.Localization.UiText.Get("Editor.GoToLine", "Go to line"), themeService)
    {
        _viewer = viewer;
        _lineBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = iConsole.Localization.UiText.Get("Editor.Line", "Line"),
            Text = viewer.CursorLine.ToString(),
        };
        _messageLabel = new Label(string.Empty, themeService);
        AddControl(_lineBox);
        AddControl(_messageLabel);
    }

    /// <summary>What the dialog says under the box: empty, or why the number was not taken.</summary>
    public string Message => _message;

    /// <summary>The line the box holds, as the viewer would go to it (counting from 1), or null when it is not a number.</summary>
    public long? RequestedLine =>
        long.TryParse(_lineBox.Text.Trim(), out long line) ? line : null;

    /// <summary>Goes to the line in the box. Returns false, and says why, when the box does not hold a number.</summary>
    public bool GoToRequestedLine()
    {
        if (RequestedLine is not long line)
        {
            SetMessage(UiText.Get("Msg.EnterLineNumber", "Enter a line number."));
            return false;
        }

        long last = Math.Max(1, _viewer.TextModel.Lines.Count);
        _viewer.ShowLine(Math.Clamp(line, 1, last) - 1, 0);
        SetMessage(string.Empty);
        return true;
    }

    /// <summary>Says it on the line under the box; the message used to be kept but never drawn, so a bad number closed nothing and told nothing.</summary>
    private void SetMessage(string message)
    {
        _message = message;
        _messageLabel.Text = message;
        _messageLabel.ForegroundColor = ThemeService.CurrentColorScheme.Error;
        StateHasChanged();
    }

    /// <summary>The box opens holding the current line; the first character typed replaces it, rather than being added to it ("1" + "250" went to line 1250).</summary>
    private bool _replaceOnType = true;

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("0-9", UiText.Get("Hint.TypeLineNumber", "Type the line number (the first key replaces the current one)"));
        yield return ("Enter", UiText.Get("Hint.GoToTheLine", "Go to the line"));
        yield return ("Esc", UiText.Get("Hint.CloseWithoutMoving", "Close without moving"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_replaceOnType && keyEvent.KeyPressType == KeyPressType.Down && !keyEvent.Control && !keyEvent.Alt)
        {
            if (keyEvent.KeyChar >= ' ' && keyEvent.KeyChar != '\u007F')
            {
                _lineBox.Text = string.Empty;
                _lineBox.ScrollableModel.CursorPosition.X = 0;
                _lineBox.ScrollableModel.ScrollPosition.X = 0;
                _replaceOnType = false;
            }
            else if (keyEvent.ConsoleKey is ConsoleKey.Backspace or ConsoleKey.Delete or ConsoleKey.LeftArrow or ConsoleKey.RightArrow)
            {
                _replaceOnType = false; // editing what is there
            }
        }

        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.Enter
            && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            if (GoToRequestedLine())
            {
                Close();
            }

            StateHasChanged();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(new Size(44, 5));
        }

        // The label says how far the number may go, so the answer is not a guess.
        _lineBox.Label = string.Format(UiText.Get("Editor.LineRange", "Line (1-{0})"), Math.Max(1, _viewer.TextModel.Lines.Count));
        _messageLabel.Position = new Position(3, 3);
        _messageLabel.ViewSize = new Size(Math.Max(0, ViewSize.Width - 6), 1);
        _lineBox.Position = new Position(3, 2);
        _lineBox.ViewSize = new Size(Math.Max(0, ViewSize.Width - 6), 1);
        _lineBox.ScrollableModel.ScrollableSize = new Size(_lineBox.MaxLength, 1);
        _lineBox.ScrollableModel.ViewSize = new Size(Math.Max(0, _lineBox.EditableWidth), 1);
        base.OnInitializeLayout();
    }
}
