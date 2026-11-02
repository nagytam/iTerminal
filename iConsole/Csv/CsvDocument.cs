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

    private static readonly char[] Candidates = [',', ';', '\t', '|'];

    /// <summary>
    /// Parses <paramref name="text"/>. Quoted fields may hold the delimiter and line breaks, and a doubled quote is a quote.
    /// Without a <paramref name="delimiter"/> the candidate that splits the first record into the most fields is used;
    /// a tie goes to the comma.
    /// </summary>
    public static CsvDocument Parse(string text, char? delimiter = null, bool hasHeader = true)
    {
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        char separator = delimiter ?? DetectDelimiter(text);
        List<string[]> records = Records(text, separator, MaxRows + 1);
        bool truncated = records.Count > MaxRows;
        if (truncated)
        {
            records.RemoveRange(MaxRows, records.Count - MaxRows);
        }

        if (records.Count == 0)
        {
            return new CsvDocument([], [], separator, truncated);
        }

        string[] headers;
        if (hasHeader)
        {
            headers = NameColumns(records[0]);
            records.RemoveAt(0);
        }
        else
        {
            int width = records.Max(record => record.Length);
            headers = [.. Enumerable.Range(1, width).Select(i => $"Column{i}")];
        }

        return new CsvDocument(headers, records, separator, truncated);
    }

    /// <summary>The delimiter that makes the first record the widest, comma by default.</summary>
    public static char DetectDelimiter(string text)
    {
        char best = ',';
        int bestCount = 1;
        foreach (char candidate in Candidates)
        {
            int count = Records(text, candidate, 1).FirstOrDefault()?.Length ?? 0;
            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        return best;
    }

    /// <summary>
    /// The records of the text, with blank lines skipped. A quote opens a quoted field only at the start of a field; inside one,
    /// a doubled quote is a literal quote and a single quote closes it. Reading stops after <paramref name="limit"/> records.
    /// </summary>
    private static List<string[]> Records(string text, char delimiter, int limit)
    {
        List<string[]> records = [];
        List<string> fields = [];
        StringBuilder field = new();
        bool quoted = false;
        bool fieldStarted = false;
        int i = 0;
        int n = text.Length;
        SearchValues<char> special = SearchValues.Create([delimiter, '\r', '\n', '"']);

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
        }

        void CloseRecord()
        {
            if (fields.Count > 1 || fields[0].Length > 0)
            {
                records.Add([.. fields]);
            }

            fields.Clear();
        }

        void EndRecord()
        {
            EndField();
            CloseRecord();
        }

        // Runs of ordinary characters are copied in one span append, found with a vectorized search for the few
        // characters that change the state; the rest of each loop pass handles those characters one at a time.
        while (i < n && records.Count < limit)
        {
            if (quoted)
            {
                int quote = text.AsSpan(i).IndexOf('"');
                if (quote < 0)
                {
                    field.Append(text, i, n - i);
                    break;
                }

                field.Append(text, i, quote);
                i += quote;
                if (i + 1 < n && text[i + 1] == '"')
                {
                    field.Append('"');
                    i += 2;
                }
                else
                {
                    quoted = false;
                    i++;
                }

                continue;
            }

            int next = text.AsSpan(i).IndexOfAny(special);
            if (next < 0)
            {
                field.Append(text, i, n - i);
                fieldStarted = true;
                break;
            }

            // The common case: a field that is one unbroken run of text, ended by a delimiter or a line break. It is
            // copied straight out of the text, with no builder in between.
            char end = text[i + next];
            if (!fieldStarted && end != '"' && (end == delimiter || end == '\r' || end == '\n'))
            {
                fields.Add(text.Substring(i, next));
                i += next + 1;
                if (end != delimiter)
                {
                    if (end == '\r' && i < n && text[i] == '\n')
                    {
                        i++;
                    }

                    CloseRecord();
                }

                continue;
            }

            if (next > 0)
            {
                field.Append(text, i, next);
                fieldStarted = true;
                i += next;
            }

            char c = text[i];
            i++;
            if (c == '"' && !fieldStarted)
            {
                // A quoted field with no doubled quote, closed by a delimiter or line break, is also copied straight out.
                int close = text.AsSpan(i).IndexOf('"');
                if (close >= 0 && i + close + 1 < n && text[i + close + 1] != '"'
                    && (text[i + close + 1] == delimiter || text[i + close + 1] is '\r' or '\n'))
                {
                    char term = text[i + close + 1];
                    fields.Add(text.Substring(i, close));
                    i += close + 2;
                    if (term != delimiter)
                    {
                        if (term == '\r' && i < n && text[i] == '\n')
                        {
                            i++;
                        }

                        CloseRecord();
                    }

                    continue;
                }

                quoted = true;
                fieldStarted = true;
            }
            else if (c == delimiter)
            {
                EndField();
            }
            else if (c == '"')
            {
                // A quote inside an unquoted field is an ordinary character.
                field.Append('"');
                fieldStarted = true;
            }
            else
            {
                if (c == '\r' && i < n && text[i] == '\n')
                {
                    i++;
                }

                EndRecord();
            }
        }

        if (records.Count < limit && (field.Length > 0 || fields.Count > 0))
        {
            EndRecord();
        }

        return records;
    }

    /// <summary>Column names as read: an empty name becomes ColumnN and a repeated name gets _2, _3 and so on, so every column can be named in a query.</summary>
    private static string[] NameColumns(string[] header)
    {
        var names = new string[header.Length];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Length; i++)
        {
            string name = header[i].Trim();
            if (name.Length == 0)
            {
                name = $"Column{i + 1}";
            }

            if (!used.Add(name))
            {
                // A generated name must not be one a later column already has: "a", "a", "a_2" is a, a_2 and a_2_2, not a_2 twice.
                string candidate;
                int number = 2;
                do
                {
                    candidate = $"{name}_{number++}";
                }
                while (!used.Add(candidate));
                name = candidate;
            }

            names[i] = name;
        }

        return names;
    }
}
