namespace iConsole.Tabular;

/// <summary>
/// The data behind a grid viewer, as an N-dimensional array of text cells. A table is the two-axis case (rows and columns); a
/// dataset of three or more dimensions is the general case. A source reads a block on request, so a viewer never holds more
/// than the cells it shows.
/// </summary>
public interface IGridArraySource
{
    /// <summary>The length of each axis, in order; the last axis varies fastest. Empty for a scalar.</summary>
    IReadOnlyList<long> Shape { get; }

    /// <summary>The names of the columns of a two-axis array, such as a file's headers; empty when the columns are only numbered.</summary>
    IReadOnlyList<string> ColumnNames { get; }

    /// <summary>
    /// The field names of a one-dimensional compound array: one column per field, and each element is one row of cells. Empty for
    /// every other array.
    /// </summary>
    IReadOnlyList<string> Fields { get; }

    /// <summary>
    /// The cells of a block: where it starts and how many it spans along each axis. The cells come back row-major, the last axis
    /// fastest, and a compound element contributes one cell per field.
    /// </summary>
    Task<IReadOnlyList<string>> ReadBlockAsync(long[] start, long[] count, CancellationToken cancellationToken);
}

/// <summary>A two-dimensional window of an array as a table: the headers, the rows, and whether a cap cut either axis short.</summary>
public sealed record GridTable(IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows, bool RowsTruncated, bool ColumnsTruncated);

/// <summary>
/// A view of an N-dimensional array: which two axes are the rows and the columns, and the index of every other axis. The operations
/// work on the axes: <see cref="WithIndex"/> and <see cref="Step"/> fix or move an axis, <see cref="Project"/> and
/// <see cref="Transpose"/> choose the rows and columns. The view is immutable; each operation returns a new one.
/// <see cref="MaterializeAsync"/> reads the window that is shown, so the query engine runs over the table it returns.
/// </summary>
public sealed class GridArrayView
{
    private readonly IGridArraySource _source;
    private readonly long[] _index;

    private GridArrayView(IGridArraySource source, long[] index, int rowAxis, int columnAxis)
    {
        _source = source;
        _index = index;
        RowAxis = rowAxis;
        ColumnAxis = columnAxis;
    }

    /// <summary>The axis shown as rows, or -1 for a scalar.</summary>
    public int RowAxis { get; }

    /// <summary>The axis shown as columns, or -1 when there is one column (a one-dimensional array, or a scalar).</summary>
    public int ColumnAxis { get; }

    /// <summary>The length of each axis.</summary>
    public IReadOnlyList<long> Shape => _source.Shape;

    /// <summary>The number of axes.</summary>
    public int Rank => _source.Shape.Count;

    /// <summary>The axes that are not shown: each is fixed at one index.</summary>
    public IReadOnlyList<int> FixedAxes =>
        [.. Enumerable.Range(0, Rank).Where(axis => axis != RowAxis && axis != ColumnAxis)];

    /// <summary>The index of an axis. For a shown axis this is the first index, which is what a window starts at.</summary>
    public long IndexOf(int axis) => _index[axis];

    /// <summary>The first view of an array: the last two axes are the rows and columns, every other axis is at index 0.</summary>
    public static GridArrayView Open(IGridArraySource source)
    {
        int rank = source.Shape.Count;
        int rowAxis = rank == 0 ? -1 : rank == 1 ? 0 : rank - 2;
        int columnAxis = rank >= 2 ? rank - 1 : -1;
        return new GridArrayView(source, new long[rank], rowAxis, columnAxis);
    }

    /// <summary>Fixes a shown-or-fixed axis at an index, clamped to the axis. A shown axis cannot be fixed; choose the rows and columns with <see cref="Project"/>.</summary>
    public GridArrayView WithIndex(int axis, long index)
    {
        CheckAxis(axis);
        if (axis == RowAxis || axis == ColumnAxis)
        {
            throw new ArgumentException($"Axis {axis} is shown as rows or columns, so it has no index.", nameof(axis));
        }

        long[] index2 = [.. _index];
        index2[axis] = Math.Clamp(index, 0, Math.Max(0, Shape[axis] - 1));
        return new GridArrayView(_source, index2, RowAxis, ColumnAxis);
    }

    /// <summary>Moves a fixed axis by <paramref name="delta"/>, clamped to the axis.</summary>
    public GridArrayView Step(int axis, long delta) => WithIndex(axis, _index[axis] + delta);

