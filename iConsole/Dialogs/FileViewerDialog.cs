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
using System.Text;

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

    /// <summary>How often a followed file is checked for new lines.</summary>
    public static readonly TimeSpan FollowInterval = TimeSpan.FromSeconds(1);

    /// <summary>The most a follow reads in one check, so a large append does not hold the worker thread for long.</summary>
    private const int FollowChunkLimit = 4 * 1024 * 1024;

    private bool _following;

    /// <summary>The bytes of the file the text holds: the length when it was loaded, moved on by each follow. -1 when it is not a local file.</summary>
    private long _followBytes = -1;

    private long _lastFollowTick;
    private int _followRunning;
    private volatile FollowChunk? _followPending;

    /// <summary>
    /// The last follow check started from the viewer: nothing waits on it; it exists so a test can wait for the new lines to
    /// land.
    /// </summary>
    public Task PendingFollow { get; private set; } = Task.CompletedTask;

    /// <summary>The text a follow read: whole lines only, and how many bytes they took; Truncated when the file got shorter.</summary>
    private sealed record FollowChunk(string Text, long Consumed, bool Truncated);

    /// <summary>Whether the file is followed as it grows (F6, like tail -f).</summary>
    public bool IsFollowing => _following;

    private string? LocalPath => Url is { } url && string.Equals(url.UrlScheme, Container.FILE, StringComparison.OrdinalIgnoreCase)
        ? url.Path
        : null;

    /// <summary>
    /// F6 starts or stops following the file. Only a local file shown as text can be followed. New lines are added at the
    /// end and the cursor goes to the last line, checked about once a second. A file that gets shorter (truncated, or rotated)
    /// stops the follow, and the text stays as it was.
    /// </summary>
    public bool ToggleFollow()
    {
        if (_following)
        {
            _following = false;
            StateHasChanged();
            return true;
        }

        if (_hexMode || LocalPath is null || _followBytes < 0)
        {
            return false;
        }

        _following = true;
        _lastFollowTick = 0;
        StateHasChanged();
        return true;
    }

    /// <summary>
    /// Applies the last follow read and starts the next one when it is due. Runs on the UI thread from <see cref="OnIdle"/>;
    /// the file is read on a worker thread, and only the complete new lines are taken.
    /// </summary>
    private void TickFollow()
    {
        if (_followPending is { } chunk)
        {
            _followPending = null;
            ApplyFollow(chunk);
        }

        var path = LocalPath;
        var now = Environment.TickCount64;
        if (!_following || _hexMode || path is null || _followBytes < 0 || _followPending is not null
            || now - _lastFollowTick < FollowInterval.TotalMilliseconds
            || Interlocked.CompareExchange(ref _followRunning, 1, 0) != 0)
        {
            return;
        }

        _lastFollowTick = now;
        var from = _followBytes;
        var encoding = _textViewerControl.TextModel.Encoding;
        PendingFollow = Task.Run(() => ReadAppended(path, from, encoding)).ContinueWith(task =>
        {
            // Observing the exception keeps a failed read from being left unobserved; the next check tries again.
            if (task.IsCompletedSuccessfully)
            {
                _followPending = task.Result;
            }
            else
            {
                _ = task.Exception;
            }

            Interlocked.Exchange(ref _followRunning, 0);
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// The bytes after <paramref name="from"/> up to and including the last line break read so far, decoded. A line break
    /// byte never sits inside a multi-byte character in the encodings this viewer reads, so the cut is safe; a partial last
    /// line waits for its break. Null when there is nothing whole to add.
    /// </summary>
    private static FollowChunk? ReadAppended(string path, long from, Encoding encoding)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < from)
        {
            return new FollowChunk(string.Empty, 0, Truncated: true);
        }

        if (stream.Length == from)
        {
            return null;
        }

        var bytes = new byte[(int)Math.Min(stream.Length - from, FollowChunkLimit)];
        stream.Position = from;
        stream.ReadExactly(bytes);
        var newline = Array.LastIndexOf(bytes, (byte)'\n');
        if (newline < 0)
        {
            return null;
        }

        var used = newline + 1;
        return new FollowChunk(encoding.GetString(bytes, 0, used), used, Truncated: false);
    }

    /// <summary>
    /// Adds the new lines to the text. The unfinished last line is joined with the start of the new text, as the text model
    /// keeps the unfinished line last.
    /// </summary>
    private void ApplyFollow(FollowChunk chunk)
    {
        if (chunk.Truncated)
        {
            _following = false;
            _followBytes = -1;
            StateHasChanged();
            return;
        }

        var model = _textViewerControl.TextModel;
        var lines = model.Lines;
        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        var parts = (lines[lines.Count - 1] + chunk.Text).Split('\n');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            parts[i] = parts[i].TrimEnd('\r');
        }

        lines[lines.Count - 1] = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            lines.Add(parts[i]);
        }

        _followBytes += chunk.Consumed;
        model.TriggerPropertyChanged();
        _textViewerControl.Refresh();
        if (_following)
        {
            _textViewerControl.ScrollableModel.CursorPosition.Y = lines.Count - 1;
            _textViewerControl.ScrollableModel.CursorPosition.X = 0;
            _textViewerControl.ScrollableModel.ScrollToVisible();
        }

        StateHasChanged();
    }

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

    /// <summary>The text view: its lines are the file as it was read, and as followed.</summary>
    public TextViewerControl TextViewerControl => _textViewerControl;

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
        yield return ("F3", UiText.Get("Hint.FindNext", "Find the next match"));
        yield return ("Ctrl+F3", UiText.Get("Hint.FindPrevious", "Find the previous match"));
        if (!_hexMode)
        {
            yield return ("F6", _following
                ? UiText.Get("Hint.StopFollowing", "Stop following the file")
                : UiText.Get("Hint.FollowFile", "Follow the file as it grows"));
        }
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

        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.F6
            && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt)
        {
            ToggleFollow();
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
        // The length is taken before the text is read: anything appended after this is followed, so nothing is missed.
        _followBytes = LocalPath is { } path && File.Exists(path) ? new FileInfo(path).Length : -1;
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
        TickFollow();
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
