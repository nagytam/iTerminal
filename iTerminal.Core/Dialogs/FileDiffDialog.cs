using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services.Compare;
using System.Text;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Ctrl+F10: two files side by side with their differences marked — mc's "Compare files". Which two
/// is the command's business (<see cref="Commands.DiffFilesDialogCommand"/>); the dialog is handed
/// them with <see cref="Load"/>.
///
/// <list type="bullet">
/// <item><b>Reading and diffing run on a background task</b> (<see cref="LoadTask"/>, the seam a
/// test waits on); the dialog says "Reading…" meanwhile, and closing it cancels the read.</item>
/// <item><b>Any drive</b>: a local file is read directly, anything else through the
/// <see cref="IFileSystem"/> the command resolved while the panel still had focus.</item>
/// <item><b>Text only, and bounded</b>: a file with a NUL byte near its start is binary and is
/// compared by its bytes, not shown; a file over <see cref="MaxBytes"/> is refused in words.</item>
/// <item><b>N / P</b> jump to the next / previous difference; <b>W</b> ignores changes in
/// whitespace and <b>C</b> ignores case, each re-running the diff on what was read.</item>
/// </list>
/// </summary>
public class FileDiffDialog : Dialog
{
    /// <summary>The largest file the dialog reads, each.</summary>
    public const long MaxBytes = 8L * 1024 * 1024;

    private const int NumberWidth = 5;
    private const int TabWidth = 4;

    private readonly DiffView _view;
    private readonly BinaryDiffView _hexView;
    private byte[]? _leftBytes;
    private byte[]? _rightBytes;
    private volatile BinaryDiffResult? _binary;
    private volatile bool _hexMode;
    private CancellationTokenSource? _cancellation;
    private string[]? _leftLines;
    private string[]? _rightLines;
    private volatile string _status = string.Empty;
    private volatile bool _statusIsError;
    private volatile string _leftName = string.Empty;
    private volatile string _rightName = string.Empty;
    private volatile bool _ignoreWhitespace;
    private volatile bool _ignoreCase;
    private Url? _leftUrl;
    private Url? _rightUrl;
    private IFileSystem? _leftFileSystem;
    private IFileSystem? _rightFileSystem;

    public FileDiffDialog(IThemeService themeService)
        : base(nameof(FileDiffDialog), themeService)
    {
        Title = iConsole.Localization.UiText.Get("Dialog.CompareFiles", "Compare files");
        _view = new DiffView(themeService);
        _hexView = new BinaryDiffView(themeService);
        AddControl(_view);
        AddControl(_hexView);
    }

    /// <summary>True while the two files are shown as hex (F4 switches a text file between its lines and its bytes).</summary>
    public bool IsHexMode => _hexMode;

    /// <summary>The byte-by-byte comparison of the two files, once they are read.</summary>
    public BinaryDiffResult? Binary => _binary;

    /// <summary>The read-and-compare started last; nothing waits on it — it exists so a test can.</summary>
    public Task LoadTask { get; private set; } = Task.CompletedTask;

    public TextDiffResult? Result { get; private set; }

    public string Status => _status;

    public bool IgnoreWhitespace => _ignoreWhitespace;

    public bool IgnoreCase => _ignoreCase;

    /// <summary>The row the view starts at, and which of the differences it is on (1-based; 0 before the first).</summary>
    public int TopRow => _view.Top;

    /// <summary>Hands the dialog the two files. Takes effect when the dialog is shown.</summary>
    public void Load(Url left, IFileSystem? leftFileSystem, Url right, IFileSystem? rightFileSystem)
    {
        _leftUrl = left;
        _rightUrl = right;
        _leftFileSystem = leftFileSystem;
        _rightFileSystem = rightFileSystem;
        _leftName = left.FullUrl;
        _rightName = right.FullUrl;
        _ignoreWhitespace = false;
        _ignoreCase = false;
        _leftLines = null;
        _rightLines = null;
        _leftBytes = null;
        _rightBytes = null;
        _binary = null;
        _hexMode = false;
        Result = null;
        _view.SetResult(null);
        SetStatus("Reading…", isError: false);
        StartLoad();
    }

