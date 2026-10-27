using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Layouts;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>
/// A grid of framed cells whose borders are shared and joined: neighbouring cells use one line between them, and where lines
/// meet the glyph is a <c>┬</c>, <c>├</c> or <c>┼</c> (through <see cref="LineCanvas"/>) instead of two boxes pressed together.
/// Tracks are sized like <see cref="GridPanel"/> (<see cref="SizeRule"/>), the lines take one cell each, a cell may span
/// several tracks, and a cell's <c>title</c> is set into its top line. A track with no cell in it draws no lines.
/// </summary>
public class BorderedGrid : Control
{
    private sealed record Cell(IControl? Content, int Row, int Column, int RowSpan, int ColumnSpan, string Title);

    private readonly List<Cell> _cells = [];
    private IReadOnlyList<SizeRule> _columns = [SizeRule.Fill()];
    private IReadOnlyList<SizeRule> _rows = [SizeRule.Fill()];
    private BorderStyle _borderStyle = BorderStyle.Single;

    public BorderedGrid(IThemeService themeService) : base(themeService)
    {
        CanFocus = false;
        ViewSize = new Size(40, 12);
    }

    public BorderStyle BorderStyle
    {
        get => _borderStyle;
        set
        {
            _borderStyle = value;
            StateHasChanged();
        }
    }

    /// <summary>The column tracks, left to right.</summary>
    public void SetColumns(params SizeRule[] rules)
    {
        _columns = rules.Length == 0 ? [SizeRule.Fill()] : [.. rules];
        StateHasChanged();
    }

    /// <summary>The row tracks, top to bottom.</summary>
    public void SetRows(params SizeRule[] rules)
    {
        _rows = rules.Length == 0 ? [SizeRule.Fill()] : [.. rules];
        StateHasChanged();
    }

    /// <summary>Puts <paramref name="content"/> (or just a frame, when null) in a cell, which may span tracks. Rows and columns past the last track are held to it.</summary>
    public void Add(IControl? content, int row, int column, int rowSpan = 1, int columnSpan = 1, string title = "")
    {
        row = Math.Clamp(row, 0, _rows.Count - 1);
        column = Math.Clamp(column, 0, _columns.Count - 1);
        rowSpan = Math.Clamp(rowSpan, 1, _rows.Count - row);
        columnSpan = Math.Clamp(columnSpan, 1, _columns.Count - column);
        _cells.Add(new Cell(content, row, column, rowSpan, columnSpan, title ?? string.Empty));
        if (content is not null)
        {
            AddControl(content);
        }

        StateHasChanged();
    }

    /// <summary>The cell's lines as columns and rows of the grid: x of the left and right line, y of the top and bottom line.</summary>
    public (int Left, int Top, int Right, int Bottom)? FrameOf(IControl content)
    {
        Cell? cell = _cells.Find(c => ReferenceEquals(c.Content, content));
        return cell is null ? null : Frame(cell, TrackLines(_columns, (int)ViewSize.Width), TrackLines(_rows, (int)ViewSize.Height));
    }

    /// <summary>The positions of the lines along one axis: n+1 of them for n tracks, each track taking its share of what the lines leave.</summary>
    private static int[] TrackLines(IReadOnlyList<SizeRule> rules, int length)
    {
        int[] sizes = SizeSolver.Solve(Math.Max(0, length - (rules.Count + 1)), rules);
        int[] lines = new int[rules.Count + 1];
        for (int i = 0; i < rules.Count; i++)
        {
            lines[i + 1] = lines[i] + sizes[i] + 1;
        }

        return lines;
    }

    private static (int Left, int Top, int Right, int Bottom) Frame(Cell cell, int[] columnLines, int[] rowLines)
    {
        return (columnLines[cell.Column], rowLines[cell.Row], columnLines[cell.Column + cell.ColumnSpan], rowLines[cell.Row + cell.RowSpan]);
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        LayoutContent();
    }

    private void LayoutContent()
    {
        int[] columnLines = TrackLines(_columns, (int)ViewSize.Width);
        int[] rowLines = TrackLines(_rows, (int)ViewSize.Height);
        foreach (Cell cell in _cells)
        {
            if (cell.Content is null)
            {
                continue;
            }

            (int left, int top, int right, int bottom) = Frame(cell, columnLines, rowLines);
            cell.Content.Position = new Position(left + 1, top + 1);
            cell.Content.ViewSize = new Size(Math.Max(0, right - left - 1), Math.Max(0, bottom - top - 1));
        }
    }

    public override void Draw(IBuffer buffer)
    {
        if (ViewSize.IsEmpty)
        {
            return;
        }

        int width = (int)ViewSize.Width;
        int height = (int)ViewSize.Height;
        ColorScheme scheme = ThemeService.CurrentColorScheme;
        iConsole.Buffers.Buffer.Clear(buffer, ZOrder, AbsolutePosition, ViewSize, scheme);

        int[] columnLines = TrackLines(_columns, width);
        int[] rowLines = TrackLines(_rows, height);
        var canvas = new LineCanvas(width, height);
        foreach (Cell cell in _cells)
        {
            (int left, int top, int right, int bottom) = Frame(cell, columnLines, rowLines);
            canvas.AddRectangle(left, top, right - left + 1, bottom - top + 1, _borderStyle);
        }

        for (int y = 0; y < height; y++)
        {
            var row = new ConxelString(string.Empty, scheme.Foreground, scheme.Background);
            for (int x = 0; x < width; x++)
            {
                row.Append(canvas.GlyphAt(x, y), scheme.Foreground, scheme.Background);
            }

            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + y), new Size(width, 1), row);
        }

        foreach (Cell cell in _cells.Where(c => c.Title.Length > 0))
        {
            (int left, int top, int right, _) = Frame(cell, columnLines, rowLines);
            int room = right - left - 3;
            if (room <= 0 || top >= height)
            {
                continue;
            }

            string title = " " + (cell.Title.Length > room - 2 ? cell.Title[..Math.Max(0, room - 2)] : cell.Title) + " ";
            buffer.Write(
                ZOrder,
                new Position(AbsolutePosition.X + left + 2, AbsolutePosition.Y + top),
                new Size(title.Length, 1),
                new ConxelString(title, scheme.SelectedForeground, scheme.Background));
        }

        LayoutContent();
        base.Draw(buffer);
    }
}
