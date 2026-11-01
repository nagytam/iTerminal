namespace iConsole.Sqlite;

/// <summary>
/// Runs one read-only query over a local SQLite database file. The implementation holds the write guard
/// (see iTerminal.Core's SqliteDatabaseReader); callers must not rely on the UI layer to refuse writes.
/// </summary>
public interface ISqliteDatabaseReader
{
    /// <summary>Runs <paramref name="sql"/> over <paramref name="path"/>. Throws <see cref="SqlQueryException"/> for a refused or failed query.</summary>
    Task<SqlTable> QueryAsync(string path, string sql, int maxRows, CancellationToken cancellationToken);
}
