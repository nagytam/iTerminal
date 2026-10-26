using iConsole.Data;
using iConsole.Display;
using iConsole.Layouts;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>
/// Lays its children out on a grid. Columns and rows are tracks, each sized by a <see cref="SizeRule"/> (fixed cells,
/// a percentage, or a weighted share of what is left); a child sits in one cell or spans several rows and columns.
/// The tracks are worked out again whenever the panel is drawn, so a resize needs no code from the owner.
/// A child that is not visible keeps its cells empty. A child placed outside the grid is clamped to its last track.
/// The panel is not focusable; Tab moves to whatever it contains.
/// </summary>
public class GridPanel : Control
{
    private readonly record struct Cell(IControl Control, int Row, int Column, int RowSpan, int ColumnSpan);

    private readonly List<Cell> _cells = [];
    private List<SizeRule> _columns = [SizeRule.Fill()];
    private List<SizeRule> _rows = [SizeRule.Fill()];
    private int _columnGap;
    private int _rowGap;

    public GridPanel(IThemeService themeService) : base(themeService)
    {
        CanFocus = false;
        ViewSize = new Size(30, 10);
    }

    /// <summary>Empty cells between neighbouring columns. Negative values count as 0.</summary>
    public int ColumnGap
    {
        get => _columnGap;
        set => Change(ref _columnGap, Math.Max(0, value));
    }

    /// <summary>Empty cells between neighbouring rows. Negative values count as 0.</summary>
    public int RowGap
    {
        get => _rowGap;
        set => Change(ref _rowGap, Math.Max(0, value));
    }

    public IReadOnlyList<SizeRule> Columns => _columns;

    public IReadOnlyList<SizeRule> Rows => _rows;

    public IReadOnlyList<IControl> Items => [.. _cells.Select(cell => cell.Control)];

    /// <summary>Replaces the column tracks. An empty list leaves one column that fills the width.</summary>
    public void SetColumns(params SizeRule[] rules)
    {
        _columns = rules.Length == 0 ? [SizeRule.Fill()] : [.. rules];
        Relayout();
    }

    /// <summary>Replaces the row tracks. An empty list leaves one row that fills the height.</summary>
    public void SetRows(params SizeRule[] rules)
    {
        _rows = rules.Length == 0 ? [SizeRule.Fill()] : [.. rules];
        Relayout();
    }

    /// <summary>Puts <paramref name="control"/> at <paramref name="row"/>, <paramref name="column"/>, covering the given number of tracks (at least one).</summary>
    public void Add(IControl control, int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        _cells.Add(new Cell(control, Math.Max(0, row), Math.Max(0, column), Math.Max(1, rowSpan), Math.Max(1, columnSpan)));
        AddControl(control);
        Relayout();
    }

    public void Remove(IControl control)
    {
        if (_cells.RemoveAll(cell => ReferenceEquals(cell.Control, control)) > 0)
        {
            RemoveControl(control);
            Relayout();
        }
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        LayoutChildren();
    }

    private void Change(ref int field, int value)
    {
        if (field != value)
        {
            field = value;
            Relayout();
        }
    }

    private void Relayout()
    {
        LayoutChildren();
        StateHasChanged();
    }

    private void LayoutChildren()
    {
        int[] widths = SizeSolver.Solve((int)ViewSize.Width, _columns, _columnGap);
        int[] heights = SizeSolver.Solve((int)ViewSize.Height, _rows, _rowGap);
        int[] left = Offsets(widths, _columnGap);
        int[] top = Offsets(heights, _rowGap);
        foreach (Cell cell in _cells)
        {
            int column = Math.Min(cell.Column, widths.Length - 1);
            int row = Math.Min(cell.Row, heights.Length - 1);
            int lastColumn = Math.Min(column + cell.ColumnSpan, widths.Length) - 1;
            int lastRow = Math.Min(row + cell.RowSpan, heights.Length) - 1;
            cell.Control.Position = new Position(left[column], top[row]);
            cell.Control.ViewSize = new Size(left[lastColumn] + widths[lastColumn] - left[column], top[lastRow] + heights[lastRow] - top[row]);
        }
    }

    private static int[] Offsets(int[] sizes, int gap)
    {
        int[] offsets = new int[sizes.Length];
        int offset = 0;
        for (int i = 0; i < sizes.Length; i++)
        {
            offsets[i] = offset;
            offset += sizes[i] + gap;
        }

        return offsets;
    }

    public override void Draw(IBuffer buffer)
    {
        if (ViewSize.IsEmpty)
        {
            return;
        }

        iConsole.Buffers.Buffer.Clear(buffer, ZOrder, AbsolutePosition, ViewSize, ThemeService.CurrentColorScheme);
        LayoutChildren();
        base.Draw(buffer);
    }
}
