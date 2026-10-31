using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Base for the Create file / Create folder dialogs: a validated name textbox, an error message
/// line, and a create/cancel button row. Subclasses supply the textbox (which carries the
/// validation model) and the command the create button runs.
/// </summary>
public abstract class CreateEntryDialog : FormDialog
{
    private Url _baseUrl = null!;

    /// <summary>
    /// The folder the entry is created in. Handed on to the name box's validation, so a name that
    /// is already taken there is reported while it is typed, and Enter cannot create over it.
    /// </summary>
    public Url BaseUrl
    {
        get => _baseUrl;
        set
        {
            _baseUrl = value;
            if (_textBoxName.ValidationModel is Model.Validations.UrlValidationModel validationModel)
            {
                validationModel.BaseUrl = value;
                validationModel.Validate(DateTime.Now);
            }
        }
    }

    internal TextBox NameBoxForTest => _textBoxName;

    /// <summary>The name typed into the dialog's textbox.</summary>
    public string NewEntryName => _textBoxName.Text;

    private readonly TextBox _textBoxName;
    private readonly ErrorMessage _errorMessage;
    private readonly Button _createButton;
    private readonly Button _cancelButton;

    protected CreateEntryDialog(
        string name,
        string title,
        TextBox textBoxName,
        Command createCommand,
        IThemeService themeService)
        : base(name, themeService)
    {
        Title = title;

        _textBoxName = textBoxName;
        _textBoxName.Label = iConsole.Localization.UiText.Get("Label.Name", "Name");
        _textBoxName.HotKeyChar = 'N';
        _errorMessage = new ErrorMessage(textBoxName.ValidationModel!, themeService);

        AddCommand(createCommand);
        _createButton = new Button(title, createCommand, themeService) { HotKeyChar = CreateButtonHotKey(title) };
        _cancelButton = new CancelDialogButton(themeService);
        AddControl(_textBoxName);
        AddControl(_errorMessage);
        AddControl(_createButton);
        AddControl(_cancelButton);
    }

    /// <summary>
    /// The letter that runs Create from the keyboard. It is taken from the caption, so the highlight shows in every
    /// language (a fixed letter that a translated caption lacks was never drawn), and it skips C and N, which the
    /// Cancel button and the name box already hold. Null when the caption has no usable letter.
    /// </summary>
    internal static char? CreateButtonHotKey(string caption)
    {
        string[] words = caption.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int word = words.Length - 1; word >= 0; word--)
        {
            foreach (char letter in words[word])
            {
                if (char.IsLetter(letter) && char.ToLowerInvariant(letter) is not ('c' or 'n'))
                {
                    return letter;
                }
            }
        }

        return null;
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(new Size(60, 7));
        }

        _textBoxName.Position = new Position(FieldX, 2);
        _textBoxName.ViewSize = new Size(FieldWidth, 1);
        _textBoxName.ScrollableModel.ScrollableSize = new Size(_textBoxName.MaxLength, 1);
        _textBoxName.ScrollableModel.ViewSize = new Size(_textBoxName.EditableWidth, 1);
        _errorMessage.Position = new Position(FieldX, 3);
        _errorMessage.ViewSize = new Size(FieldWidth, 1);
        LayoutButtonRow(_createButton, _cancelButton);
    }

    /// <summary>
    /// Says why the create did not happen, on the error line, and keeps the dialog open with the name still in
    /// the box so it can be changed. The next key that edits the name withdraws the message.
    /// </summary>
    public void ReportFailure(string message) => _errorMessage.Text = message;

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var isHandled = await base.HandleKeyEventAsync(keyEvent);
        bool editsTheName = ConsoleKeyHelper.IsTypingKey(keyEvent)
            || keyEvent.ConsoleKey is ConsoleKey.Backspace or ConsoleKey.Delete;
        if (isHandled && _textBoxName.IsFocused && editsTheName)
        {
            _errorMessage.Text = string.Empty;
        }

        return isHandled;
    }

    public void Reset()
    {
        _textBoxName.ScrollableModel.CursorPosition.X = 0;
        _textBoxName.Text = string.Empty;
        _errorMessage.Text = string.Empty;
    }
}
