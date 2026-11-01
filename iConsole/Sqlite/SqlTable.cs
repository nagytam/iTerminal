namespace iConsole.Sqlite;

/// <summary>The rows a query returned: the column names, the cell text, and whether the row cap cut the result short.</summary>
public sealed record SqlTable(IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows, bool Truncated);
