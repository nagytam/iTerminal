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
            int named = (int)Math.Min(columnsAvailable, Math.Max(0, maxColumns));
            return new GridTable(Headers(named, byField, fields, withRowNumbers), [], false, columnsAvailable > named);
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

    /// <summary>The number of cells one block of a streamed query reads: the largest block the query holds at a time.</summary>
    public const long DefaultChunkCells = 100_000;

    /// <summary>
    /// The one query path for every array. A query that names <c>FROM cells</c>, or any array of three or more axes (or none), runs over
    /// the long form: one row per cell with an index column per axis. Any other query runs over the rows of a one- or two-axis array:
    /// one row per index of the first axis, with the array's column names. Either way the array is streamed in blocks of at most
    /// <paramref name="chunkCells"/> cells, each block is filtered as it arrives, and at most <paramref name="maxMatches"/> matches are kept.
    /// Order and limit run over the matches.
    /// </summary>
    public Task<GridQueryResult> QueryAsync(string text, long chunkCells, int maxMatches, CancellationToken cancellationToken)
    {
        // A query with an axis clause names the window it reads: its axes are chosen and fixed first, then the query runs over that window.
        if (AxisClauseOf(text) is { } clause)
        {
            return QueryWindowAsync(text, clause, cancellationToken);
        }

        return AsksForCells(text)
            ? QueryLongFormAsync(text, chunkCells, maxMatches, cancellationToken)
            : QueryRowsAsync(text, chunkCells, maxMatches, cancellationToken);
    }

    /// <summary>The axis clause of a query (<c>AT</c>, <c>ROWS</c>, <c>COLUMNS</c>), or null when it has none or does not parse.</summary>
    public GridAxisClause? AxisClauseOf(string text) => GridQuery.AxisClauseOf(text);

    /// <summary>
    /// Runs a query over a window of the array: the rows and columns are the axes the clause names (or the array's own), each fixed axis
    /// is fixed at its index, and the window is read whole, up to the window cap. The query's filter, order and limit then run over that
    /// window's table, with the window's own column labels.
    /// </summary>
    private async Task<GridQueryResult> QueryWindowAsync(string text, GridAxisClause clause, CancellationToken cancellationToken)
    {
        if (Rank < 2)
        {
            throw new GridQueryException("An axis clause needs an array of two or more axes.");
        }

        int rowAxis = clause.Rows is null ? RowAxis : AxisNumber(clause.Rows);
        int columnAxis = clause.Columns is null ? ColumnAxis : AxisNumber(clause.Columns);
        if (rowAxis == columnAxis)
        {
            throw new GridQueryException("The rows and the columns must be different axes.");
        }

        GridArrayView window = Project(rowAxis, columnAxis);
        foreach ((string axisName, long index) in clause.Fixed)
        {
            int axis = AxisNumber(axisName);
            if (axis == rowAxis || axis == columnAxis)
            {
                throw new GridQueryException($"{axisName} is shown as rows or columns, so it cannot also be fixed.");
            }

            window = window.WithIndex(axis, index);
        }

        GridTable table = await window.MaterializeAsync(int.MaxValue, int.MaxValue, (int)Math.Min(int.MaxValue, DefaultChunkCells), cancellationToken, withRowNumbers: false);
        GridQueryResult shaped = GridQuery.Execute(table.Headers, table.Rows, GridQuery.Parse(text, table.Headers));
        return shaped with { Truncated = table.RowsTruncated || table.ColumnsTruncated };
    }

    /// <summary>The number of an axis named <c>axisN</c>, checked against the array's axes.</summary>
    private int AxisNumber(string name)
    {
        int axis = int.Parse(name.AsSpan(4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
        if (axis >= Rank)
        {
            throw new GridQueryException($"An array of {Rank} axes has no {name}.");
        }

        return axis;
    }

    private bool AsksForCells(string text)
    {
        if (Rank != 1 && Rank != 2)
        {
            return true;
        }

        try
        {
            return string.Equals(GridQuery.Parse(text, LongFormHeaders).From, "cells", StringComparison.OrdinalIgnoreCase);
        }
        catch (GridQueryException)
        {
            return false;
        }
    }

    /// <summary>The rows of a one- or two-axis array: one row per index of the first axis, one column per field or per column of the second.</summary>
    private async Task<GridQueryResult> QueryRowsAsync(string text, long chunkCells, int maxMatches, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> fields = _source.Fields;
        int width = Rank == 1 ? (fields.Count > 0 ? fields.Count : 1) : (int)Shape[1];
        IReadOnlyList<string> headers = Rank == 1
            ? (fields.Count > 0 ? fields : ["value"])
            : (_source.ColumnNames.Count == width ? _source.ColumnNames : [.. Enumerable.Range(0, width).Select(column => column.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
        GridQueryPlan plan = GridQuery.Parse(text, headers);
        GridQueryPlan filter = new(plan.From, null, plan.Where, null, false, null);
        var matched = new List<string[]>();
        bool truncated = false;
        long rows = Shape[0];
        long perBlock = Math.Max(1, chunkCells / Math.Max(1, width));
        for (long first = 0; first < rows && !truncated; first += perBlock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long count = Math.Min(perBlock, rows - first);
            long[] start = Rank == 1 ? [first] : [first, 0];
            long[] blockCount = Rank == 1 ? [count] : [count, width];
            IReadOnlyList<string> cells = await _source.ReadBlockAsync(start, blockCount, cancellationToken);
            var lines = new List<string[]>((int)count);
            for (long row = 0; row < count; row++)
            {
                var line = new string[width];
                for (int column = 0; column < width; column++)
                {
                    long at = row * width + column;
                    line[column] = at < cells.Count ? cells[(int)at] : string.Empty;
                }

                lines.Add(line);
            }

            foreach (string[] line in GridQuery.Execute(headers, lines, filter).Rows)
            {
                if (matched.Count >= maxMatches)
                {
                    truncated = true;
                    break;
                }

                matched.Add(line);
            }
        }

        GridQueryResult shaped = GridQuery.Execute(headers, matched, new GridQueryPlan(null, plan.Columns, null, plan.OrderBy, plan.Descending, plan.Limit));
        return shaped with { Matched = matched.Count, Truncated = truncated };
    }

    /// <summary>
    /// Runs a query over every cell of the array, as the long form: one row per cell, with an index column for each axis and a
    /// <c>value</c> column. The array is read in blocks of at most <paramref name="chunkCells"/> cells; each block is filtered as it
    /// arrives, and only the matching rows are kept, so the whole array is searched without holding it. Order and limit then run over
    /// the matches, so they are correct across blocks. At most <paramref name="maxMatches"/> matches are kept; the result says when
    /// that cut them short, and its <see cref="GridQueryResult.Matched"/> counts the matches kept.
    /// </summary>
    public async Task<GridQueryResult> QueryLongFormAsync(string text, long chunkCells, int maxMatches, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> headers = LongFormHeaders;
        GridQueryPlan plan = GridQuery.Parse(text, headers);
        GridQueryPlan filter = new(plan.From, null, plan.Where, null, false, null);
        var matched = new List<string[]>();
        bool truncated = false;
        if (Shape.Any(length => length == 0))
        {
            return new GridQueryResult(headers, [], 0);
        }

        // The trailing axes that fit in one block are read whole; the leading ones are walked one index at a time.
        int split = Rank;
        long inner = 1;
        while (split > 0 && inner * Shape[split - 1] <= Math.Max(1, chunkCells))
        {
            split--;
            inner *= Shape[split];
        }

        var index = new long[split];
        bool more = true;
        while (more && !truncated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = new long[Rank];
            var count = new long[Rank];
            for (int axis = 0; axis < Rank; axis++)
            {
                start[axis] = axis < split ? index[axis] : 0;
                count[axis] = axis < split ? 1 : Shape[axis];
            }

            IReadOnlyList<string> cells = await _source.ReadBlockAsync(start, count, cancellationToken);
            foreach (string[] row in GridQuery.Execute(headers, LongRows(start, count, cells), filter).Rows)
            {
                if (matched.Count >= maxMatches)
                {
                    truncated = true;
                    break;
                }

                matched.Add(row);
            }

            more = Advance(index, split);
        }

        GridQueryResult shaped = GridQuery.Execute(headers, matched, new GridQueryPlan(null, plan.Columns, null, plan.OrderBy, plan.Descending, plan.Limit));
        return shaped with { Matched = matched.Count, Truncated = truncated };
    }

    /// <summary>The cells of a block as long-form rows: the coordinate of each cell on every axis, then the cell.</summary>
    private List<string[]> LongRows(long[] start, long[] count, IReadOnlyList<string> cells)
    {
        var rows = new List<string[]>();
        var local = new long[Rank];
        long total = count.Aggregate(1L, (product, length) => product * length);
        for (long at = 0; at < total; at++)
        {
            var line = new string[Rank + 1];
            for (int axis = 0; axis < Rank; axis++)
            {
                line[axis] = (start[axis] + local[axis]).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            line[Rank] = at < cells.Count ? cells[(int)at] : string.Empty;
            rows.Add(line);
            Advance(local, count, Rank);
        }

        return rows;
    }

    /// <summary>Steps an index to the next position over the first <paramref name="axes"/> axes, last axis fastest; false when it has wrapped round.</summary>
    private bool Advance(long[] index, int axes) => Advance(index, Shape, axes);

    private static bool Advance(long[] index, IReadOnlyList<long> bounds, int axes)
    {
        for (int axis = axes - 1; axis >= 0; axis--)
        {
            if (++index[axis] < bounds[axis])
            {
                return true;
            }

            index[axis] = 0;
        }

        return false;
    }

    /// <summary>The headers of the long form: <c>axis0</c> ... one per axis, then <c>value</c>.</summary>
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
