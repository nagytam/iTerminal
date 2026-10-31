using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Localization;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A grid of text cells with a header row: the rows scroll up and down, and the columns scroll left and right so the cell
/// under the cursor is always on screen. Each column is as wide as its widest value in the first rows, within a limit, and a
/// value's line breaks and tabs show as spaces. The grid holds data only; the dialog that owns it decides what rows it shows.
/// </summary>
public class CsvGrid : Control
{
    /// <summary>How many rows are looked at to size the columns: a grid of a million rows does not need to measure all of them.</summary>
    internal const int WidthSampleRows = 500;
    internal const int MinColumnWidth = 3;
    internal const int MaxColumnWidth = 40;
    internal const string Separator = " │ ";

    private IReadOnlyList<string> _headers = [];
    private IReadOnlyList<string[]> _rows = [];
    private int[] _widths = [];
    private int _cursorRow;
    private int _cursorColumn;
    private int _firstRow;
    private int _firstColumn;

    public CsvGrid(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
    }

    private string _emptyText = string.Empty;

    /// <summary>What to show when there are no rows.</summary>
    public string EmptyText
    {
        get => _emptyText;
        set => _emptyText = value ?? string.Empty;
    }

    public int CursorRow => _cursorRow;

    public int CursorColumn => _cursorColumn;

    public int RowCount => _rows.Count;

    public int ColumnCount => _headers.Count;

    /// <summary>Replaces the grid's contents and puts the cursor on the first cell.</summary>
    public void SetData(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        _headers = headers;
        _rows = rows;
        _widths = ComputeWidths();
        _cursorRow = 0;
        _cursorColumn = 0;
        _firstRow = 0;
        _firstColumn = 0;
        StateHasChanged();
    }

    /// <summary>The text of the cell at a row and column, as the grid shows it (before it is cut to the column's width).</summary>
    public string CellAt(int row, int column) => Field(_rows[row], column);

    private int[] ComputeWidths()
    {
        var widths = new int[_headers.Count];
        for (int column = 0; column < widths.Length; column++)
        {
            int width = _headers[column].Length;
            for (int row = 0; row < _rows.Count && row < WidthSampleRows; row++)
            {
                width = Math.Max(width, Display(Field(_rows[row], column)).Length);
            }

            widths[column] = Math.Clamp(width, MinColumnWidth, MaxColumnWidth);
        }

        return widths;
    }

    private static string Field(string[] row, int column) => column < row.Length ? row[column] : string.Empty;

    private static string Display(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

    /// <summary>Whether the columns from <paramref name="first"/> to <paramref name="last"/>, with their separators, fit the width.</summary>
    private bool Fits(int first, int last)
    {
        int total = 0;
        for (int column = first; column <= last; column++)
        {
            total += _widths[column] + (column > first ? Separator.Length : 0);
        }

        return total <= ViewSize.Width;
    }

    private int BodyHeight => Math.Max(1, (int)ViewSize.Height - 2);

    /// <summary>Moves the scroll positions so the cursor's row and column are on screen.</summary>
    private void EnsureCursorVisible()
    {
        if (_widths.Length == 0 || _rows.Count == 0)
        {
            return;
        }

        _cursorRow = Math.Clamp(_cursorRow, 0, _rows.Count - 1);
        _cursorColumn = Math.Clamp(_cursorColumn, 0, _widths.Length - 1);
        if (_cursorRow < _firstRow)
        {
            _firstRow = _cursorRow;
        }
        else if (_cursorRow >= _firstRow + BodyHeight)
        {
            _firstRow = _cursorRow - BodyHeight + 1;
        }

        if (_cursorColumn < _firstColumn)
        {
            _firstColumn = _cursorColumn;
        }

        while (_firstColumn < _cursorColumn && !Fits(_firstColumn, _cursorColumn))
        {
            _firstColumn++;
        }
    }

    /// <summary>The keys the grid takes, for the help screen. A disabled grid takes none.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled)
        {
            yield break;
        }

        yield return ("↑↓", UiText.Get("Csv.Hint.MoveRows", "Move between the rows"));
        yield return ("←→", UiText.Get("Csv.Hint.MoveColumns", "Move between the columns"));
        yield return ("PgUp/PgDn", UiText.Get("Csv.Hint.Page", "Move a page at a time"));
        yield return ("Home/End", UiText.Get("Csv.Hint.Ends", "Go to the first or the last row"));
    }

