using System.Globalization;
using iConsole.Sqlite;
using Microsoft.Data.Sqlite;

namespace iTerminal.Core.Services.Sqlite;

/// <summary>
/// Runs one read-only query over a local SQLite file for the SQLite viewer. Three guards stand together: the statement
/// checker (<see cref="SqliteStatementChecker"/>), a connection opened <c>Mode=ReadOnly</c>, and <c>PRAGMA query_only = ON</c>.
/// Extension loading stays off (the library default). Each query gets its own connection, which is closed when it ends.
/// </summary>
public sealed class SqliteDatabaseReader : ISqliteDatabaseReader
{
    /// <summary>Seconds one query may run. A legitimate large query that needs longer gets an error on the status line.</summary>
    public const int CommandTimeoutSeconds = 10;

    private const int HeaderLength = 16;

    public async Task<SqlTable> QueryAsync(string path, string sql, int maxRows, CancellationToken cancellationToken)
    {
        SqliteStatementChecker.Validate(sql);
        if (!File.Exists(path))
        {
            throw new SqlQueryException("The database file was not found");
        }

        await CheckHeaderAsync(path, cancellationToken);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            // No pooling: the file handle is released as soon as the query ends, not when the pool recycles it.
            Pooling = false,
        };
        await using var connection = new SqliteConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            using (SqliteCommand guard = connection.CreateCommand())
            {
                guard.CommandText = "PRAGMA query_only = ON";
                await guard.ExecuteNonQueryAsync(cancellationToken);
            }

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = CommandTimeoutSeconds;
            // Cancelling the token interrupts the running statement. SqliteCommand.Cancel does not stop a step that is
            // already running, so the native sqlite3_interrupt is called on the connection's handle instead; SQLite allows
            // that call from another thread. Without it a runaway query keeps its worker until it finishes.
            using CancellationTokenRegistration interrupt = cancellationToken.Register(() => SQLitePCL.raw.sqlite3_interrupt(connection.Handle));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            return await ReadRowsAsync(reader, maxRows, cancellationToken);
        }
        catch (SqliteException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("The query was cancelled", ex, cancellationToken);
        }
        catch (SqliteException ex)
        {
            throw new SqlQueryException(ex.Message);
        }
    }

    /// <summary>A file without the SQLite header is not a database, and an empty file is not one either. Checked before SQLite opens it.</summary>
    private static async Task CheckHeaderAsync(string path, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[HeaderLength];
        int read;
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            read = await stream.ReadAtLeastAsync(buffer, HeaderLength, throwOnEndOfStream: false, cancellationToken);
        }

        if (!SqliteFileTypes.HasSqliteHeader(buffer.AsSpan(0, read)))
        {
            throw new SqlQueryException("Not an SQLite database");
        }
    }

    private static async Task<SqlTable> ReadRowsAsync(SqliteDataReader reader, int maxRows, CancellationToken cancellationToken)
    {
        int fields = reader.FieldCount;
        string[] headers = new string[fields];
        for (int i = 0; i < fields; i++)
        {
            headers[i] = reader.GetName(i);
        }

        var rows = new List<string[]>();
        bool truncated = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count >= maxRows)
            {
                // One row past the cap means the result was cut short.
                truncated = true;
                break;
            }

            string[] cells = new string[fields];
            for (int i = 0; i < fields; i++)
            {
                cells[i] = Cell(reader, i);
            }

            rows.Add(cells);
        }

        return new SqlTable(headers, rows, truncated);
    }

    private static string Cell(SqliteDataReader reader, int index)
    {
        if (reader.IsDBNull(index))
        {
            return "NULL";
        }

        object value = reader.GetValue(index);
        return value switch
        {
            byte[] bytes => $"<blob {bytes.Length} bytes>",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }
}
