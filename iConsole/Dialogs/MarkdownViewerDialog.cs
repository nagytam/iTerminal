using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Localization;
using iConsole.Services;

namespace iConsole.Dialogs;

/// <summary>
/// The Markdown viewer: the document laid out as it reads (headings, lists, quotes, code blocks, emphasis), re-wrapped when the
/// window is resized. F4 switches to the file's text, the same view the plain viewer shows, and back again. Scrolling is the
/// Markdown control's: the arrow keys, Page Up and Page Down, Home and End, and the wheel.
/// </summary>
public class MarkdownViewerDialog : Dialog
{
    private const string CloseActionView = " [X]";

    private readonly IContentModelService _contentModelService;
    private readonly IFileToTextConverterService _fileToTextConverterService;
    // The rendered document is added first: a dialog gives the keyboard to its first focusable control, and that is where reading starts.
    private readonly MarkdownViewerControl _markdownViewerControl;
    private readonly TextViewerControl _textViewerControl;
    private readonly Label _statusLabel;
    private bool _textMode;
    private bool _textLoaded;

    public MarkdownViewerDialog(
        IContentModelService contentModelService,
        IFileToTextConverterService fileToTextConverterService,
        IThemeService themeService)
        : base(nameof(MarkdownViewerDialog), themeService)
    {
        _contentModelService = contentModelService;
        _fileToTextConverterService = fileToTextConverterService;
        _markdownViewerControl = new MarkdownViewerControl(themeService);
        _textViewerControl = new TextViewerControl(false, themeService);
        _statusLabel = new Label(string.Empty, themeService);
        AddControl(_markdownViewerControl);
        AddControl(_textViewerControl);
        AddControl(_statusLabel);
    }

    /// <summary>The file shown.</summary>
    public Url? Url { get; set; }

    /// <summary>True while the file's text is shown instead of the rendered document (F4 toggles).</summary>
    public bool IsTextMode => _textMode;

    /// <summary>The status line: why the file could not be shown, or empty.</summary>
    public string Status => _statusLabel.Text;

    /// <summary>The Markdown source of the rendered view, or empty while the file loads.</summary>
    public string Markdown => _markdownViewerControl.Markdown;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        IFileSystem? fileSystem = _contentModelService.GetFocusedModel() as IFileSystem;
        if (Url is null || fileSystem is null)
        {
            _statusLabel.Text = UiText.Get("Viewer.NothingToShow", "Nothing to show");
            return;
        }

        byte[]? content = (await fileSystem.ReadFileAsync(Url))?.Content;
        if (content is null)
        {
            _statusLabel.Text = UiText.Get("Viewer.CouldNotRead", "Could not read the file");
            return;
        }

        // The text is decoded the way the plain viewer decodes it: a byte order mark, else UTF-8, else the system's code page.
        _markdownViewerControl.Markdown = TextEncodings.Decode(content, out _);
        StateHasChanged();
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        // ShowAsync makes every child visible; the text view is on demand.
        _textMode = false;
        _textViewerControl.IsVisible = false;
        _markdownViewerControl.SetFocus();
    }

    /// <summary>Switches between the rendered document and the file's text.</summary>
    public async Task ToggleTextModeAsync()
    {
        if (!_textMode)
        {
            await ShowTextAsync();
        }
        else
        {
            SetMode(textMode: false);
        }
    }

    private async Task ShowTextAsync()
    {
        if (!_textLoaded)
        {
            IFileSystem? fileSystem = _contentModelService.GetFocusedModel() as IFileSystem;
            await _fileToTextConverterService.ReadFileAsync(Url, fileSystem!, _textViewerControl.TextModel);
            _textLoaded = true;
        }

        SetMode(textMode: true);
    }

    private void SetMode(bool textMode)
    {
        _textMode = textMode;
        _markdownViewerControl.IsVisible = !textMode;
        _textViewerControl.IsVisible = textMode;
        if (textMode)
        {
            _textViewerControl.SetFocus();
        }
        else
        {
            _markdownViewerControl.SetFocus();
        }

        Title = TitleText();
        StateHasChanged();
    }

    private string TitleText() =>
        (Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length)) ?? "Markdown")
        + (_textMode ? " [TEXT]" : "  F4:Text");

    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton(UiText.Get("Common.Close", "Close"), CloseActionView, new CloseSelfCommand(this), ThemeService));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType != KeyPressType.Down)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Escape && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            Close();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.F4 && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            await ToggleTextModeAsync();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>The keys this dialog adds to the document's, for the help screen.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("F4", UiText.Get("Viewer.HintToggleText", "Show the file's text, or the rendered document again"));
    }

    private sealed class CloseSelfCommand(MarkdownViewerDialog owner) : Command
    {
        public override Task ExecuteAsync(object? arg)
        {
            owner.Close();
            return Task.CompletedTask;
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            ViewSize = Parent.ViewSize;
            Position = new Position(0, 0);
            Title = TitleText();
        }

        // The rows inside the border, with the status line on the row above the bottom border.
        int width = Math.Max(0, (int)ViewSize.Width - 2);
        int content = Math.Max(1, (int)ViewSize.Height - 3);
        int statusRow = Math.Max(0, (int)ViewSize.Height - 2);

        _markdownViewerControl.Position = new Position(1, 1);
        _markdownViewerControl.ViewSize = new Size(width, content);

        _textViewerControl.Position = new Position(1, 1);
        _textViewerControl.ViewSize = new Size(width, content);

        _statusLabel.Position = new Position(1, statusRow);
        _statusLabel.ViewSize = new Size(width, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}
