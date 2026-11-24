using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;

namespace iConsole.Csv;

/// <summary>The formats the table viewers export rows in. CSV comes first, so Enter in the format prompt picks it.</summary>
public enum TableExportFormat
{
    Csv,
    Json,
    Xml,
}

/// <summary>
/// A format as a choice in the format prompt. It is a reference type on purpose: a cancelled selection comes back as null, and an enum
/// would come back as its default value (CSV) instead.
/// </summary>
internal sealed record TableExportChoice(TableExportFormat Format);

/// <summary>
/// Writes rows as CSV, JSON or XML. CSV is <see cref="CsvExport"/>, unchanged. JSON is an array of objects keyed by the column names.
/// XML is a table of rows, each cell a <c>c</c> element with its column name in a <c>name</c> attribute. Every value is text, so the three
/// formats hold the same data. A row shorter than the header is padded with empty values. No UI.
/// </summary>
public static class TableExport
{
    public static string Build(TableExportFormat format, IReadOnlyList<string> headers, IEnumerable<string[]> rows) => format switch
    {
        TableExportFormat.Json => BuildJson(headers, rows),
        TableExportFormat.Xml => BuildXml(headers, rows),
        _ => CsvExport.Build(headers, rows),
    };

    /// <summary>The file extension of a format, with its dot.</summary>
    public static string Extension(TableExportFormat format) => format switch
    {
        TableExportFormat.Json => ".json",
        TableExportFormat.Xml => ".xml",
        _ => ".csv",
    };

    /// <summary>
    /// The name with the format's extension: a name that already ends in it (ignoring case) is kept, and any other name gets it appended.
    /// </summary>
    public static string WithExtension(string name, TableExportFormat format)
    {
        string extension = Extension(format);
        return name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? name : name + extension;
    }

    /// <summary>
    /// Column names as JSON keys and XML attributes. An empty name becomes <c>column_N</c>, N the 1-based position. A name already used
    /// by an earlier column gets <c>_2</c>, <c>_3</c> and so on until it is unused. Comparison is ordinal and case-sensitive.
    /// </summary>
    internal static IReadOnlyList<string> UniqueNames(IReadOnlyList<string> headers)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<string>(headers.Count);
        for (int column = 0; column < headers.Count; column++)
        {
            string name = string.IsNullOrEmpty(headers[column]) ? "column_" + (column + 1) : headers[column];
            string candidate = name;
            int suffix = 2;
            while (used.Contains(candidate))
            {
                candidate = name + "_" + suffix;
                suffix++;
            }

            used.Add(candidate);
            names.Add(candidate);
        }

        return names;
    }

    private static string BuildJson(IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        IReadOnlyList<string> names = UniqueNames(headers.Select(JsonText).ToList());
        using var stream = new MemoryStream();
        var options = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using (var writer = new Utf8JsonWriter(stream, options))
        {
            writer.WriteStartArray();
            foreach (string[] row in rows)
            {
                writer.WriteStartObject();
                for (int column = 0; column < names.Count; column++)
                {
                    writer.WriteString(names[column], JsonText(Value(row, column)));
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildXml(IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        IReadOnlyList<string> names = UniqueNames(headers.Select(XmlCharacters).ToList());
        // Entitize keeps a carriage return in a value (the default would turn it into a line feed), so XML holds the same text as JSON and CSV.
        var settings = new XmlWriterSettings
        {
            Indent = true,
            NewLineHandling = NewLineHandling.Entitize,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        var text = new StringWriter();
        using (XmlWriter writer = XmlWriter.Create(text, settings))
        {
            writer.WriteStartElement("table");
            foreach (string[] row in rows)
            {
                writer.WriteStartElement("row");
                for (int column = 0; column < names.Count; column++)
                {
                    writer.WriteStartElement("c");
                    writer.WriteAttributeString("name", names[column]);
                    writer.WriteString(XmlCharacters(Value(row, column)));
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        return text.ToString();
    }

    /// <summary>JSON cannot carry an unpaired surrogate (the writer throws on one), so each one is written as U+FFFD.</summary>
    private static string JsonText(string value)
    {
        var text = new StringBuilder(value.Length);
        int index = 0;
        while (index < value.Length)
        {
            char current = value[index];
            if (char.IsHighSurrogate(current) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
            {
                text.Append(current).Append(value[++index]);
            }
            else if (char.IsSurrogate(current))
            {
                text.Append('�');
            }
            else
            {
                text.Append(current);
            }
            index++;
        }

        return text.ToString();
    }

    /// <summary>
    /// Drops the characters XML 1.0 cannot carry: control characters other than tab, line feed and carriage return, U+FFFE and U+FFFF,
    /// and an unpaired surrogate. The removal is silent; XML has no way to write them. Column names go through it too, before they are
    /// checked for emptiness, so a name made only of such characters becomes column_N.
    /// </summary>
    private static string XmlCharacters(string value)
    {
        var text = new StringBuilder(value.Length);
        int index = 0;
        while (index < value.Length)
        {
            char current = value[index];
            if (char.IsHighSurrogate(current) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
            {
                text.Append(current).Append(value[++index]);
            }
            else if (current is '\t' or '\n' or '\r' || (current >= ' ' && current <= '퟿') || (current >= '' && current <= '�'))
            {
                text.Append(current);
            }
            index++;
        }

        return text.ToString();
    }

    private static string Value(string[] row, int column) => column < row.Length ? row[column] : string.Empty;
}
