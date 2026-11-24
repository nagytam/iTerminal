using iConsole.Controls;
using System.Text;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Services.Properties;
using iTerminal.Core.Services.Streams;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// The NTFS alternate data streams (Windows) or extended attributes (Linux, macOS) of the file or folder under the cursor:
/// name and size of each, a text or hex preview of the one under the dialog's cursor, and Delete.
///
/// <list type="bullet">
/// <item><b>Nothing waits on the disk.</b> The listing, each preview and the delete run on background tasks; a reply that is
/// no longer the current one (the cursor moved on, the list was read again, the dialog closed) is dropped
/// (<see cref="_generation"/>), and closing the dialog cancels what is in flight.</item>
/// <item><b>Delete asks first, in the footer.</b> Del arms a question that names the stream; <c>y</c> removes it and any other key
/// (Esc included) keeps it. There is no recycle bin behind it. Deleting <c>Zone.Identifier</c> is "unblock this file" and
/// is meant to work.</item>
/// <item><b>Text or hex is picked from the bytes</b>: valid UTF-8 without control characters shows as text, anything else as a
/// hex dump; <c>H</c> switches. Only the first <see cref="PreviewBytes"/> bytes are read, and the preview says so when
/// there is more. An empty stream says it is empty.</item>
/// <item>Local files and folders only; the command refuses the rest, and never acts on the <c>..</c> row.</item>
/// </list>
/// </summary>
public class StreamsDialog : Dialog
{
    internal const int PreviewBytes = 64 * 1024;

    private const int DialogWidth = 84;
    private const int DialogHeight = 24;
    private const int ListRows = 7;

    private readonly IStreamAttributeService _service;
    private readonly Lock _sync = new();

    private CancellationTokenSource _cancellation = new();
    private IReadOnlyList<StreamItem> _items = [];
    private StreamData? _content;
    private string? _contentName;
    private bool _contentIsBinary;
    private bool? _forceHex;
    private bool _loaded;
    // The list the table shows. The worker parks its result in _items; ApplyPendingItems takes it on the UI thread.
    private readonly ConcurrentObservableCollection<StreamItem> _queue = [];
    private readonly Table<StreamItem> _table;
    private IReadOnlyList<StreamItem> _appliedItems = [];
    private string? _previewName;
    private string? _listFailure;
    private bool _confirmDelete;
    private string _status = string.Empty;
    private bool _statusIsError;
    private int _generation;
    private int _previewGeneration;

    public StreamsDialog(IStreamAttributeService service, IThemeService themeService)
        : base(nameof(StreamsDialog), themeService)
    {
        Title = UiText.Get("Dialog.Streams", "Streams and attributes");
        _service = service;
        _table = new Table<StreamItem>(_queue, themeService)
        {
            EmptyText = UiText.Get("Streams.Reading", "Reading…"),
        };
        ConfigureColumns();
        AddControl(_table);
    }

    /// <summary>The file or folder whose streams are shown.</summary>
    public string? TargetPath { get; private set; }

    /// <summary>The last read or delete; nothing waits on it, it exists so a test can.</summary>
    public Task PendingOperation { get; private set; } = Task.CompletedTask;

    /// <summary>Sets what to look at; call before showing the dialog.</summary>
    public void Load(string path) => TargetPath = path;

    public override async Task ShowAsync()
    {
        lock (_sync)
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = new CancellationTokenSource();
            _items = [];
            _content = null;
            _contentName = null;
            _forceHex = null;
            _loaded = false;
            _listFailure = null;
            _confirmDelete = false;
            _status = string.Empty;
            _statusIsError = false;
            Title = UiText.Get("Dialog.Streams", "Streams and attributes") + " — " + _service.Kind;
        }

