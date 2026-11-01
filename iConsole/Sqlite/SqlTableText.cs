using System.Text;
using iConsole.Localization;

namespace iConsole.Sqlite;

/// <summary>The query result as aligned plain text, the F4 view of the SQLite viewer.</summary>
public static class SqlTableText
{
    internal const int MaxColumnWidth = 40;
    internal const string Separator = " | ";

    public static IReadOnlyList<string> Format(SqlTable table)
    {
        int columns = table.Headers.Count;
        if (columns == 0)
        {
            return [];
        }

        int[] widths = new int[columns];
        for (int c = 0; c < columns; c++)
        {
            widths[c] = Math.Clamp(table.Headers[c].Length, 1, MaxColumnWidth);
        }

        foreach (string[] row in table.Rows)
        {
            for (int c = 0; c < columns && c < row.Length; c++)
            {
                widths[c] = Math.Max(widths[c], Math.Min(MaxColumnWidth, row[c].Length));
            }
        }

        List<string> lines = [FormatRow(table.Headers, widths), Rule(widths)];
        foreach (string[] row in table.Rows)
        {
            lines.Add(FormatRow(row, widths));
        }

        lines.Add(string.Empty);
        string count = string.Format(UiText.Get("Sqlite.Text.Rows", "{0:N0} row(s)"), table.Rows.Count);
        lines.Add(count + (table.Truncated ? UiText.Get("Sqlite.Text.Truncated", ", truncated") : string.Empty));
        return lines;
    }

    private static string FormatRow(IReadOnlyList<string> cells, int[] widths)
    {
        var builder = new StringBuilder();
        for (int c = 0; c < widths.Length; c++)
        {
            if (c > 0)
            {
                builder.Append(Separator);
            }

            string cell = c < cells.Count ? cells[c] : string.Empty;
            builder.Append(Fit(cell, widths[c]));
        }

        return builder.ToString().TrimEnd();
    }

    private static string Fit(string text, int width)
    {
        string single = text.Replace('\r', ' ').Replace('\n', ' ');
        if (single.Length > width)
        {
            return single[..(width - 1)] + "…";
        }

        return single.PadRight(width);
    }

    private static string Rule(int[] widths) =>
        string.Join("-+-", widths.Select(w => new string('-', w)));
}
