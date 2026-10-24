using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.StatusBar;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Dialogs;

public class FileViewerDialog : Dialog
{
    private const string CloseActionView = " [X]";

    public Url? Url { get; set; } = null;
    private readonly IContentModelService _contentModelService;
    private readonly IFileToTextConverterService _fileToTextConverterService;
    private readonly TextViewerControl _textViewerControl;
    private readonly ViewerStatusBar _statusBar;
    private readonly HexViewer _hexViewer;
    private bool _hexMode;
    private bool _hexLoaded;

    public FileViewerDialog(
        IContentModelService contentModelService,
        IFileToTextConverterService fileToTextConverterService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(FileViewerDialog), themeService)
    {
        _contentModelService = contentModelService;
        _fileToTextConverterService = fileToTextConverterService;
        _textViewerControl = new TextViewerControl(false, themeService);
        _statusBar = new ViewerStatusBar(
            _textViewerControl.ScrollableModel,
            _textViewerControl.SearchModel,
            _textViewerControl.TextModel,
            beepService,
            dateTimeProviderService,
            themeService,
            _textViewerControl);
        AddCommand(new ViewerWrapLinesCommand(_textViewerControl));
        AddCommand(new ViewerGoToLineCommand(_textViewerControl, beepService, dateTimeProviderService, themeService));
        _hexViewer = new HexViewer(themeService);
        AddCommand(new ViewerGoToOffsetCommand(_hexViewer, beepService, dateTimeProviderService, themeService));
        AddControl(_textViewerControl);
        AddControl(_hexViewer);
        AddControl(_statusBar);
    }

    /// <summary>True while the file is shown as a hex dump instead of text (F4 toggles).</summary>
    public bool IsHexMode => _hexMode;

    /// <summary>The hex dump control; exposed so tests can read the loaded bytes.</summary>
    public HexViewer HexViewer => _hexViewer;

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        // ShowAsync makes every child visible; the hex dump is on demand.
        _hexMode = false;
        _hexViewer.IsVisible = false;
        _textViewerControl.IsVisible = true;
    }

    /// <summary>Switches between the text view and the hex dump of the file's raw bytes.</summary>
    public async Task ToggleHexModeAsync()
    {
        if (!_hexMode && !_hexLoaded)
        {
            var fileSystem = _contentModelService.GetFocusedModel() as IFileSystem;
            var file = Url is not null && fileSystem is not null
                ? await fileSystem.ReadFileAsync(Url)
                : null;
            _hexViewer.Data = file?.Content ?? [];
            _hexLoaded = true;
        }

        _hexMode = !_hexMode;
        _textViewerControl.IsVisible = !_hexMode;
        _hexViewer.IsVisible = _hexMode;
        if (_hexMode)
        {
            _hexViewer.SetFocus();
        }
        else
        {
            _textViewerControl.SetFocus();
        }
        Title = (Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length)) ?? "Viewer")
            + (_hexMode ? HexHints() : TextHints());
        StateHasChanged();
    }

    /// <summary>
    /// F8 / Shift+F8: reads the text again under the next / previous code page. Only for what is shown
    /// as text — a hex dump has no code page, and a PDF or an image no bytes that are text.
    /// </summary>
    public void SwitchEncoding(bool backwards = false)
    {
        if (_hexMode || !FileToTextConverterService.IsEncodingRelevant(Url))
        {
            return;
        }

        TextEncodings.Reinterpret(
            _textViewerControl.TextModel,
            TextEncodings.Next(_textViewerControl.TextModel.Encoding, backwards));
        StateHasChanged();
    }

    /// <summary>The keys the hex dump adds: the offset key, and the way back to the text.</summary>
    private static string HexHints() => " [HEX]  F4:Text  F5:Offset";

    /// <summary>The keys the text view adds; the wrap key says whether wrapping is on.</summary>
    private string TextHints() =>
        $"  F2:Wrap{(_textViewerControl.WrapLines ? "[on]" : string.Empty)}  F4:Hex  F5:Line  F8:Codepage";

    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton("Close", CloseActionView, new CloseSelfCommand(this), ThemeService));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.Escape
            && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            Close();
            return true;
        }

        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.F8
            && !keyEvent.Control && !keyEvent.Alt)
        {
            SwitchEncoding(backwards: keyEvent.Shift);
            return true;
        }

        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.F4
            && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            await ToggleHexModeAsync();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (HitTestTitleBarClose(mouseEvent))
        {
            Close();
            return true;
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    /// <summary>
    /// Hit-test the close label at the right end of the title row (see <see cref="PopUpWindow.Draw"/> + <see cref="StringHelper.AlignCenter"/>).
    /// </summary>
    private bool HitTestTitleBarClose(MouseEvent mouseEvent)
    {
        if (!mouseEvent.LeftClicked || mouseEvent.IsRelease || !IsVisible)
        {
            return false;
        }

        var actions = GetActionButtons();
        if (actions.Length == 0 || ViewSize.Width < 3)
        {
            return false;
        }

        if (mouseEvent.Position.Y != AbsolutePosition.Y)
        {
            return false;
        }

        var innerW = (int)ViewSize.Width - 2;
        var lineLen = (int)ViewSize.Width;
        var start = (lineLen - innerW) / 2;
        var closeLeft = AbsolutePosition.X + start + (innerW - actions.Length);
        var closeRight = AbsolutePosition.X + start + innerW - 1;
        return mouseEvent.Position.X >= closeLeft && mouseEvent.Position.X <= closeRight;
    }

    private sealed class CloseSelfCommand(FileViewerDialog owner) : Command
    {
        public override Task ExecuteAsync(object? arg)
        {
            owner.Close();
            return Task.CompletedTask;
        }
    }

    public override async Task OnInitializeAsync()
    {
        var fileSystem = _contentModelService.GetFocusedModel() as IFileSystem;
        await _fileToTextConverterService.ReadFileAsync(Url, fileSystem!, _textViewerControl.TextModel);

        await base.OnInitializeAsync();
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            ViewSize = Parent.ViewSize;
            Position = new Position(0, 0);
            Title = Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length)) ?? "Viewer";
            Title += _hexMode ? HexHints() : TextHints();
        }

        _textViewerControl.Position = new(1, 1);
        _textViewerControl.ViewSize =
            new(ViewSize.Width - 2, ViewSize.Height - 3);

        _hexViewer.Position = new(1, 1);
        _hexViewer.ViewSize = new(ViewSize.Width - 2, ViewSize.Height - 3);

        _statusBar.Position = new Position(1, ViewSize.Height - 2);
        _statusBar.ViewSize = new(ViewSize.Width - 3, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}
