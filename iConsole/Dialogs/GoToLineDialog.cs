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
    private string _message = string.Empty;

    public GoToLineDialog(
        TextViewerControl viewer,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base("Go to line", themeService)
    {
        _viewer = viewer;
        _lineBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = "Line",
            Text = viewer.CursorLine.ToString(),
        };
        AddControl(_lineBox);
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
            _message = "Enter a line number.";
            return false;
        }

        long last = Math.Max(1, _viewer.TextModel.Lines.Count);
        _viewer.ShowLine(Math.Clamp(line, 1, last) - 1, 0);
        _message = string.Empty;
        return true;
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
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

        _lineBox.Position = new Position(3, 2);
        _lineBox.ViewSize = new Size(Math.Max(0, ViewSize.Width - 6), 1);
        _lineBox.ScrollableModel.ScrollableSize = new Size(_lineBox.MaxLength, 1);
        _lineBox.ScrollableModel.ViewSize = new Size(Math.Max(0, _lineBox.EditableWidth), 1);
        base.OnInitializeLayout();
    }
}
