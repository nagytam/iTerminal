namespace iConsole.Csv;

/// <summary>
/// Reads delimited text into a <see cref="CsvDocument"/>. The implementation is in <c>iConsole.Formats</c>, so the CSV
/// viewer here never references the parsing code.
/// </summary>
public interface ICsvDocumentParser
{
    /// <summary>
    /// Parses <paramref name="text"/>. Quoted fields may hold the delimiter and line breaks, and a doubled quote is a quote.
    /// Without a <paramref name="delimiter"/> the candidate that splits the first record into the most fields is used;
    /// a tie goes to the comma.
    /// </summary>
    CsvDocument Parse(string text, char? delimiter = null, bool hasHeader = true);

    /// <summary>The delimiter that makes the first record the widest, comma by default.</summary>
    char DetectDelimiter(string text);
}
