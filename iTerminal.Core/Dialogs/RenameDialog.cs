using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Services;
using iTerminal.Core.Commands;

namespace iTerminal.Core.Dialogs;

public class RenameDialog : FormDialog
{
    private readonly IContentModelService _contentModelService;
    public Url SourceUrl { get; set; }
    public Url TargetUrl
    {
        get => ResolveTarget(_textBoxName?.Text ?? string.Empty);
        set
        {
            if (_textBoxName.Text != value)
            {
                _textBoxName.Text = value;
            }
        }
    }

    /// <summary>What the name box holds: the entry's new name, or a whole path to move it to.</summary>
    public string NewName
    {
        get => _textBoxName.Text ?? string.Empty;
        set => _textBoxName.Text = value ?? string.Empty;
    }

    /// <summary>
    /// A bare name is the entry's new name <em>in the folder it is in</em>, worked out from the
    /// source's own URL so it holds on every drive kind; anything with a separator is taken as the
    /// full target. The dialog used to be handed the entry itself as the base, which made a bare
    /// name either rename a file to itself or move a folder into itself — so the box was pre-filled
    /// with the whole URL and the new name had to be typed at the end of it.
    /// </summary>
    internal Url ResolveTarget(string text)
    {
        if (string.IsNullOrEmpty(text) || !string.Equals(Path.GetFileName(text), text, StringComparison.Ordinal))
        {
            return text;
        }

        var source = SourceUrl?.FullUrl?.TrimEnd('/', '\\') ?? string.Empty;
        var cut = source.LastIndexOfAny(['/', '\\']);
        if (cut > 0)
        {
            return new Url(source[..(cut + 1)] + text);
        }

        return BaseUrl is not null ? BaseUrl.Combine(text) : text;
    }

    private RenameCommand _renameCommand { get; set; }
    private CancelDialogCommand _cancelCommand { get; set; }
    private TextBox _textBoxName { get; set; }
    private CheckBox _checkBoxOverwrite { get; set; }
    public bool Overwrite => _checkBoxOverwrite.IsChecked;
    private Button _renameButton { get; set; }
    private Button _cancelButton { get; set; }
    public Url BaseUrl { get; internal set; }
    private Label _errorLabel { get; set; }
    public Label ErrorMessage => _errorLabel;

    public RenameDialog(
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IContentModelService contentModelService,
        IThemeService themeService
        ) 
        : base(nameof(RenameDialog), themeService)
    {
        _contentModelService = contentModelService;
        Title = "Rename";

        _renameCommand = new RenameCommand(contentModelService);
        AddCommand(_renameCommand);

        _textBoxName = new TextBox(beepService, dateTimeProviderService, themeService) 
        { 
            Label = "Name", 
            Text = TargetUrl, 
            IsEnabled = true, 
            HotKeyChar = 'N' 
        };
        _checkBoxOverwrite = new CheckBox(themeService)
        {
            Label = "Overwrite",
            IsChecked = false,
            IsEnabled = true,
            HotKeyChar = 'O'
        };
        _renameButton = new Button("Rename", _renameCommand, themeService) { HotKeyChar = 'R' };
        _cancelButton = new CancelDialogButton(themeService);
        _errorLabel = new Label(string.Empty, themeService);
        AddControl(_textBoxName);
        AddControl(_checkBoxOverwrite);
        AddControl(_errorLabel);
        AddControl(_renameButton);
        AddControl(_cancelButton);
    }

    public override async Task OnInitializeAsync()
    {
        //BaseUrl = _contentModelService.Url;
        //var url = _contentModelService.FocusedFileUrl;
        //var targetFolder = _contentModelService.NonFocusedFolderUrl;
        //ArgumentNullException.ThrowIfNull(url);
        //SourceUrl = url;
        //TargetUrl = new Url(targetFolder).Combine(url.Name);
        _textBoxName.ScrollableModel.Reset();
        _textBoxName.ScrollableModel.ScrollableSize = new Size(_textBoxName.Text.Length, 1);

        await base.OnInitializeAsync();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(new Size(80, 7));
        }

        _textBoxName.Position = new Position(FieldX, 2);
        _textBoxName.ViewSize = new Size(FieldWidth, 1);
        _textBoxName.ScrollableModel.ViewSize = _textBoxName.ViewSize;
        _checkBoxOverwrite.ViewSize = new Size(FieldWidth, 1);
        _errorLabel.ViewSize = new Size(FieldWidth, 1);
        LayoutButtonRow(_renameButton, _cancelButton);

        Layout.Bottom(_checkBoxOverwrite, _textBoxName);
        Layout.Bottom(_errorLabel, _checkBoxOverwrite);
    }

    public void Reset()
    {
        BaseUrl = string.Empty;
        SourceUrl = string.Empty;
        TargetUrl = string.Empty;
        _errorLabel.Text = string.Empty;
    }
}