    private void StartLoad()
    {
        var left = _leftUrl;
        var right = _rightUrl;
        if (left is null || right is null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref _cancellation, cancellation)?.Cancel();
        var token = cancellation.Token;
        var leftFileSystem = _leftFileSystem;
        var rightFileSystem = _rightFileSystem;

        LoadTask = Task.Run(async () =>
        {
            try
            {
                var leftBytes = await ReadAsync(left, leftFileSystem, token);
                var rightBytes = await ReadAsync(right, rightFileSystem, token);
                var binary = BinaryComparer.Compare(leftBytes, rightBytes);
                _leftBytes = leftBytes;
                _rightBytes = rightBytes;
                _binary = binary;
                _hexView.SetData(leftBytes, rightBytes, binary);

                if (IsBinary(leftBytes) || IsBinary(rightBytes))
                {
                    // No lines to compare: the bytes are the comparison, and the hex view is what shows them.
                    _hexMode = true;
                    SetStatus(
                        binary.Identical
                            ? "Binary files — identical, byte for byte."
                            : $"Binary files — they differ ({leftBytes.Length:N0} and {rightBytes.Length:N0} bytes): {binary.DifferingBytes:N0} bytes in {binary.Runs.Count:N0} place(s). Shift+F12 shows their checksums.",
                        isError: false);
                    return;
                }

                _leftLines = SplitLines(Decode(leftBytes));
                _rightLines = SplitLines(Decode(rightBytes));
                Recompute();
            }
            catch (OperationCanceledException)
            {
                // Closed, or replaced by a newer load.
            }
            catch (Exception exception)
            {
                SetStatus($"Could not read the files: {exception.Message}", isError: true);
            }
        });
    }

    /// <summary>F4: switches between a text file's lines and its bytes. A binary file has no lines, so it stays in hex.</summary>
    private void ToggleHex()
    {
        if (_leftLines is null || _rightLines is null || _binary is not { } binary)
        {
            return;
        }

        _hexMode = !_hexMode;
        SetStatus(_hexMode ? BinaryStatus(binary) : Result?.Summary() ?? _status, isError: false);
        ApplyMode();
    }

    private static string BinaryStatus(BinaryDiffResult binary) =>
        binary.Identical
            ? "Identical, byte for byte."
            : $"{binary.DifferingBytes:N0} bytes differ in {binary.Runs.Count:N0} place(s).";

    /// <summary>Shows the view of the mode in use and hides the other; each is only touched when it changes.</summary>
    private void ApplyMode()
    {
        SetVisible(_view, !_hexMode);
        SetVisible(_hexView, _hexMode);
    }

    private static void SetVisible(Control control, bool visible)
    {
        if (control.IsVisible != visible)
        {
            control.IsVisible = visible;
        }
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        // ShowAsync makes every child visible; the mode decides which one stays.
        ApplyMode();
    }

    /// <summary>The files are read on a background task, so the mode is applied here, on the thread that owns the controls.</summary>
    public override void OnIdle()
    {
        base.OnIdle();
        ApplyMode();
    }

    private void Recompute()
    {
        var left = _leftLines;
        var right = _rightLines;
        if (left is null || right is null)
        {
            return;
        }

        var result = TextDiff.Compare(left, right, new TextDiffOptions(_ignoreWhitespace, _ignoreCase));
        Result = result;
        _view.SetResult(result);
        SetStatus(result.Summary(), isError: false);
    }

    private static async Task<byte[]> ReadAsync(Url url, IFileSystem? fileSystem, CancellationToken cancellationToken)
    {
        if (fileSystem is null)
        {
            if (url.ContainerType != ContainerType.FileSystem)
            {
                throw new NotSupportedException("this drive cannot be read here");
            }

            var path = Path.TrimEndingDirectorySeparator(url.Path);
            var length = new FileInfo(path).Length;
            if (length > MaxBytes)
            {
                throw new IOException($"{Path.GetFileName(path)} is {length:N0} bytes; files over {MaxBytes / (1024 * 1024)} MB are not compared");
            }

            return await File.ReadAllBytesAsync(path, cancellationToken);
        }

        using var buffer = new LimitedStream(MaxBytes);
        await fileSystem.StreamToAsync(url, buffer, null, cancellationToken);
        return buffer.ToArray();
    }

