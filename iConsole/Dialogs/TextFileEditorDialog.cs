using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Controls.StatusBar;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Layouts;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using System.Text;
using System.Threading;

namespace iConsole.Dialogs;

public class TextFileEditorDialog : Dialog
{
    public override string Title
    {
        get => base.Title
            + (TextEditorControl.IsDirty ? " (*)" : string.Empty)
            + (_changedOnDisk ? " (" + UiText.Get("Viewer.ChangedOnDisk", "changed on disk") + ")" : string.Empty)
            + (_hexMode
                ? " [" + UiText.Get("Editor.HexTitle", "hex") + "]"
                : " [" + TextEditorControl.LineEndingName(TextEditorControl.TextModel.LineEnding) + "]");
        set => base.Title = value;
    }
    public Url? Url { get; set; } = null;
    private IContentModelService _contentModelService { get; set; }
    private IFileToTextConverterService _fileToTextConverterService { get; set; }
    public TextEditorControl TextEditorControl { get; set; }
    private EditorStatusBar _statusBar { get; set; }

    /// <summary>The largest file the hex view opens: the whole file is held in memory while it is edited.</summary>
    public const long MaxHexBytes = 64L * 1024 * 1024;

    /// <summary>The hex view of the file's bytes. Hidden until F4 (or <see cref="OpenAsHex"/>) shows it.</summary>
    public HexEditorControl HexEditorControl { get; }

    private bool _hexMode;

    /// <summary>The hex view was edited since the text was last made from it, so leaving it reads the text from the bytes.</summary>
    private bool _hexChanged;

    /// <summary>The text model no longer matches the file on disk, so it is read again when the editor leaves the hex view.</summary>
    private bool _textStale;

    /// <summary>
    /// The file was opened as bytes because the text editor does not take it. Leaving the hex view is allowed only when the
    /// bytes read back as the same text, so a binary is never saved as the text of its decoding.
    /// </summary>
    private bool _binaryFile;

    /// <summary>The file's bytes as last known, or null when they must be read again.</summary>
    private byte[]? _fileBytes;

    /// <summary>True while the editor shows the file's bytes instead of its text (F4 toggles).</summary>
    public bool IsHexMode => _hexMode;

    /// <summary>Set before the dialog is shown for a file the text editor does not take: the editor opens in the hex view.</summary>
    public bool OpenAsHex
    {
        get => _binaryFile;
        set => _binaryFile = value;
    }

    public TextFileEditorDialog(
        IContentModelService contentModelService,
        IFileToTextConverterService fileToTextConverterService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService,
        EditorBackupStore? backups = null)
        : base(nameof(TextFileEditorDialog), themeService)
    {
        _contentModelService = contentModelService;
        _backups = backups;
        _fileToTextConverterService = fileToTextConverterService;

        TextEditorControl = new TextEditorControl(themeService);
        HexEditorControl = new HexEditorControl(themeService)
        {
            IsVisible = false,
            ReadOnlyCheck = () => TextEditorControl.IsReadOnly,
        };
        HexEditorControl.Edited += OnHexEdited;
        _statusBar = new EditorStatusBar(
            TextEditorControl,
            TextEditorControl,
            TextEditorControl,
            TextEditorControl.ScrollableModel,
            TextEditorControl.SearchModel,
            TextEditorControl.TextModel,
            contentModelService,
            beepService,
            dateTimeProviderService,
            themeService,
            TextEditorControl);
        // The same key as the viewer's: F5 asks for a line number and goes to it.
        AddCommand(new ViewerGoToLineCommand(TextEditorControl, beepService, dateTimeProviderService, themeService));
        // Shift+F2 wraps the lines in the editor, as F2 does in the viewer; F2 is save here, and F6 is the footer's
        // read-only toggle (the editor's text is never focusable, so the footer always takes F6 first). Wrapped, Up and Down walk the rows.
        AddCommand(new ViewerWrapLinesCommand(TextEditorControl, new iConsole.Data.Key(ConsoleKey.F2, true, false, false)));
        AddControl(TextEditorControl);
        AddControl(HexEditorControl);
        AddControl(_statusBar);
    }

    /// <summary>
    /// Writes the text to its file the way F2 does, keeping the code page it was read with. A saved file is what the
    /// editor shows, so it is no longer changed on disk, and a reload that was read before the save is dropped.
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        var saved = await SaveCommand.WriteAsync(this, TextEditorControl.TextModel, _contentModelService);
        if (saved)
        {
            _pending = null;
            DeleteBackup();
            _seenStamp = StampOf(Url);
            _changedOnDisk = false;
            StateHasChanged();
        }

