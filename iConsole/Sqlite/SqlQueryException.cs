namespace iConsole.Sqlite;

/// <summary>A query that was refused, or that SQLite could not run. The message is shown to the person as it is.</summary>
public sealed class SqlQueryException : Exception
{
    public SqlQueryException(string message) : base(message)
    {
    }
}
