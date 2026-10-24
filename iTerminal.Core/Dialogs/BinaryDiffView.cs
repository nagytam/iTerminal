using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iTerminal.Core.Services.Compare;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// The two files as hex, side by side, with the bytes that differ in the error colour. A row holds as many bytes as the
/// columns have room for (a power of two, so the offsets line up), and the view scrolls by row. N and P jump to the next and
/// the previous run of differing bytes.
/// </summary>
internal sealed class BinaryDiffView(IThemeService themeService) : Control(themeService)
{
    /// <summary>The characters a row spends besides its bytes: the offset, its gap, the gap before the text, and the text's own spacing.</summary>
    private const int RowOverhead = 11;
    private const int MaxBytesPerRow = 32;

    private readonly Lock _lock = new();
    private byte[] _left = [];
    private byte[] _right = [];
    private BinaryDiffResult? _result;
    private long _top;

    public int PageSize => Math.Max(1, (int)ViewSize.Height - 1);

    /// <summary>The bytes a row shows at this width: the largest power of two (from 4) that fits a column.</summary>
    public static int BytesPerRowFor(int totalWidth)
    {
        var column = Math.Max(1, (totalWidth - 1) / 2);
        var fit = (column - RowOverhead) / 4;
        var bytes = 4;
        while (bytes * 2 <= fit && bytes < MaxBytesPerRow)
        {
            bytes *= 2;
        }

        return bytes;
    }

    /// <summary>Sets the two files and their comparison, and scrolls to the first difference.</summary>
    public void SetData(byte[] left, byte[] right, BinaryDiffResult result)
    {
        lock (_lock)
        {
            _left = left;
            _right = right;
            _result = result;
            var first = result.Runs.Count > 0 ? result.Runs[0].Start / BytesPerRowFor((int)ViewSize.Width) : 0;
            _top = Math.Max(0, first - 1) * BytesPerRowFor((int)ViewSize.Width);
        }

        StateHasChanged();
    }

    /// <summary>The number of the difference the view is on, counting from one; zero above the first.</summary>
    public int CurrentDifference()
    {
        lock (_lock)
        {
            if (_result is null)
            {
                return 0;
            }

            var bytesPerRow = BytesPerRowFor((int)ViewSize.Width);
            var row = TopRow(bytesPerRow) + 1;
            var index = 0;
            for (var i = 0; i < _result.Runs.Count; i++)
            {
                if (_result.Runs[i].Start / bytesPerRow <= row)
                {
                    index = i + 1;
                }
            }

            return index;
        }
    }

    public void ScrollBy(int rows)
    {
        lock (_lock)
        {
            var bytesPerRow = BytesPerRowFor((int)ViewSize.Width);
            _top = ClampRow(TopRow(bytesPerRow) + rows) * bytesPerRow;
        }

        StateHasChanged();
    }

    /// <summary>Scrolls to a row; int.MaxValue is the last page.</summary>
    public void ScrollTo(int row)
    {
        lock (_lock)
        {
            var bytesPerRow = BytesPerRowFor((int)ViewSize.Width);
            _top = ClampRow(row) * bytesPerRow;
        }

        StateHasChanged();
    }

    /// <summary>Moves to the next (or previous) run of differing bytes, a row above it so it is not at the very top.</summary>
    public void JumpToDifference(bool next)
    {
        lock (_lock)
        {
            if (_result is null || _result.Runs.Count == 0)
            {
                return;
            }

            var bytesPerRow = BytesPerRowFor((int)ViewSize.Width);
            // The row the difference is on after the margin: a run one row above it is already on screen, not the next.
            var current = TopRow(bytesPerRow) + 1;
            long? target = null;
            foreach (var run in _result.Runs)
            {
                var row = run.Start / bytesPerRow;
                if (next && row > current)
                {
                    target = row;
                    break;
                }

                if (!next && row < current)
                {
                    target = row;
                }
            }

            if (target is { } found)
            {
                _top = ClampRow(Math.Max(0, found - 1)) * bytesPerRow;
            }
        }

        StateHasChanged();
    }

    private long TopRow(int bytesPerRow) => _top / bytesPerRow;

    private int ClampRow(long row)
    {
        var bytesPerRow = BytesPerRowFor((int)ViewSize.Width);
        var longest = Math.Max(_left.Length, _right.Length);
        var rows = (longest + bytesPerRow - 1) / bytesPerRow;
        return (int)Math.Clamp(row, 0, Math.Max(0, rows - (int)ViewSize.Height));
    }

    public override void Draw(IBuffer buffer)
    {
        byte[] left;
        byte[] right;
        BinaryDiffResult? result;
        long top;
        lock (_lock)
        {
            left = _left;
            right = _right;
            result = _result;
            top = _top;
        }

        if (ViewSize.IsEmpty)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        var width = (int)ViewSize.Width;
        var column = Math.Max(1, (width - 1) / 2);
        var rightWidth = Math.Max(0, width - column - 1);
        var bytesPerRow = BytesPerRowFor(width);
        var separator = new Conxel('│', colorScheme.Foreground, colorScheme.Background);

        for (var row = 0; row < (int)ViewSize.Height; row++)
        {
            var start = top + (long)row * bytesPerRow;
            var cells = new Conxel[width];
            Array.Fill(cells, new Conxel(' ', colorScheme.Foreground, colorScheme.Background));
            WriteSide(cells, 0, column, left, result, start, bytesPerRow, colorScheme);
            cells[column] = separator;
            if (rightWidth > 0)
            {
                WriteSide(cells, column + 1, rightWidth, right, result, start, bytesPerRow, colorScheme);
            }

            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + row), new Size(width, 1), new ConxelString(cells));
        }
    }

    /// <summary>One file's row: the offset, the bytes in hex, then the same bytes as text. Bytes past the end leave blanks.</summary>
    private static void WriteSide(Conxel[] cells, int x, int width, byte[] bytes, BinaryDiffResult? result, long start, int bytesPerRow, ColorScheme colorScheme)
    {
        if (width < RowOverhead + 4 * bytesPerRow)
        {
            return;
        }

        var background = colorScheme.Background;
        for (var i = 0; i < 8; i++)
        {
            cells[x + i] = new Conxel(start.ToString("X8")[i], colorScheme.DisabledForeground, background);
        }

        for (var j = 0; j < bytesPerRow; j++)
        {
            var offset = start + j;
            if (offset >= bytes.Length)
            {
                break;
            }

            var value = bytes[offset];
            var color = result is not null && result.IsDifferent(offset) ? colorScheme.Error : colorScheme.Foreground;
            var hex = value.ToString("X2");
            var hexX = x + 10 + 3 * j;
            cells[hexX] = new Conxel(hex[0], color, background);
            cells[hexX + 1] = new Conxel(hex[1], color, background);
            // The text starts after the hex groups and one more space.
            cells[x + 11 + 3 * bytesPerRow + j] = new Conxel(value is >= 0x20 and < 0x7F ? (char)value : '.', color, background);
        }
    }
}
