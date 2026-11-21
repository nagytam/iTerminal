using JetDatabaseReader;

namespace iConsole.Services;

/// <summary>
/// Reads a Microsoft Access database (.mdb, .accdb) into lines: each user table under its name and row count, then its
/// first rows as a grid. The JET format is read by JetDatabaseReader, a managed reader that needs no Access or ACE
/// driver. Queries, forms, reports and macros are not read, nor are the rows beyond <see cref="RowsPerTable"/>.
/// Access files are read from a local path, because the reader opens the file itself, as the SQLite viewer does.
/// </summary>
public static class AccessDatabaseTextReader
{
    /// <summary>The most rows shown for one table; a larger table says how many it has.</summary>
    public const int RowsPerTable = 200;

    public static IReadOnlyList<string> Read(string path)
    {
        List<string> lines = [];
        try
        {
            using AccessReader reader = AccessReader.Open(path, new AccessReaderOptions());
            List<string> tables = reader.ListTables().ToList();
            lines.Add($"Access database: {Path.GetFileName(path)} ({tables.Count} {(tables.Count == 1 ? "table" : "tables")})");
            if (tables.Count == 0)
            {
                lines.Add(string.Empty);
                lines.Add("[The database has no tables]");
                return lines;
            }

            foreach (string table in tables)
            {
                lines.Add(string.Empty);
                AppendTable(lines, reader, table);
            }
        }
        catch (Exception exception)
        {
            lines.Add($"[Could not read the database: {exception.Message}]");
        }

        return lines;
    }

    private static void AppendTable(List<string> lines, AccessReader reader, string table)
    {
        StringTableResult result = reader.ReadTableAsStrings(table, RowsPerTable);
        OfficeTextRenderer.AppendHeading(lines, $"Table: {table}");
        lines.Add(result.RowCount > result.Rows.Count
            ? $"{result.RowCount} rows (the first {result.Rows.Count} are shown)"
            : $"{result.RowCount} {(result.RowCount == 1 ? "row" : "rows")}");

        if (result.Headers.Count == 0)
        {
            return;
        }

        List<IReadOnlyList<string>> grid = [[.. result.Headers]];
        foreach (IReadOnlyList<string> row in result.Rows)
        {
            grid.Add([.. row.Select(cell => cell ?? string.Empty)]);
        }

        OfficeTextRenderer.AppendTextTable(lines, grid);
    }
}