        return saved;
    }

    /// <summary>
    /// Records what a save wrote: the bytes of a hex save, which the next toggle to the hex view uses as they are; a text save
    /// leaves the bytes to be read again when they are needed.
    /// </summary>
    internal void NoteSaved(byte[]? hexBytes) => _fileBytes = hexBytes;

    // ---- Changes on disk ----

    /// <summary>How often the file's size and write time are looked at while the editor is open.</summary>
    public static readonly TimeSpan DiskCheckInterval = TimeSpan.FromSeconds(2);

    /// <summary>A file's size and write time: what changes when another program saves it.</summary>
    public readonly record struct DiskStamp(DateTime Modified, long Length);

    /// <summary>
    /// What a check read from disk, waiting for the UI thread to put it in the editor: the text, or the bytes when the hex view
    /// is shown or the file is a binary.
    /// </summary>
    private sealed record PendingDiskText(DiskStamp Stamp, string[]? Lines, string LineEnding, Encoding? Encoding, byte[]? Bytes);

    private IFileSystem? _fileSystem;

    /// <summary>The newest state of the file seen on disk; a check reads the file only when this changes.</summary>
    private DiskStamp? _seenStamp;

    private bool _changedOnDisk;
    private volatile PendingDiskText? _pending;
    private long _lastDiskCheck;
    private int _diskCheckRunning;

    /// <summary>Where unsaved text is backed up, or null for none (the editor then keeps nothing across a crash).</summary>
    private readonly EditorBackupStore? _backups;

    /// <summary>How often unsaved text is backed up while it changes.</summary>
    public static readonly TimeSpan BackupInterval = TimeSpan.FromSeconds(10);

    private long _lastBackupTick;
    private int _backupRunning;

    /// <summary>The text the last backup holds, so an idle editor does not write the same text again.</summary>
    private string[]? _lastBackedUp;

    /// <summary>
    /// The last backup write or removal started from the editor: nothing waits on it; it exists so a test can wait for the
    /// disk to settle.
    /// </summary>
    public Task PendingBackup { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// The stamp of a local file, or null when the file is not on this machine's disk or cannot be found. Other drives
    /// (SFTP, archives, memory) are not watched.
    /// </summary>
    public static DiskStamp? StampOf(Url? url)
    {
        if (url is null || !string.Equals(url.UrlScheme, Container.FILE, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(url.Path);
            return info.Exists ? new DiskStamp(info.LastWriteTimeUtc, info.Length) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Looks at the file, on a worker thread, and if it changed since the editor last saw it, reads it. The text is only
    /// put into the editor by <see cref="ApplyDiskChange"/>, on the UI thread. Public so that a test can run one check.
    /// </summary>
    public async Task CheckDiskAsync()
    {
        var url = Url;
        var fileSystem = _fileSystem;
        if (url is null || fileSystem is null)
        {
            return;
        }

        var stamp = await Task.Run(() => StampOf(url));
        if (stamp is not { } current || current == _seenStamp)
        {
            return;
        }

        // A binary is read as bytes, never through the text converter, which would give its decoding, not the file.
        if (_hexMode || _binaryFile)
        {
            try
            {
                _pending = new PendingDiskText(current, null, string.Empty, null, await ReadFileBytesAsync());
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }

            return;
        }

        var text = new TextModel();
        try
        {
            await _fileToTextConverterService.ReadFileAsync(url, fileSystem, text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        _pending = new PendingDiskText(current, text.Lines.AsSnapshot().ToArray(), text.LineEnding, text.Encoding, null);
    }

    /// <summary>
    /// Applies the change the last check read. A file with no unsaved edits is reloaded. One with edits keeps them, and the
    /// title says the file changed on disk until it is saved.
    /// </summary>
    public void ApplyDiskChange()
    {
        var pending = _pending;
        if (pending is null)
        {
            return;
        }

        // The view changed while the file was read, so what was read is the wrong kind: the next check reads it again.
        if ((_hexMode || _binaryFile) != (pending.Bytes is not null))
        {
            _pending = null;
            return;
        }

        _pending = null;
        _seenStamp = pending.Stamp;
        if (TextEditorControl.IsDirty)
        {
            _changedOnDisk = true;
            StateHasChanged();
            return;
        }

        if (pending.Bytes is { } bytes)
        {
            // The text is made from the bytes again when the editor leaves the hex view. A binary has no text to show here, so
            // the title says the file changed instead.
            _fileBytes = bytes;
            _textStale = true;
            if (_hexMode)
            {
                HexEditorControl.Load(bytes);
                _hexChanged = false;
            }
            else
            {
                _changedOnDisk = true;
                StateHasChanged();
                return;
            }
        }
        else
        {
            TextEditorControl.ReplaceAllText(pending.Lines!, pending.LineEnding, pending.Encoding!);
            _fileBytes = null;
            _textStale = false;
        }

        _changedOnDisk = false;
        StateHasChanged();
    }

    public override void OnIdle()
    {
        base.OnIdle();
        ApplyDiskChange();
        MaybeBackup();

        var now = Environment.TickCount64;
        if (now - _lastDiskCheck >= DiskCheckInterval.TotalMilliseconds
            && Interlocked.CompareExchange(ref _diskCheckRunning, 1, 0) == 0)
        {
            _lastDiskCheck = now;
            // Observing the exception keeps a failed check from being left unobserved; the next check runs as usual.
            _ = CheckDiskAsync().ContinueWith(task =>
            {
                _ = task.Exception;
                Interlocked.Exchange(ref _diskCheckRunning, 0);
            }, TaskScheduler.Default);
        }
    }

    private bool _asking;
    private bool _closeConfirmed;

    /// <summary>The question asked by the last Close of a changed file. Nothing waits on it; it exists so a test can.</summary>
    public Task PendingClose { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Closing a file that has unsaved changes asks first (Save, Discard or Cancel); Esc, the Close button and F10 all
    /// come here. A read-only editor has nothing to lose and closes at once.
    /// </summary>
    public override void Close()
    {
        if (_closeConfirmed || !TextEditorControl.IsDirty || TextEditorControl.IsReadOnly)
        {
            // Closed with nothing unsaved (or the changes discarded): the backup has nothing left to keep.
            _closeConfirmed = false;
            DeleteBackup();
            base.Close();
            return;
        }

        if (!_asking)
        {
            PendingClose = AskBeforeClosingAsync();
        }
    }

    private async Task AskBeforeClosingAsync()
    {
        _asking = true;
        try
        {
            var prompt = new UnsavedChangesDialog(Url?.Name is { Length: > 0 } name ? name : "This file", ThemeService);
            await ShowDialogAsync(prompt);
            switch (await prompt.Choice)
            {
                case UnsavedChoice.Save:
                    try
                    {
                        if (await SaveAsync())
                        {
                            _closeConfirmed = true;
                            Close();
                        }
                        else
                        {
                            Title = string.Format(UiText.Get("Viewer.NowhereToSave", "Nowhere to save to: {0}"), Url?.Shrink((int)ViewSize.Width - 30) ?? "no file");
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // Stay in the editor with the text: closing now would lose what could not be written.
                        Title = string.Format(UiText.Get("Viewer.NotSaved", "Not saved: {0}"), exception.Message);
                    }

                    StateHasChanged();
                    break;
                case UnsavedChoice.Discard:
                    _closeConfirmed = true;
                    Close();
                    break;
            }
        }
        finally
        {
            _asking = false;
        }
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        // The dialog is reused for every file, so what the last file left behind is cleared here. OpenAsHex is set by the
        // command just before this and is kept.
        _hexChanged = false;
        _textStale = false;
        _fileBytes = null;
        _pending = null;
        _changedOnDisk = false;
        // ShowAsync makes every child visible; the hex view is on demand, and a file the text editor does not take opens in it.
        _hexMode = false;
        HexEditorControl.IsVisible = false;
        TextEditorControl.IsVisible = true;
        if (_binaryFile)
        {
            await EnterHexAsync();
        }
    }

    public override async Task OnInitializeAsync()
    {
        var fileSystem = _contentModelService.GetFocusedModel() as IFileSystem;
        _fileSystem = fileSystem;
        if (_binaryFile)
        {
            // A binary is shown as bytes: its text is never read, and no backup is kept of it.
            _textStale = true;
            _seenStamp = StampOf(Url);
            await base.OnInitializeAsync();
            return;
        }

        await _fileToTextConverterService.ReadFileAsync(Url, fileSystem!, TextEditorControl.TextModel);
        _seenStamp = StampOf(Url);
        TextEditorControl.Syntax = SyntaxHighlighter.ForFile(Url is null ? null : (Url.Path.Length > 0 ? Url.Path : Url.FullUrl));
        await RestoreBackupAsync();

        await base.OnInitializeAsync();
    }

    // ---- The hex view ----

    /// <summary>F4: shows the file's bytes, or the text again. Leaving the hex view can be refused; the title says why.</summary>
    public async Task ToggleHexModeAsync()
    {
        if (_hexMode)
        {
            await LeaveHexAsync();
        }
        else
        {
            await EnterHexAsync();
        }
    }

    /// <summary>
    /// Shows the file as bytes. Unsaved text is shown as the bytes it would be saved as; otherwise the bytes are the file's, so
    /// the hex view never shows a re-encoding of the text.
    /// </summary>
    private async Task EnterHexAsync()
    {
        byte[] bytes = TextEditorControl.IsDirty
            ? TextBytes()
            : (_fileBytes ??= await ReadFileBytesAsync());

        HexEditorControl.Load(bytes);
        _hexChanged = false;
        _hexMode = true;
        TextEditorControl.ResetFocus();
        TextEditorControl.IsVisible = false;
        HexEditorControl.IsVisible = true;
        HexEditorControl.SetFocus();
        StateHasChanged();
    }

    /// <summary>
    /// Leaves the hex view for the text. Bytes that were edited, or that a binary is shown from, are read as text only when they
    /// read back unchanged in the file's code page; otherwise the editor stays in the hex view. Text that was not edited is
    /// read again when the file changed on disk while the hex view was shown.
    /// </summary>
    private async Task<bool> LeaveHexAsync()
    {
        if (_hexChanged || _binaryFile)
        {
            var encoding = TextEditorControl.TextModel.Encoding;
            if (!TryDecodeExactly(HexEditorControl.Data, encoding, TextEditorControl.TextModel.LineEnding, out string[] lines, out string lineEnding))
            {
                Title = string.Format(UiText.Get("Editor.NotText", "Not text: the bytes do not read back unchanged in {0}, so the hex view stays."), encoding.WebName);
                StateHasChanged();
                return false;
            }

            bool wasDirty = TextEditorControl.IsDirty;
            TextEditorControl.ReplaceAllText(lines, lineEnding, encoding);
            TextEditorControl.IsDirty = wasDirty;
            _hexChanged = false;
            _textStale = false;
        }
        else if (_textStale && _fileSystem is not null && Url is not null)
        {
            var text = new TextModel();
            await _fileToTextConverterService.ReadFileAsync(Url, _fileSystem, text);
            bool wasDirty = TextEditorControl.IsDirty;
            TextEditorControl.ReplaceAllText(text.Lines.AsSnapshot().ToArray(), text.LineEnding, text.Encoding);
            TextEditorControl.IsDirty = wasDirty;
            _textStale = false;
        }

        _hexMode = false;
        HexEditorControl.ResetFocus();
        HexEditorControl.IsVisible = false;
        TextEditorControl.IsVisible = true;
        TextEditorControl.SetFocus();
        StateHasChanged();
        return true;
    }

    /// <summary>A hex edit makes the file changed. The text is read again from these bytes when the hex view is left.</summary>
    private void OnHexEdited()
    {
        TextEditorControl.IsDirty = true;
        _hexChanged = true;
        StateHasChanged();
    }

    /// <summary>The text as it is saved: what the editor shows, written with its line break and code page.</summary>
    private byte[] TextBytes()
    {
        var model = TextEditorControl.TextModel;
        using var stream = string.Join(model.LineEnding, model.Lines).ToMemoryStream(model.Encoding);
        return stream.ToArray();
    }

    /// <summary>The file's bytes, read from the file system the editor was opened on.</summary>
    private async Task<byte[]> ReadFileBytesAsync()
    {
        var fileSystem = _fileSystem ?? _contentModelService.GetLastContentModel() as IFileSystem;
        if (Url is null || fileSystem is null)
        {
            return [];
        }

        var file = await fileSystem.ReadFileAsync(Url);
        return file?.Content ?? [];
    }

    /// <summary>
    /// Decodes <paramref name="bytes"/> as text, and only when encoding that text again gives back the same bytes: a byte order
    /// mark is kept or dropped as the file had it, and the line break is the one the file uses (the editor's when it has none).
    /// Mixed breaks, and bytes that are not valid in the code page, do not read back, so they are refused.
    /// </summary>
    internal static bool TryDecodeExactly(byte[] bytes, Encoding encoding, string defaultLineEnding, out string[] lines, out string lineEnding)
    {
        byte[] preamble = encoding.GetPreamble();
        bool hasByteOrderMark = preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble);
        string text = encoding.GetString(bytes, hasByteOrderMark ? preamble.Length : 0, bytes.Length - (hasByteOrderMark ? preamble.Length : 0));

        lineEnding = text.Contains("\r\n") ? "\r\n"
            : text.Contains('\n') ? "\n"
            : text.Contains('\r') ? "\r"
            : defaultLineEnding;
        lines = text.ReplaceLineEndings("\n").Split('\n');

        byte[] body = encoding.GetBytes(string.Join(lineEnding, lines));
        byte[] expected = hasByteOrderMark ? [.. preamble, .. body] : body;
        return expected.AsSpan().SequenceEqual(bytes);
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Ctrl+S", UiText.Get("Viewer.HintSave", "Save the file"));
        if (_hexMode)
        {
            yield return ("F4", UiText.Get("Editor.HintTextView", "Text view"));
            yield break;
        }

        yield return ("Tab", UiText.Get("Viewer.HintIndent", "Indent"));
        yield return ("F8", UiText.Get("Viewer.HintCodePage", "Next code page (Shift: previous)"));
        yield return ("Ctrl+F8", UiText.Get("Viewer.HintConvertEncoding", "Save in the next encoding (Shift: previous)"));
        yield return ("F4", UiText.Get("Editor.HintHexView", "Hex view"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // F4 switches between the text and the hex view. The hex view takes the hex digits itself, through its focus.
        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.F4
            && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            await ToggleHexModeAsync();
            return true;
        }

        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.F8
            && !keyEvent.Control && !keyEvent.Alt)
        {
            SwitchEncoding(backwards: keyEvent.Shift);
            return true;
        }

        // Ctrl+F8 changes the encoding the file is saved in, keeping the text; F8 re-reads it under another code page.
        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.F8
            && keyEvent.Control && !keyEvent.Alt)
        {
            ConvertEncoding(backwards: keyEvent.Shift);
            return true;
        }

        // Tab indents in the editor: it is not for moving between the status bar's buttons, which have keys of their own.
        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.Tab
            && !keyEvent.Control && !keyEvent.Alt
            && !_hexMode
            && TextEditorControl.IsEnabled)
        {
            return await TextEditorControl.HandleKeyEventAsync(keyEvent);
        }

        // Ctrl+S as well as F2: the key everybody types to save.
        if (keyEvent.KeyPressType == KeyPressType.Down
            && keyEvent.ConsoleKey == ConsoleKey.S
            && keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            if (TextEditorControl.IsDirty && !TextEditorControl.IsReadOnly)
            {
                await SaveAsync();
            }

            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>
    /// F8 / Shift+F8: reads the text under the next / previous code page, and saves with it. Unsaved
    /// changes are kept — the text is re-read in memory, not from the disk — and the file counts as
    /// changed, since saving now writes different bytes.
    /// </summary>
    public void SwitchEncoding(bool backwards = false)
    {
        if (_hexMode || !FileToTextConverterService.IsEncodingRelevant(Url))
        {
            return;
        }

        var model = TextEditorControl.TextModel;
        TextEncodings.Reinterpret(model, TextEncodings.Next(model.Encoding, backwards));
        TextEditorControl.IsDirty = true;
        TextEditorControl.Refresh();
        StateHasChanged();
    }

    /// <summary>
    /// Ctrl+F8 / Ctrl+Shift+F8: the file is saved in the next (previous) encoding. Unlike F8, the text is not read again:
    /// what is on screen is what is written, and the file counts as changed.
    /// </summary>
    public void ConvertEncoding(bool backwards = false)
    {
        if (TextEditorControl.IsReadOnly || _hexMode)
        {
            return;
        }

        var model = TextEditorControl.TextModel;
        model.Encoding = TextEncodings.Next(model.Encoding, backwards);
        TextEditorControl.IsDirty = true;
        StateHasChanged();
    }

    /// <summary>The address a backup is kept under: a local file's. Other drives (SFTP, archives, memory) are not backed up.</summary>
    private string? BackupKey => Url is { } url && string.Equals(url.UrlScheme, Container.FILE, StringComparison.OrdinalIgnoreCase)
        ? url.FullUrl
        : null;

    /// <summary>
    /// Backs up the unsaved text, on a worker thread, while it keeps changing: at most once per <see cref="BackupInterval"/>,
    /// and only when it differs from the last backup. Runs on the UI thread from <see cref="OnIdle"/>.
    /// </summary>
    private void MaybeBackup()
    {
        var store = _backups;
        // The text is backed up only when it is what the file would be saved as: not while bytes are shown or edited, or a binary.
        if (store is null || !TextEditorControl.IsDirty || TextEditorControl.IsReadOnly || _hexMode || _hexChanged || _binaryFile)
        {
            return;
        }

        var key = BackupKey;
        var now = Environment.TickCount64;
        if (key is null || now - _lastBackupTick < BackupInterval.TotalMilliseconds
            || Interlocked.CompareExchange(ref _backupRunning, 1, 0) != 0)
        {
            return;
        }

        _lastBackupTick = now;
        var model = TextEditorControl.TextModel;
        var lines = model.Lines.AsSnapshot().ToArray();
        if (_lastBackedUp is not null && lines.SequenceEqual(_lastBackedUp, StringComparer.Ordinal))
        {
            Interlocked.Exchange(ref _backupRunning, 0);
            return;
        }

        _lastBackedUp = lines;
        var backup = new EditorBackup(DateTime.UtcNow, lines, model.LineEnding, model.Encoding.WebName);
        PendingBackup = Task.Run(() => store.Write(key, backup)).ContinueWith(task =>
        {
            // Observing the exception keeps a failed write from being left unobserved; the next change tries again.
            _ = task.Exception;
            Interlocked.Exchange(ref _backupRunning, 0);
        }, TaskScheduler.Default);
    }

    /// <summary>Removes the backup of this file: it was saved, or closed with nothing unsaved.</summary>
    private void DeleteBackup()
    {
        var store = _backups;
        var key = BackupKey;
        _lastBackedUp = null;
        if (store is null || key is null)
        {
            return;
        }

        PendingBackup = Task.Run(() => store.Delete(key));
    }

    /// <summary>
    /// Puts back the unsaved text of a backup that is newer than the file on disk and differs from it, as unsaved edits. An
    /// older backup, or one that matches the file, is removed.
    /// </summary>
    private async Task RestoreBackupAsync()
    {
        var store = _backups;
        var key = BackupKey;
        if (store is null || key is null)
        {
            return;
        }

        var backup = await Task.Run(() => store.Read(key));
        if (backup is null)
        {
            return;
        }

        var model = TextEditorControl.TextModel;
        var sameAsFile = model.Lines.AsSnapshot().SequenceEqual(backup.Lines, StringComparer.Ordinal);
        if (StampOf(Url) is not { } stamp || backup.SavedAtUtc <= stamp.Modified || sameAsFile)
        {
            DeleteBackup();
            return;
        }

        var encoding = TextEncodings.Available.FirstOrDefault(e => string.Equals(e.WebName, backup.EncodingName, StringComparison.OrdinalIgnoreCase))
            ?? model.Encoding;
        TextEditorControl.ReplaceAllText(backup.Lines, backup.LineEnding, encoding);
        TextEditorControl.IsDirty = true;
        _lastBackedUp = backup.Lines;
        StateHasChanged();
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            ViewSize = Parent.ViewSize;
            Position = new Position(0, 0);
            // The title says how to leave the editor, as the viewer's does: nothing else on screen does.
            string closeHint = UiText.Get("Viewer.CloseHint", "  Esc:close");
            Title = (Url?.Shrink(Math.Max(4, (int)ViewSize.Width - 14 - closeHint.Length)) ?? "Text file editor") + closeHint;
        }

        TextEditorControl.Position = new(1, 1);
        // Ensure we never set negative dimensions when the dialog has no parent
        var editorWidth = Math.Max(0L, ViewSize.Width - 2);
        var editorHeight = Math.Max(0L, ViewSize.Height - 3);
        TextEditorControl.ViewSize = new(editorWidth, editorHeight);
        HexEditorControl.Position = new(1, 1);
        HexEditorControl.ViewSize = new(editorWidth, editorHeight);

        var statusY = Math.Max(0L, ViewSize.Height - 2);
        var statusWidth = Math.Max(0L, ViewSize.Width - 2);
        _statusBar.Position = new Position(1, statusY);
        _statusBar.ViewSize = new(statusWidth, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }
}