    /// <summary>
    /// Chooses which two axes are shown as rows and columns. An axis that becomes fixed keeps the index it was at, so stepping back
    /// returns to where the view was.
    /// </summary>
    public GridArrayView Project(int rowAxis, int columnAxis)
    {
        if (Rank < 2)
        {
            throw new InvalidOperationException("Only an array of two or more axes has rows and columns to choose.");
        }

        CheckAxis(rowAxis);
        CheckAxis(columnAxis);
        if (rowAxis == columnAxis)
        {
            throw new ArgumentException("The rows and the columns must be different axes.", nameof(columnAxis));
        }

        return new GridArrayView(_source, _index, rowAxis, columnAxis);
    }

    /// <summary>Swaps the rows and the columns.</summary>
    public GridArrayView Transpose() => Rank < 2 ? this : Project(ColumnAxis, RowAxis);

    /// <summary>
    /// The position in words, as the slice line shows it: the fixed index of each axis, and <c>*</c> for an axis that is shown,
    /// such as <c>[1, *, *]</c>.
    /// </summary>
    public string Describe()
    {
        var parts = new List<string>(Rank);
        for (int axis = 0; axis < Rank; axis++)
        {
            parts.Add(axis == RowAxis || axis == ColumnAxis ? "*" : _index[axis].ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return "[" + string.Join(", ", parts) + "]";
    }

    /// <summary>
    /// Reads the window that is shown: at most <paramref name="maxRows"/> rows, <paramref name="maxColumns"/> columns and
    /// <paramref name="maxCells"/> cells (the rows are cut first). Only the cells of the window are read. The table has a row-number
    /// column first unless <paramref name="withRowNumbers"/> is false, which is how a file's rows are queried.
    /// </summary>
    public async Task<GridTable> MaterializeAsync(int maxRows, int maxColumns, int maxCells, CancellationToken cancellationToken, bool withRowNumbers = true)
    {
        if (Rank == 0)
        {
            IReadOnlyList<string> scalar = await _source.ReadBlockAsync([], [], cancellationToken);
            return new GridTable(["value"], [[scalar.Count > 0 ? scalar[0] : string.Empty]], false, false);
        }

        IReadOnlyList<string> fields = _source.Fields;
        bool byField = ColumnAxis < 0 && fields.Count > 0;
        long rowsAvailable = RowAxis >= 0 ? Shape[RowAxis] : 1;
        long columnsAvailable = ColumnAxis >= 0 ? Shape[ColumnAxis] : byField ? fields.Count : 1;
        if (rowsAvailable == 0 || columnsAvailable == 0)
        {
            // Nothing to read, but the columns are still named: a file with headers and no rows keeps its headers.
            return new GridTable(Headers(0, byField, fields, withRowNumbers), [], false, false);
        }

        int columns = (int)Math.Min(columnsAvailable, Math.Max(1, maxColumns));
        int rows = (int)Math.Min(rowsAvailable, Math.Min(Math.Max(1, maxRows), Math.Max(1, maxCells / columns)));

        // Every fixed axis is a block of one; the row and column axes span the window. A block comes back in axis order, so when
        // the row axis comes after the column axis (a transposed view) the row varies slowest only in the other direction.
        var start = new long[Rank];
        var count = new long[Rank];
        for (int axis = 0; axis < Rank; axis++)
        {
            start[axis] = axis == RowAxis || axis == ColumnAxis ? 0 : _index[axis];
            count[axis] = axis == RowAxis ? rows : axis == ColumnAxis ? columns : 1;
        }

        IReadOnlyList<string> cells = await _source.ReadBlockAsync(start, count, cancellationToken);
        int width = byField ? fields.Count : columns;
        int offset = withRowNumbers ? 1 : 0;
        var table = new List<string[]>(rows);
        for (int row = 0; row < rows; row++)
        {
            var line = new string[columns + offset];
            if (withRowNumbers)
            {
                line[0] = row.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            for (int column = 0; column < columns; column++)
            {
                int at = ColumnAxis >= 0 && RowAxis > ColumnAxis ? column * rows + row : row * width + column;
                line[column + offset] = at < cells.Count ? cells[at] : string.Empty;
            }

            table.Add(line);
        }

        bool rowsTruncated = rows < rowsAvailable;
        bool columnsTruncated = columns < columnsAvailable;
        return new GridTable(Headers(columns, byField, fields, withRowNumbers), table, rowsTruncated, columnsTruncated);
    }

    /// <summary>
    /// Every cell of the array as one row: an index column for each axis, then the value. A query over this table selects on the
    /// axes by name (<c>axis0</c>, <c>axis1</c>, ...), so it can pick any N-dimensional selection a two-axis window cannot show.
    /// Reads the largest rectangle that fits <paramref name="maxCells"/> cells, which is a prefix of the array in row-major order;
    /// <see cref="GridTable.RowsTruncated"/> says when that is less than the whole.
    /// </summary>
    public async Task<GridTable> LongFormAsync(long maxCells, CancellationToken cancellationToken)
    {
        if (Shape.Any(length => length == 0))
        {
            return new GridTable(LongFormHeaders, [], false, false);
        }

        long[] count = Rectangle(Shape, Math.Max(1, maxCells));
        long total = Shape.Aggregate(1L, (product, length) => product * length);
        long read = count.Aggregate(1L, (product, length) => product * length);
        var start = new long[Rank];
        IReadOnlyList<string> cells = await _source.ReadBlockAsync(start, count, cancellationToken);
        var table = new List<string[]>((int)read);
        var coordinate = new long[Rank];
        for (long at = 0; at < read; at++)
        {
            var line = new string[Rank + 1];
            for (int axis = 0; axis < Rank; axis++)
            {
                line[axis] = coordinate[axis].ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            line[Rank] = at < cells.Count ? cells[(int)at] : string.Empty;
            table.Add(line);
            for (int axis = Rank - 1; axis >= 0; axis--)
            {
                if (++coordinate[axis] < count[axis])
                {
                    break;
                }

                coordinate[axis] = 0;
            }
        }

        return new GridTable(LongFormHeaders, table, read < total, false);
    }

    /// <summary>The headers of <see cref="LongFormAsync"/>: <c>axis0</c> ... one per axis, then <c>value</c>.</summary>
    public IReadOnlyList<string> LongFormHeaders =>
        [.. Enumerable.Range(0, Rank).Select(axis => "axis" + axis.ToString(System.Globalization.CultureInfo.InvariantCulture)), "value"];

    /// <summary>The largest rectangle, cut from the first axis inward, of at most <paramref name="budget"/> cells: a prefix of the array in row-major order.</summary>
    private static long[] Rectangle(IReadOnlyList<long> shape, long budget)
    {
        var count = new long[shape.Count];
        long remaining = budget;
        for (int axis = 0; axis < shape.Count; axis++)
        {
            long inner = 1;
            for (int rest = axis + 1; rest < shape.Count; rest++)
            {
                inner *= shape[rest];
            }

            count[axis] = Math.Max(1, Math.Min(shape[axis], remaining / inner));
            remaining = Math.Max(1, remaining / Math.Max(1, count[axis]));
        }

        return count;
    }

    /// <summary>The headers of a window of <paramref name="columns"/> columns: the names the source gives, or the numbers, field names or <c>value</c>.</summary>
    private IReadOnlyList<string> Headers(int columns, bool byField, IReadOnlyList<string> fields, bool withRowNumbers)
    {
        var headers = new List<string>(columns + 1);
        if (withRowNumbers)
        {
            headers.Add("#");
        }

        if (ColumnAxis >= 0)
        {
            IReadOnlyList<string> names = _source.ColumnNames;
            bool named = names.Count == Shape[ColumnAxis];
            headers.AddRange(Enumerable.Range(0, columns).Select(column => named
                ? names[column]
                : column.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        else if (byField)
        {
            headers.AddRange(fields.Take(columns));
        }
        else
        {
            headers.Add("value");
        }

        return headers;
    }

    private void CheckAxis(int axis)
    {
        if (axis < 0 || axis >= Rank)
        {
            throw new ArgumentOutOfRangeException(nameof(axis), axis, $"An array of {Rank} axes has no axis {axis}.");
        }
    }
}

/// <summary>An array held in memory, such as a CSV document's rows or a Parquet preview: a two-axis array when given rows.</summary>
public sealed class InMemoryGridArray(IReadOnlyList<string[]> rows, IReadOnlyList<string> headers) : IGridArraySource
{
    public IReadOnlyList<long> Shape { get; } = [rows.Count, headers.Count];

    public IReadOnlyList<string> ColumnNames { get; } = headers;

    public IReadOnlyList<string> Fields { get; } = [];

    public Task<IReadOnlyList<string>> ReadBlockAsync(long[] start, long[] count, CancellationToken cancellationToken)
    {
        var cells = new List<string>();
        for (long row = start[0]; row < start[0] + count[0]; row++)
        {
            string[] line = rows[(int)row];
            for (long column = start[1]; column < start[1] + count[1]; column++)
            {
                cells.Add(column < line.Length ? line[column] : string.Empty);
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(cells);
    }
}
