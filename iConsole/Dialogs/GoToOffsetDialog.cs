using iConsole.Controls;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Services;

namespace iConsole.Dialogs;

/// <summary>
/// F5 in the hex dump: a byte offset to jump to, decimal or hexadecimal (<c>0x1F</c>, <c>1Fh</c>). Enter goes there and Esc
/// closes. An offset past the end goes to the last byte; anything that is not an offset says so.
/// </summary>
public class GoToOffsetDialog : Dialog
{
    private readonly HexViewer _hexViewer;
    private readonly TextBox _offsetBox;
    private string _message = string.Empty;

    public GoToOffsetDialog(
        HexViewer hexViewer,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(iConsole.Localization.UiText.Get("Editor.GoToOffset", "Go to offset"), themeService)
    {
        _hexViewer = hexViewer;
        _offsetBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = iConsole.Localization.UiText.Get("Editor.Offset", "Offset"),
            Text = "0x0",
        };
        AddControl(_offsetBox);
    }

    /// <summary>What the dialog says under the box: empty, or why the offset was not taken.</summary>
    public string Message => _message;

    /// <summary>Goes to the offset in the box. Returns false, and says why, when the box does not hold an offset.</summary>
    public bool GoToRequestedOffset()
    {
        if (!HexViewer.TryParseOffset(_offsetBox.Text, out long offset))
        {
            _message = "Enter an offset: a number, or a hex number as 0x1F or 1Fh.";
            return false;
        }

        _hexViewer.ShowOffset(offset);
        _message = string.Empty;
        return true;
    }

    /// <summary>The box opens holding "0x0"; the first character typed replaces it rather than following it.</summary>
    private bool _replaceOnType = true;

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("0-9, 0x.., ..h", "Type the offset in decimal or hexadecimal");
        yield return ("Enter", "Go to the offset");
        yield return ("Esc", "Close without moving");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_replaceOnType && keyEvent.KeyPressType == KeyPressType.Down && !keyEvent.Control && !keyEvent.Alt)
        {
            if (keyEvent.KeyChar >= ' ' && keyEvent.KeyChar != '\u007F')
            {
                _offsetBox.Text = string.Empty;
                _offsetBox.ScrollableModel.CursorPosition.X = 0;
                _offsetBox.ScrollableModel.ScrollPosition.X = 0;
                _replaceOnType = false;
            }
            else if (keyEvent.ConsoleKey is ConsoleKey.Backspace or ConsoleKey.Delete or ConsoleKey.LeftArrow or ConsoleKey.RightArrow)
            {
                _replaceOnType = false;
            }
        }

        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.Enter
            && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            if (GoToRequestedOffset())
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

        _offsetBox.Position = new Position(3, 2);
        _offsetBox.ViewSize = new Size(Math.Max(0, ViewSize.Width - 6), 1);
        _offsetBox.ScrollableModel.ScrollableSize = new Size(_offsetBox.MaxLength, 1);
        _offsetBox.ScrollableModel.ViewSize = new Size(Math.Max(0, _offsetBox.EditableWidth), 1);
        base.OnInitializeLayout();
    }
}
