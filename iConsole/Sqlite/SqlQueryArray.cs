using iConsole.Tabular;

namespace iConsole.Sqlite;

/// <summary>
/// The result of one <c>SELECT</c> read as a two-axis array. The rows are not held: each block the view asks for is a query over the
/// statement with <c>LIMIT</c> and <c>OFFSET</c>, so the viewer reads only the window it shows. Call <see cref="InitializeAsync"/> once
/// first: it reads the column names and the row count.
/// </summary>
/// <remarks>
/// The statement is wrapped as a subquery, so it must be a <c>SELECT</c> or a <c>WITH</c>; the reader's write guards still check the
/// wrapped statement. Each block re-runs the statement, so a slow query is paid for once per block read.
/// </remarks>
public sealed class SqlQueryArray(ISqliteDatabaseReader reader, string path, string select) : IGridArraySource
{
    private IReadOnlyList<long> _shape = [0, 0];
    private IReadOnlyList<string> _columnNames = [];

    /// <summary>The number of rows and of columns the statement returns, once initialised.</summary>
    public IReadOnlyList<long> Shape => _shape;

    /// <summary>The column names the statement returns, once initialised.</summary>
    public IReadOnlyList<string> ColumnNames => _columnNames;

    public IReadOnlyList<string> Fields { get; } = [];

    /// <summary>The statement without a trailing semicolon, so it can sit inside a subquery.</summary>
    private string Inner => select.Trim().TrimEnd(';').TrimEnd();

    /// <summary>Reads the column names and the row count: two queries that return no rows.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        SqlTable columns = await reader.QueryAsync(path, $"SELECT * FROM ({Inner}) LIMIT 0", 0, cancellationToken);
        SqlTable count = await reader.QueryAsync(path, $"SELECT COUNT(*) FROM ({Inner})", 1, cancellationToken);
        _columnNames = columns.Headers;
        long rows = count.Rows.Count > 0 && long.TryParse(count.Rows[0][0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long total) ? total : 0;
        _shape = [rows, columns.Headers.Count];
    }

    /// <summary>
    /// Runs a query over the statement's result as one SQLite statement: the query is compiled over <c>(statement) AS result</c>, so the filter,
    /// order and limit run inside the database in a single run, not one run per block. Call <see cref="InitializeAsync"/> first.
    /// </summary>
    public async Task<GridQueryResult> QueryAsync(string text, int maxRows, CancellationToken cancellationToken)
    {
        GridQueryPlan plan = GridQuery.Parse(text, _columnNames);
        string sql = GridQuery.ToSqlFrom(plan, $"({Inner}) AS result");
        SqlTable table = await reader.QueryAsync(path, sql, maxRows, cancellationToken);
        return new GridQueryResult(table.Headers, table.Rows, table.Rows.Count, table.Truncated);
    }

    /// <summary>The cells of a block: the rows from <c>start[0]</c>, and the columns from <c>start[1]</c>, read with <c>LIMIT</c> and <c>OFFSET</c>.</summary>
    public async Task<IReadOnlyList<string>> ReadBlockAsync(long[] start, long[] count, CancellationToken cancellationToken)
    {
        if (count[0] == 0 || count[1] == 0)
        {
            return [];
        }

        SqlTable window = await reader.QueryAsync(
            path,
            $"SELECT * FROM ({Inner}) LIMIT {count[0].ToString(System.Globalization.CultureInfo.InvariantCulture)} OFFSET {start[0].ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            (int)count[0],
            cancellationToken);
        var cells = new List<string>();
        foreach (string[] row in window.Rows)
        {
            for (long column = start[1]; column < start[1] + count[1]; column++)
            {
                cells.Add(column < row.Length ? row[column] : string.Empty);
            }
        }

        return cells;
    }
}