    /// <summary>A left click inside the grid gives it the focus, as a click on any focusable control does.</summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (mouseEvent.LeftClicked && !mouseEvent.IsRelease && IsEnabled && IsVisible && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            SetFocus();
            return true;
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // An input control acts only while it is enabled and has the focus.
        if (!IsEnabled || !IsFocused || keyEvent.Control || keyEvent.Alt || keyEvent.Shift || _rows.Count == 0)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.DownArrow:
                _cursorRow++;
                break;
            case ConsoleKey.UpArrow:
                _cursorRow--;
                break;
            case ConsoleKey.PageDown:
                _cursorRow += BodyHeight;
                break;
            case ConsoleKey.PageUp:
                _cursorRow -= BodyHeight;
                break;
            case ConsoleKey.Home:
                _cursorRow = 0;
                break;
            case ConsoleKey.End:
                _cursorRow = _rows.Count - 1;
                break;
            case ConsoleKey.RightArrow:
                _cursorColumn++;
                break;
            case ConsoleKey.LeftArrow:
                _cursorColumn--;
                break;
            default:
                return await base.HandleKeyEventAsync(keyEvent);
        }

        EnsureCursorVisible();
        StateHasChanged();
        return true;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        int width = (int)ViewSize.Width;
        int height = (int)ViewSize.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        ColorScheme scheme = ThemeService.CurrentColorScheme;
        EnsureCursorVisible();
        if (_rows.Count == 0 || _widths.Length == 0)
        {
            // The placeholder row carries the state too: an empty grid still shows which state it is in.
            Color emptyText = IsEnabled ? scheme.Foreground : scheme.DisabledForeground;
            Color emptyBackground = IsFocused ? scheme.SelectedFocusedBackground : scheme.Background;
            WriteLine(buffer, 0, width, EmptyText, emptyText, emptyBackground);
            for (int y = 1; y < height; y++)
            {
                WriteLine(buffer, y, width, string.Empty, scheme.Foreground, scheme.Background);
            }

            return;
        }

        int last = FirstFittingLast();
        Color text = IsEnabled ? scheme.Foreground : scheme.DisabledForeground;
        Color headerBackground = IsFocused ? scheme.SelectedFocusedBackground : scheme.Background;
        WriteLine(buffer, 0, width, Compose(first: _firstColumn, last, i => _headers[i]), text, headerBackground);
        WriteLine(buffer, 1, width, new string('─', width), scheme.DisabledForeground, scheme.Background);

        for (int y = 2; y < height; y++)
        {
            int row = _firstRow + y - 2;
            if (row >= _rows.Count)
            {
                WriteLine(buffer, y, width, string.Empty, scheme.Foreground, scheme.Background);
                continue;
            }

            bool cursorRow = row == _cursorRow;
            Color background = cursorRow && IsFocused ? scheme.SelectedFocusedBackground : cursorRow ? scheme.SelectedBackground : scheme.Background;
            Color foreground = cursorRow && IsFocused ? scheme.SelectedFocusedForeground : cursorRow ? scheme.SelectedForeground : text;
            WriteLine(buffer, y, width, Compose(_firstColumn, last, column => Display(Field(_rows[row], column))), foreground, background);
            if (cursorRow && IsFocused)
            {
                WriteCell(buffer, y, _cursorColumn, Display(Field(_rows[row], _cursorColumn)), foreground, scheme.SelectedBackground);
            }
        }
    }

    /// <summary>The last column that fits on screen when the view starts at the first visible column.</summary>
    private int FirstFittingLast()
    {
        int last = _firstColumn;
        while (last + 1 < _widths.Length && Fits(_firstColumn, last + 1))
        {
            last++;
        }

        return last;
    }

    /// <summary>The cells from <paramref name="first"/> to <paramref name="last"/>, each padded to its column and joined by the separator.</summary>
    private string Compose(int first, int last, Func<int, string> valueAt)
    {
        var parts = new List<string>();
        for (int column = first; column <= last; column++)
        {
            parts.Add(Fit(valueAt(column), _widths[column]));
        }

        return string.Join(Separator, parts);
    }

    private static string Fit(string value, int width) => value.Cut(width).ExpandRight(width);

    private void WriteCell(IBuffer buffer, int y, int column, string value, Color foreground, Color background)
    {
        int x = 0;
        for (int c = _firstColumn; c < column; c++)
        {
            x += _widths[c] + Separator.Length;
        }

        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + x, AbsolutePosition.Y + y),
            new Size(_widths[column], 1),
            new ConxelString(Fit(value, _widths[column]), foreground, background));
    }

    private void WriteLine(IBuffer buffer, int y, int width, string text, Color foreground, Color background)
    {
        string line = (text.Length > width ? text.Cut(width) : text).ExpandRight(width);
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X, AbsolutePosition.Y + y),
            new Size(width, 1),
            new ConxelString(line, foreground, background));
    }
}
