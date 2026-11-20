using System.Text;

namespace iConsole.Csv;

/// <summary>
/// Writes rows as CSV (RFC 4180): a header record, then one record per row, comma-separated, each record ending in CRLF. A value
/// with a comma, a double quote or a line break is quoted, and its double quotes are doubled. A row shorter than the header
/// is padded with empty values. No UI.
/// </summary>
public static class CsvExport
{
    private static readonly char[] NeedsQuotes = [',', '"', '\r', '\n'];

    public static string Build(IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        var text = new StringBuilder();
        AppendRecord(text, headers);
        foreach (string[] row in rows)
        {
            var fields = new string[headers.Count];
            for (int column = 0; column < fields.Length; column++)
            {
                fields[column] = column < row.Length ? row[column] : string.Empty;
            }

            AppendRecord(text, fields);
        }

        return text.ToString();
    }

    private static void AppendRecord(StringBuilder text, IEnumerable<string> fields)
    {
        bool first = true;
        foreach (string field in fields)
        {
            if (!first)
            {
                text.Append(',');
            }

            first = false;
            text.Append(Quote(field));
        }

        text.Append("\r\n");
    }

    private static string Quote(string value) =>
        value.IndexOfAny(NeedsQuotes) < 0 ? value : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