    internal static bool IsBinary(byte[] bytes)
    {
        var count = Math.Min(bytes.Length, 8192);
        return bytes.AsSpan(0, count).IndexOf((byte)0) >= 0 && !HasUtf16ByteOrderMark(bytes);
    }

    private static bool HasUtf16ByteOrderMark(byte[] bytes) =>
        bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF));

    internal static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException)
        {
            // Never UTF-8 with replacement characters: Latin-1 keeps every byte as itself.
            return Encoding.Latin1.GetString(bytes, start, bytes.Length - start);
        }
    }

    /// <summary>Splits on \r\n, \n and a lone \r; a final line break does not make an empty last line.</summary>
    internal static string[] SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n' || text[i] == '\r')
            {
                lines.Add(text[start..i]);
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return [.. lines];
    }

    private void SetStatus(string status, bool isError)
    {
        _status = status;
        _statusIsError = isError;
        StateHasChanged();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            var width = Math.Clamp((int)Parent.ViewSize.Width - 4, 60, 220);
            var height = Math.Clamp((int)Parent.ViewSize.Height - 2, 12, 60);
            CenterInParent(new Size(width, height));
        }

        _view.Position = new Position(1, 3);
        _view.ViewSize = new Size(Math.Max(1, ViewSize.Width - 2), Math.Max(1, ViewSize.Height - 5));
        _hexView.Position = _view.Position;
        _hexView.ViewSize = _view.ViewSize;
        base.OnInitializeLayout();
    }

    public override void Close()
    {
        Interlocked.Exchange(ref _cancellation, null)?.Cancel();
        base.Close();
    }

    /// <summary>The wheel scrolls the comparison three lines a notch, wherever in the dialog the pointer is.</summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            Scroll(mouseEvent.WheelDelta > 0 ? -3 : 3);
            StateHasChanged();
            return true;
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Up/Down", "Scroll the differences");
        yield return ("PageUp/PageDown", "Scroll a page at a time");
        yield return ("Home/End", "Jump to the start or the end");
        if (!_hexMode)
        {
            yield return ("Left/Right", "Scroll sideways");
        }

        yield return ("N/P", "Jump to the next or previous difference");
        yield return ("F4", "Switch between the text and the hex view");
        if (!_hexMode)
        {
            yield return ("W", "Ignore white space");
            yield return ("C", "Ignore letter case");
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (!keyEvent.Alt && !keyEvent.Control)
        {
            var handled = true;
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.UpArrow:
                    Scroll(-1);
                    break;
                case ConsoleKey.DownArrow:
                    Scroll(1);
                    break;
                case ConsoleKey.PageUp:
                    Scroll(-CurrentPageSize());
                    break;
                case ConsoleKey.PageDown:
                    Scroll(CurrentPageSize());
                    break;
                case ConsoleKey.Home:
                    ScrollTo(0);
                    break;
                case ConsoleKey.End:
                    ScrollTo(int.MaxValue);
                    break;
                case ConsoleKey.LeftArrow when !_hexMode:
                    _view.ScrollSideways(-8);
                    break;
                case ConsoleKey.RightArrow when !_hexMode:
                    _view.ScrollSideways(8);
                    break;
                case ConsoleKey.N:
                    Jump(next: true);
                    break;
                case ConsoleKey.P:
                    Jump(next: false);
                    break;
                case ConsoleKey.F4:
                    ToggleHex();
                    break;
                case ConsoleKey.W when !_hexMode:
                    _ignoreWhitespace = !_ignoreWhitespace;
                    RecomputeInBackground();
                    break;
                case ConsoleKey.C when !_hexMode:
                    _ignoreCase = !_ignoreCase;
                    RecomputeInBackground();
                    break;
                default:
                    handled = false;
                    break;
            }

            if (handled)
            {
                StateHasChanged();
                return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private void RecomputeInBackground() => LoadTask = Task.Run(Recompute);

    private void Scroll(int rows)
    {
        if (_hexMode)
        {
            _hexView.ScrollBy(rows);
        }
        else
        {
            _view.ScrollBy(rows);
        }
    }

    private void ScrollTo(int row)
    {
        if (_hexMode)
        {
            _hexView.ScrollTo(row);
        }
        else
        {
            _view.ScrollTo(row);
        }
    }

    private int CurrentPageSize() => _hexMode ? _hexView.PageSize : _view.PageSize;

    private void Jump(bool next)
    {
        if (_hexMode)
        {
            _hexView.JumpToDifference(next);
        }
        else
        {
            _view.JumpToDifference(next);
        }
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        var width = (int)ViewSize.Width;
        var inner = Math.Max(0, width - 2);
        var column = _view.ColumnWidth(inner);

        void Write(int x, int row, string text, int length, Color color) =>
            buffer.Write(ZOrder, new Position(AbsolutePosition.X + x, AbsolutePosition.Y + row), new Size(length, 1),
                new ConxelString((text.Shrink(length) ?? string.Empty).ExpandRight(length), color, colorScheme.Background));

        Write(1, 1, " " + _leftName, column, Color.Cyan);
        Write(1 + column, 1, "│", 1, foreground);
        Write(2 + column, 1, " " + _rightName, Math.Max(0, inner - column - 1), Color.Yellow);

        var rule = new ConxelString(Border.Horizontal.Repeat(inner), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + 2), new Size(rule.Count, 1), rule);

        string options;
        string position;
        if (_hexMode)
        {
            options = " [hex]";
            position = _binary is { Runs.Count: > 0 } binary ? $"  ({_hexView.CurrentDifference()}/{binary.Runs.Count})" : string.Empty;
        }
        else
        {
            options = (_ignoreWhitespace ? " [ignore whitespace]" : string.Empty) + (_ignoreCase ? " [ignore case]" : string.Empty);
            position = Result is { HunkStarts.Count: > 0 } result ? $"  ({_view.CurrentDifference(result)}/{result.HunkStarts.Count})" : string.Empty;
        }

        Write(1, (int)ViewSize.Height - 2, " " + _status + position + options, inner, _statusIsError ? colorScheme.Error : foreground);

        // F4 only means something where there are lines to go back to.
        var toggle = _leftLines is null ? string.Empty : _hexMode ? "  F4:text" : "  F4:hex";
        var hints = _hexMode
            ? $" N/P:next/prev difference{toggle}  ↑↓ PgUp PgDn wheel  Esc:close ".FitHints(Math.Max(0, width - 4))
            : $" N/P:next/prev difference  W:whitespace  C:case{toggle}  ↑↓ PgUp PgDn wheel ←→:scroll  Esc:close ".FitHints(Math.Max(0, width - 4));
        var bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, hints), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }

    /// <summary>The rows of the diff, two columns with a rule between them.</summary>
    private sealed class DiffView(IThemeService themeService) : Control(themeService)
    {
        private readonly Lock _lock = new();
        private TextDiffResult? _result;
        private int _top;
        private int _left;

        public int PageSize => Math.Max(1, (int)ViewSize.Height - 1);

        public int Top
        {
            get
            {
                lock (_lock)
                {
                    return _top;
                }
            }
        }

        public int ColumnWidth(int totalWidth) => Math.Max(1, (totalWidth - 1) / 2);

        public void SetResult(TextDiffResult? result)
        {
            lock (_lock)
            {
                _result = result;
                _top = result is { HunkStarts.Count: > 0 } ? Math.Max(0, result.HunkStarts[0] - 2) : 0;
                _left = 0;
            }

            StateHasChanged();
        }

        public void ScrollBy(int step)
        {
            lock (_lock)
            {
                _top = Clamp(_top + step);
            }
        }

        public void ScrollTo(int row)
        {
            lock (_lock)
            {
                _top = Clamp(row);
            }
        }

        public void ScrollSideways(int step)
        {
            lock (_lock)
            {
                _left = Math.Max(0, _left + step);
            }
        }

        /// <summary>Moves to the first row of the next (or previous) difference after the one on the first row.</summary>
        public void JumpToDifference(bool next)
        {
            lock (_lock)
            {
                if (_result is not { HunkStarts.Count: > 0 } result)
                {
                    return;
                }

                // The difference "under" the view is the last one that starts at or above its top
                // line plus the margin it is scrolled to.
                var current = CurrentIndex(result);
                var target = next ? Math.Min(current + 1, result.HunkStarts.Count - 1) : Math.Max(current - 1, 0);
                if (current >= 0 && result.HunkStarts[current] - 2 > _top && !next)
                {
                    target = current;
                }

                _top = Clamp(Math.Max(0, result.HunkStarts[target] - 2));
            }
        }

        /// <summary>1-based number of the difference the view is on, 0 above the first.</summary>
        public int CurrentDifference(TextDiffResult result)
        {
            lock (_lock)
            {
                return CurrentIndex(result) + 1;
            }
        }

        private int CurrentIndex(TextDiffResult result)
        {
            var index = -1;
            for (var i = 0; i < result.HunkStarts.Count; i++)
            {
                if (result.HunkStarts[i] - 2 <= _top)
                {
                    index = i;
                }
            }

            return index;
        }

        private int Clamp(int row)
        {
            var count = _result?.Rows.Count ?? 0;
            return Math.Clamp(row, 0, Math.Max(0, count - (int)ViewSize.Height));
        }

        public override void Draw(IBuffer buffer)
        {
            TextDiffResult? result;
            int top;
            int left;
            lock (_lock)
            {
                result = _result;
                top = _top;
                left = _left;
            }

            var colorScheme = ThemeService.CurrentColorScheme;
            var width = (int)ViewSize.Width;
            var column = ColumnWidth(width);
            var rightWidth = Math.Max(0, width - column - 1);
            var separator = new ConxelString("│", colorScheme.Foreground, colorScheme.Background);

            for (var row = 0; row < (int)ViewSize.Height; row++)
            {
                var index = top + row;
                var leftText = string.Empty;
                var rightText = string.Empty;
                var leftColor = colorScheme.Foreground;
                var rightColor = colorScheme.Foreground;

                if (result is not null && index < result.Rows.Count)
                {
                    var diff = result.Rows[index];
                    leftText = Cell(diff.LeftNumber, diff.Left, column, left);
                    rightText = Cell(diff.RightNumber, diff.Right, rightWidth, left);
                    switch (diff.Kind)
                    {
                        case DiffRowKind.LeftOnly:
                            leftColor = Color.Cyan;
                            break;
                        case DiffRowKind.RightOnly:
                            rightColor = Color.Yellow;
                            break;
                        case DiffRowKind.Changed:
                            leftColor = colorScheme.Error;
                            rightColor = colorScheme.Error;
                            break;
                    }
                }

                // Every cell of the rectangle, every time: a shorter diff must not leave old rows behind.
                var y = AbsolutePosition.Y + row;
                var x = AbsolutePosition.X;
                buffer.Write(ZOrder, new Position(x, y), new Size(column, 1),
                    new ConxelString(leftText.ExpandRight(column)[..column], leftColor, colorScheme.Background));
                buffer.Write(ZOrder, new Position(x + column, y), new Size(1, 1), separator);
                if (rightWidth > 0)
                {
                    buffer.Write(ZOrder, new Position(x + column + 1, y), new Size(rightWidth, 1),
                        new ConxelString(rightText.ExpandRight(rightWidth)[..rightWidth], rightColor, colorScheme.Background));
                }
            }
        }

        private static string Cell(int? number, string? text, int width, int horizontalOffset)
        {
            if (number is null || text is null)
            {
                return string.Empty;
            }

            var shown = Printable(text);
            shown = horizontalOffset >= shown.Length ? string.Empty : shown[horizontalOffset..];
            var line = number.Value.ToString().PadLeft(NumberWidth) + " " + shown;
            return line.Length > width ? line[..Math.Max(0, width)] : line;
        }

        private static string Printable(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (c == '\t')
                {
                    builder.Append(' ', TabWidth);
                }
                else
                {
                    builder.Append(char.IsControl(c) ? '·' : c);
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>A memory stream that refuses to grow past a limit — a remote file is not sized up front.</summary>
    private sealed class LimitedStream(long limit) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            Check(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer.Length);
            base.Write(buffer);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Check(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Check(count);
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        private void Check(int count)
        {
            if (Length + count > limit)
            {
                throw new IOException($"the file is over {limit / (1024 * 1024)} MB; files that large are not compared");
            }
        }
    }
}
