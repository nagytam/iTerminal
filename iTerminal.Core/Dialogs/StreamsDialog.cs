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
    private int _cursor;
    private bool _loaded;
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
            _cancellation = new CancellationTokenSource();
            _items = [];
            _content = null;
            _contentName = null;
            _forceHex = null;
            _cursor = 0;
            _loaded = false;
            _listFailure = null;
            _confirmDelete = false;
            _status = string.Empty;
            _statusIsError = false;
            Title = UiText.Get("Dialog.Streams", "Streams and attributes") + " — " + _service.Kind;
        }

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
                string? wanted;
                lock (_sync)
                {
                    if (generation != Volatile.Read(ref _generation) || token.IsCancellationRequested)
                    {
                        return;
                    }

                    _items = listing.Items;
                    _listFailure = listing.Failure;
                    _loaded = true;
                    _cursor = Math.Clamp(_cursor, 0, Math.Max(0, _items.Count - 1));
                    wanted = Selected?.Name;
                    if (wanted != _contentName)
                    {
                        _content = null;
                        _contentName = null;
                    }
                }

                if (wanted is not null)
                {
                    await ReadPreviewAsync(path, wanted, token);
                }
            }
            catch (OperationCanceledException)
            {
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
        });
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

        PendingOperation = Task.Run(async () =>
        {
            try
            {
                await ReadPreviewAsync(path, selected.Name, token);
            }
            catch (OperationCanceledException)
            {
            }

            StateHasChanged();
        });
    }

    private async Task ReadPreviewAsync(string path, string name, CancellationToken token)
    {
        int generation = Interlocked.Increment(ref _previewGeneration);
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
            if (generation != Volatile.Read(ref _previewGeneration) || token.IsCancellationRequested || Selected?.Name != name)
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

    private StreamItem? Selected => _cursor >= 0 && _cursor < _items.Count ? _items[_cursor] : null;

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
                case ConsoleKey.UpArrow when _cursor > 0:
                    _cursor--;
                    moved = true;
                    break;
                case ConsoleKey.DownArrow when _cursor < _items.Count - 1:
                    _cursor++;
                    moved = true;
                    break;
                case ConsoleKey.UpArrow or ConsoleKey.DownArrow:
                    break;
                case ConsoleKey.Home when _cursor != 0:
                    _cursor = 0;
                    moved = true;
                    break;
                case ConsoleKey.End when _cursor != _items.Count - 1 && _items.Count > 0:
                    _cursor = _items.Count - 1;
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
        });
    }

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
    }

    #endregion

    #region Drawing

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

            int sizeWidth = 12;
            int nameWidth = Math.Max(8, iw - 2 - 2 - sizeWidth - 1);
            Put(buffer, 1, 2, iw - 2, "  " + UiText.Get("Streams.Col.Name", "Name").PadRight(nameWidth) + " " + UiText.Get("Streams.Col.Size", "Size").PadLeft(sizeWidth), colors.DisabledForeground, colors.Background);

            if (_items.Count == 0)
            {
                string empty = !_loaded
                    ? UiText.Get("Streams.Reading", "Reading…")
                    : _listFailure ?? string.Format(UiText.Get("Streams.None", "This entry has no {0}."), _service.Kind.ToLowerInvariant());
                Put(buffer, 1, 3, iw - 2, empty, _listFailure is not null && _loaded ? colors.Error : colors.DisabledForeground, colors.Background);
            }

            int first = _cursor >= ListRows ? _cursor - ListRows + 1 : 0;
            for (int i = 0; i < ListRows; i++)
            {
                int index = first + i;
                if (index >= _items.Count)
                {
                    continue;
                }

                StreamItem item = _items[index];
                bool highlighted = index == _cursor;
                Put(buffer, 1, 3 + i, iw - 2,
                    (highlighted ? "> " : "  ") + Clip(item.Name, nameWidth).PadRight(nameWidth) + " " + Compact(item.Size).PadLeft(sizeWidth),
                    highlighted ? colors.SelectedFocusedForeground : colors.Foreground,
                    highlighted ? colors.SelectedFocusedBackground : colors.Background);
            }

            DrawPreview(buffer, iw, ih, colors);

            Put(buffer, 1, ih - 2, iw - 2, _status, _statusIsError ? colors.Error : Color.Cyan, colors.Background);
            Put(buffer, 1, ih - 1, iw - 2, _confirmDelete
                ? UiText.Get("Streams.Hint.Confirm", "y deletes it  any other key keeps it")
                : UiText.Get("Streams.Hint.List", "↑↓ choose  H text/hex  Del delete  F5 refresh  Esc close"),
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

            lines = _content.Bytes.Length == 0
                ? [UiText.Get("Streams.Empty", "(empty)")]
                : hex ? HexLines(_content.Bytes, rows) : TextLines(_content.Bytes.AsSpan(0, Math.Min(_content.Bytes.Length, rows * iw * 4)).ToArray(), rows); // Only what can be seen is decoded.
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

    private static string Clip(string text, int width) => text.Length <= width ? text : text[..Math.Max(0, width - 1)] + "…";

    #endregion

    #region Test seams

    internal string StatusForTest { get { lock (_sync) return _status; } }
    internal int CountForTest { get { lock (_sync) return _items.Count; } }
    internal int CursorForTest { get { lock (_sync) return _cursor; } }

    #endregion
}