        ApplyPendingItems();
        StartList();
        await base.ShowAsync();
    }

    public override void Close()
    {
        lock (_sync)
        {
            _cancellation.Cancel();
        }

        base.Close();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(Math.Min(DialogWidth, Math.Max(40, (int)Parent.ViewSize.Width - 2)), DialogHeight));
        }

        // The header is the first row of the table; the entries follow it, ListRows of them.
        _table.Position = new Position(1, 2);
        _table.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), ListRows + 1);
        _table.ScrollableModel.ViewSize = new Size(_table.ViewSize.Width, ListRows);
        ConfigureColumns();
        MoveCursor(0);

        base.OnInitializeLayout();
    }

    #region Reading

    /// <summary>Reads the list, then the preview of the entry the cursor ends on.</summary>
    internal void StartList()
    {
        string? path = TargetPath;
        CancellationToken token;
        int generation;
        lock (_sync)
        {
            token = _cancellation.Token;
            generation = Interlocked.Increment(ref _generation);
        }

        if (path is null)
        {
            lock (_sync)
            {
                _loaded = true;
            }

            return;
        }

        PendingOperation = Task.Run(async () =>
        {
            try
            {
                StreamListing listing = await _service.ListAsync(path, token);
                lock (_sync)
                {
                    if (generation != Volatile.Read(ref _generation) || token.IsCancellationRequested)
                    {
                        return;
                    }

                    _items = listing.Items;
                    _listFailure = listing.Failure;
                    _loaded = true;
                }
            }
            catch (OperationCanceledException)
            {
                // Intentionally empty: there is nothing to do for this case.
            }
            catch (Exception exception)
            {
                lock (_sync)
                {
                    _loaded = true;
                    _listFailure = exception.Message;
                }
            }

            StateHasChanged();
        }, CancellationToken.None);
    }

    private void StartPreview()
    {
        string? path = TargetPath;
        StreamItem? selected = Selected;
        CancellationToken token = _cancellation.Token;
        if (path is null || selected is null)
        {
            return;
        }

        // The entry the cursor is on, written here on the UI thread: the read below runs in the background and must not read the table.
        // The number is taken here too: a read started earlier must not be able to take a later one, and then win.
        int generation = Interlocked.Increment(ref _previewGeneration);
        lock (_sync)
        {
            _previewName = selected.Name;
        }

        PendingOperation = Task.Run(async () =>
        {
            try
            {
                await ReadPreviewAsync(path, selected.Name, generation, token);
            }
            catch (OperationCanceledException)
            {
                // Intentionally empty: there is nothing to do for this case.
            }

            StateHasChanged();
        }, CancellationToken.None);
    }

    private async Task ReadPreviewAsync(string path, string name, int generation, CancellationToken token)
    {
        StreamData content;
        try
        {
            content = await _service.ReadAsync(path, name, PreviewBytes, token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            content = new StreamData([], 0, exception.Message);
        }

        lock (_sync)
        {
            // The cursor moved on to another entry, or the dialog closed: this answer is old news.
            if (generation != Volatile.Read(ref _previewGeneration) || token.IsCancellationRequested || _previewName != name)
            {
                return;
            }

            _content = content;
            _contentName = name;
            _contentIsBinary = LooksLikeBinary(content); // Once, here, not on every redraw.
        }
    }

    #endregion

    #region Keys

    private StreamItem? Selected => _table.SelectedRow;

    /// <summary>The highlighted entry's row, which is the table's cursor.</summary>
    private int Cursor => (int)_table.ScrollableModel.CursorPosition.Y;

    /// <summary>
    /// Takes the list a read left, on the UI thread. The preview follows the entry the cursor is on, and a preview of an
    /// entry that is no longer there is dropped.
    /// </summary>
    private void ApplyPendingItems()
    {
        IReadOnlyList<StreamItem> items;
        lock (_sync)
        {
            if (ReferenceEquals(_items, _appliedItems))
            {
                return;
            }

            items = _items;
            _appliedItems = items;
        }

        _queue.ReplaceAll(items.ToList());
        MoveCursor(0);

        string? wanted = Selected?.Name;
        lock (_sync)
        {
            if (wanted != _contentName)
            {
                _content = null;
                _contentName = null;
            }
        }

        if (Selected is not null)
        {
            StartPreview();
        }
    }

    /// <summary>Moves the highlight to <paramref name="index"/> (clamped to the list) and scrolls it into view.</summary>
    private void MoveCursor(int index)
    {
        var model = _table.ScrollableModel;
        model.CursorPosition.Y = Math.Clamp(index, 0, Math.Max(0, _queue.Count - 1));

        // Before the first layout the table has no height, and scrolling to a cursor in an empty view would hide row 0.
        if (model.ViewSize.Height > 0)
        {
            model.ScrollToVisible();
        }

        model.Update();
    }

    public override void OnIdle()
    {
        base.OnIdle();
        ApplyPendingItems();
        lock (_sync)
        {
            _table.EmptyText = !_loaded
                ? UiText.Get("Streams.Reading", "Reading…")
                : _listFailure ?? string.Format(UiText.Get("Streams.None", "This entry has no {0}."), _service.Kind.ToLowerInvariant());
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        bool confirming;
        lock (_sync)
        {
            confirming = _confirmDelete;
        }

        if (confirming)
        {
            yield return ("Y", UiText.Get("DialogHint.DeleteStreamYes", "Delete the stream or attribute"));
            yield return ("Any other key", UiText.Get("DialogHint.KeepStream", "Keep it"));
            yield break;
        }

        yield return ("Up/Down", UiText.Get("DialogHint.ChooseStream", "Choose a stream or attribute"));
        yield return ("H", UiText.Get("DialogHint.StreamHexText", "Switch the preview between text and hex"));
        yield return ("Delete", UiText.Get("DialogHint.DeleteStreamAsks", "Delete it (asks first)"));
        yield return ("F5", UiText.Get("DialogHint.StreamsRefresh", "Read the list again"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // A key acts on the list as it is read, not as it was drawn the last time the dialog was idle.
        ApplyPendingItems();
        if (HandleKey(keyEvent))
        {
            StateHasChanged();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private bool HandleKey(KeyEvent keyEvent)
    {
        bool plain = !keyEvent.Control && !keyEvent.Alt;
        bool moved = false;
        bool handled;
        lock (_sync)
        {
            if (_confirmDelete)
            {
                // Answered by y; any other key (Esc too) only withdraws the question, so Esc never closes by accident here.
                _confirmDelete = false;
                if (plain && char.ToLowerInvariant(keyEvent.KeyChar) == 'y')
                {
                    DeleteSelected();
                }
                else
                {
                    SetStatus(string.Empty, false);
                }

                return true;
            }

            if (!plain)
            {
                return false;
            }

            handled = true;
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.UpArrow when Cursor > 0:
                    MoveCursor(Cursor - 1);
                    moved = true;
                    break;
                case ConsoleKey.DownArrow when Cursor < _queue.Count - 1:
                    MoveCursor(Cursor + 1);
                    moved = true;
                    break;
                case ConsoleKey.UpArrow or ConsoleKey.DownArrow:
                    break;
                case ConsoleKey.Home when Cursor != 0:
                    MoveCursor(0);
                    moved = true;
                    break;
                case ConsoleKey.End when Cursor != _queue.Count - 1 && _queue.Count > 0:
                    MoveCursor(_queue.Count - 1);
                    moved = true;
                    break;
                case ConsoleKey.F5:
                    SetStatus(string.Empty, false);
                    StartList();
                    break;
                case ConsoleKey.Delete:
                    if (Selected is { } stream)
                    {
                        _confirmDelete = true;
                        SetStatus(string.Format(UiText.Get("Streams.Status.AskDelete", "Delete {0}? y deletes it."), stream.Name), false);
                    }

                    break;
                default:
                    if (char.ToLowerInvariant(keyEvent.KeyChar) == 'h')
                    {
                        _forceHex = !(_forceHex ?? _contentIsBinary);
                    }
                    else
                    {
                        handled = false;
                    }

                    break;
            }

            if (moved)
            {
                _content = null;
                _contentName = null;
            }
        }

        if (moved)
        {
            StartPreview();
        }

        return handled;
    }

    private void DeleteSelected()
    {
        string? path = TargetPath;
        StreamItem? stream = Selected;
        CancellationToken token = _cancellation.Token;
        if (path is null || stream is null)
        {
            return;
        }

        PendingOperation = Task.Run(async () =>
        {
            try
            {
                StreamResult result = await _service.DeleteAsync(path, stream.Name, token);
                lock (_sync)
                {
                    if (!token.IsCancellationRequested)
                    {
                        SetStatus(result.Message, !result.Success);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                lock (_sync)
                {
                    SetStatus(exception.Message, true);
                }
            }

            // Read again, so the list shows what the disk says rather than what was expected.
            lock (_sync)
            {
                _content = null;
                _contentName = null;
            }

            StartList();
            StateHasChanged();
        }, CancellationToken.None);
    }

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
    }

    #endregion

    #region Drawing

    private const int SizeWidth = 12;

    /// <summary>Name takes the width that is left; the size keeps its column on the right.</summary>
    private void ConfigureColumns()
    {
        int nameWidth = Math.Max(8, (int)_table.ViewSize.Width - SizeWidth - 1);
        _table.Columns.Clear();
        _table.Columns.Add(new ColumnDefinition<StreamItem>(UiText.Get("Streams.Col.Name", "Name"), nameWidth, item => item.Name));
        _table.Columns.Add(new ColumnDefinition<StreamItem>(UiText.Get("Streams.Col.Size", "Size"), SizeWidth, item => Compact(item.Size), TextAlignment.Right));
    }

    /// <summary>Whether the bytes are not text: invalid UTF-8, or a control character other than tab, CR and LF.</summary>
    internal static bool LooksLikeBinary(StreamData? content)
    {
        if (content is null || content.Bytes.Length == 0)
        {
            return false;
        }

        try
        {
            string text = new UTF8Encoding(false, true).GetString(content.Bytes);
            return text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n'));
        }
        catch (ArgumentException)
        {
            // A cut in the middle of a multi-byte character at the end of a truncated read is still text.
            return !(content.IsTruncated && IsValidBeforeCut(content.Bytes));
        }
    }

    private static bool IsValidBeforeCut(byte[] bytes)
    {
        for (int trim = 1; trim <= 3 && trim < bytes.Length; trim++)
        {
            try
            {
                string text = new UTF8Encoding(false, true).GetString(bytes, 0, bytes.Length - trim);
                return !text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n'));
            }
            catch (ArgumentException)
            {
                // Intentionally empty: there is nothing to do for this case.
            }
        }

        return false;
    }

    /// <summary>A classic hex dump: offset, sixteen bytes, and the printable characters.</summary>
    internal static IReadOnlyList<string> HexLines(byte[] bytes, int maxLines)
    {
        List<string> lines = [];
        for (int offset = 0; offset < bytes.Length && lines.Count < maxLines; offset += 16)
        {
            int count = Math.Min(16, bytes.Length - offset);
            StringBuilder hex = new();
            StringBuilder ascii = new();
            for (int i = 0; i < 16; i++)
            {
                if (i < count)
                {
                    byte b = bytes[offset + i];
                    hex.Append(b.ToString("x2")).Append(' ');
                    ascii.Append(b is >= 0x20 and < 0x7f ? (char)b : '.');
                }
                else
                {
                    hex.Append("   ");
                }
            }

            lines.Add($"{offset:x6}  {hex} {ascii}");
        }

        return lines;
    }

    /// <summary>The text split into lines, tabs widened so a drawn row is as wide as it looks.</summary>
    internal static IReadOnlyList<string> TextLines(byte[] bytes, int maxLines)
    {
        string text = Encoding.UTF8.GetString(bytes).Replace("\t", "    ");
        List<string> lines = [];
        foreach (string line in text.Split('\n'))
        {
            if (lines.Count >= maxLines)
            {
                break;
            }

            lines.Add(line.TrimEnd('\r').Select(c => char.IsControl(c) ? '·' : c).Aggregate(new StringBuilder(), (sb, c) => sb.Append(c)).ToString());
        }

        return lines;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        int iw = (int)ViewSize.Width - 2;
        int ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 8)
        {
            return;
        }

        ColorScheme colors = ThemeService.CurrentColorScheme;
        lock (_sync)
        {
            for (int row = 0; row < ih; row++)
            {
                Put(buffer, 0, row, iw, string.Empty, colors.Foreground, colors.Background);
            }

            string target = TargetPath ?? string.Empty;
            Put(buffer, 1, 0, iw - 2, UiText.Get("Streams.Target", "File").PadRight(9) + target, colors.Foreground, colors.Background);

            DrawPreview(buffer, iw, ih, colors);

            Put(buffer, 1, ih - 2, iw - 2, _status, _statusIsError ? colors.Error : colors.Foreground, colors.Background);
            Put(buffer, 1, ih - 1, iw - 2, _confirmDelete
                ? UiText.Get("Streams.Hint.Confirm", "y deletes it  any other key keeps it")
                : UiText.Get("Streams.Hint.List", "↑↓ choose  H text/hex  Del delete  F5 refresh  Esc close").FitHints(iw - 2),
                colors.Foreground, colors.Background);
        }
    }

    private void DrawPreview(IBuffer buffer, int iw, int ih, ColorScheme colors)
    {
        int top = 3 + ListRows + 1;
        int rows = ih - 2 - top - 1;
        if (rows <= 0)
        {
            return;
        }

        string header;
        IReadOnlyList<string> lines = [];
        if (Selected is not { } selected)
        {
            header = UiText.Get("Streams.Preview", "Preview");
        }
        else if (_content is null || _contentName != selected.Name)
        {
            header = UiText.Get("Streams.Preview", "Preview") + "  " + selected.Name;
            lines = [UiText.Get("Streams.Reading", "Reading…")];
        }
        else if (_content.Failure is { } failure)
        {
            header = UiText.Get("Streams.Preview", "Preview") + "  " + selected.Name;
            lines = [failure];
        }
        else
        {
            bool hex = _forceHex ?? _contentIsBinary;
            string mode = hex ? UiText.Get("Streams.Mode.Hex", "hex") : UiText.Get("Streams.Mode.Text", "text");
            header = $"{UiText.Get("Streams.Preview", "Preview")}  {selected.Name}  ({mode})";
            if (_content.IsTruncated)
            {
                header += "  " + string.Format(UiText.Get("Streams.Truncated", "first {0} of {1}"), Compact(_content.Bytes.Length), Compact(_content.TotalSize));
            }

            if (_content.Bytes.Length == 0) lines = [UiText.Get("Streams.Empty", "(empty)")];
            else if (hex) lines = HexLines(_content.Bytes, rows);
            else lines = TextLines(_content.Bytes.AsSpan(0, Math.Min(_content.Bytes.Length, rows * iw * 4)).ToArray(), rows); // Only what can be seen is decoded.
        }

        Put(buffer, 1, top - 1, iw - 2, header, colors.DisabledForeground, colors.Background);
        for (int i = 0; i < rows && i < lines.Count; i++)
        {
            Put(buffer, 1, top + i, iw - 2, lines[i], _content?.Failure is not null ? colors.Error : colors.Foreground, colors.Background);
        }
    }

    private void Put(IBuffer buffer, int x, int y, int width, string text, Color foreground, Color background)
    {
        if (width <= 0)
        {
            return;
        }

        string clipped = text.Length > width ? text[..width] : text.PadRight(width);
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1 + x, AbsolutePosition.Y + 1 + y),
            new Size(width, 1),
            new ConxelString(clipped, foreground, background));
    }

    private static string Compact(long bytes) => bytes.ToFileSize().Trim().Replace("  ", " ");

    #endregion

    #region Test seams

    internal string StatusForTest { get { lock (_sync) return _status; } }
    internal int CountForTest { get { lock (_sync) return _items.Count; } }
    internal int CursorForTest => Cursor;

    #endregion
}
