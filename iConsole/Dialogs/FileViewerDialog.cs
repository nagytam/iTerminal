using iConsole.Localization;
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

    // The disassembly of a .dll, .exe or .so is built on a worker after the metadata page is already on screen and lands from
    // OnIdle, like the HDF5 and SQLite viewers' reads. The worker only returns lines; the text model is changed on the UI thread.
    private Task<IReadOnlyList<string>>? _disassembly;
    private CancellationTokenSource? _disassemblyCancellation;
    private List<string>? _metadataLines;
    private string? _disassemblyUrl;

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

    /// <summary>The disassembly that is being built in the background, or null; exposed so tests can wait for it.</summary>
    public Task? DisassemblyTask => _disassembly;

    /// <summary>The hex dump control; exposed so tests can read the loaded bytes.</summary>
    public HexViewer HexViewer => _hexViewer;

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        // ShowAsync makes every child visible; the hex dump is on demand.
        _hexMode = false;
        _hexViewer.IsVisible = false;
        _textViewerControl.IsVisible = true;
        // The dialog gave the focus to its first focusable child while laying out, and that was the hex dump: hidden, but
        // first in line for every key, so Down, PageDown, End and the like all went to a view nobody could see.
        _hexViewer.ResetFocus();
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
            // The text view cannot take the focus; what matters is that the hidden hex dump no longer holds it.
            _hexViewer.ResetFocus();
        }
        Title =(Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - CloseActionView.Length)) ?? "Viewer")
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
        string.Format(iConsole.Localization.UiText.Get("Viewer.TitleHints", "  F2:Wrap{0}  F4:Hex  F5:Line  F8:Codepage"), _textViewerControl.WrapLines ? "[on]" : string.Empty);

    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton(iConsole.Localization.UiText.Get("Common.Close", "Close"), CloseActionView, new CloseSelfCommand(this), ThemeService));
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Esc", UiText.Get("Hint.CloseViewer", "Close the viewer"));
        yield return ("F4", UiText.Get("Hint.SwitchTextHex", "Switch between the text and the hex view"));
        yield return ("F8", UiText.Get("Viewer.HintCodePage", "Next code page (Shift: previous)"));
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
        _textViewerControl.Syntax = SyntaxHighlighter.ForFile(Url is null ? null : (Url.Path.Length > 0 ? Url.Path : Url.FullUrl));
        StartDisassembly(fileSystem);

        await base.OnInitializeAsync();
    }

    /// <summary>Starts the disassembly of a binary in the background and marks the end of the metadata page as in progress.</summary>
    private void StartDisassembly(IFileSystem? fileSystem)
    {
        if (Url is null || fileSystem is null || !FileToTextConverterService.HasDisassembly(Url))
        {
            return;
        }

        CancelDisassembly();
        _metadataLines = _textViewerControl.TextModel.Lines.ToList();
        _disassemblyUrl = Url.FullUrl;
        _textViewerControl.TextModel.Lines.Add(string.Empty);
        _textViewerControl.TextModel.Lines.Add(UiText.Get("Viewer.Disassembling", "Disassembling…"));

        _disassemblyCancellation = new CancellationTokenSource();
        CancellationToken token = _disassemblyCancellation.Token;
        Url url = Url;
        IFileToTextConverterService converter = _fileToTextConverterService;
        // A converter that hands back no task (a test double) means there is nothing to add.
        _disassembly = Task.Run(
            async () => await (converter.BuildDisassemblyAsync(url, fileSystem, token) ?? Task.FromResult<IReadOnlyList<string>>([])),
            token);
    }

    /// <summary>Puts a finished disassembly on screen. A cancelled one, or one for another file, is dropped without a word.</summary>
    public override void OnIdle()
    {
        if (LandDisassembly())
        {
            StateHasChanged();
        }

        base.OnIdle();
    }

    private bool LandDisassembly()
    {
        if (_disassembly is not { IsCompleted: true } task)
        {
            return false;
        }

        List<string>? metadata = _metadataLines;
        string? startedFor = _disassemblyUrl;
        _disassembly = null;
        _metadataLines = null;
        _disassemblyCancellation?.Dispose();
        _disassemblyCancellation = null;
        if (metadata is null || task.IsCanceled || !string.Equals(startedFor, Url?.FullUrl, StringComparison.Ordinal))
        {
            // Reading the exception marks it observed, so a failed worker leaves no unobserved-exception noise behind.
            _ = task.Exception;
            return false;
        }

        List<string> lines = [.. metadata];
        if (task.IsFaulted)
        {
            lines.Add(string.Empty);
            lines.Add(string.Format(UiText.Get("Viewer.DisassemblyFailed", "Disassembly failed: {0}"), task.Exception?.GetBaseException().Message));
        }
        else if (task.Result is { Count: > 0 } disassembly)
        {
            lines.Add(string.Empty);
            lines.AddRange(disassembly);
        }

        _textViewerControl.TextModel.Lines.ReplaceAll(lines);
        return true;
    }

    /// <summary>Cancels a disassembly that is being replaced or dropped, so nothing lands on a dialog that is gone.</summary>
    public override void Close()
    {
        CancelDisassembly();
        base.Close();
    }

    private void CancelDisassembly()
    {
        CancellationTokenSource? source = _disassemblyCancellation;
        Task? running = _disassembly;
        _disassemblyCancellation = null;
        _disassembly = null;
        _metadataLines = null;
        if (source is null)
        {
            return;
        }

        source.Cancel();
        // The running worker still holds the token, so the source is disposed only once the worker has finished.
        (running ?? Task.CompletedTask).ContinueWith(
            finished =>
            {
                _ = finished.Exception;
                source.Dispose();
            },
            TaskScheduler.Default);
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

        // The base layout hands the focus to the first focusable child, which is the hex dump — hidden, yet first in line for every
        // key. The control that is on screen is the one that takes them.
        if (_hexMode)
        {
            _hexViewer.SetFocus();
        }
        else
        {
            // Only the hex dump can take the focus, and the base layout gave it; the text view reads keys as an unfocused child's.
            _hexViewer.ResetFocus();
        }

        Layout.Center(this);
    }
}
