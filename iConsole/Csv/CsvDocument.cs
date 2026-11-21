using System.Buffers;
using System.Text;

namespace iConsole.Csv;

/// <summary>
/// A delimited text file read into a header and rows. Parsing is lenient: a short row reads as empty in the missing
/// columns, a long row keeps its extra fields, and nothing a file contains makes the parse fail.
/// </summary>
/// <param name="Headers">The column names, from the first record (or generated when the file has no header).</param>
/// <param name="Rows">The records after the header, as they were read.</param>
/// <param name="Delimiter">The character that separated the fields.</param>
/// <param name="Truncated">True when the file had more than <see cref="MaxRows"/> records and only the first were read.</param>
public sealed record CsvDocument(IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows, char Delimiter, bool Truncated)
{
    /// <summary>The most records read; a larger file keeps its first records and says so.</summary>
    public const int MaxRows = 500_000;
}
