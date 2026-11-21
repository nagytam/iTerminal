using System.IO.Compression;
using System.Text;
using iConsole.Services;

namespace iConsole.IWork;

/// <summary>
/// Reads the text of an Apple iWork document (.pages, .numbers, .key) into lines. The file is a zip of .iwa archives,
/// read by <see cref="IwaDocument"/>. A Numbers document is shown by sheet, each sheet's text cells listed under its
/// tables (its numeric and formula cells are stored in binary tiles and are not read). A Pages or Keynote document is
/// its text storages in turn, the body first, so headers, footnotes and slide text follow it. Images, charts and
/// formatting are not shown, and an encrypted or iWork '09 document says so instead.
/// </summary>
public static class IWorkTextReader
{
    /// <summary>The object types of a Numbers sheet, a Numbers table's info, model and string table, and a text storage.</summary>
    private const uint SheetType = 2;
    private const uint TableInfoType = 6000;
    private const uint TableModelType = 6001;
    private const uint TableDataListType = 6005;
    private const uint TextStorageType = 2001;
    private const uint OtherTextStorageType = 2005;
    private const uint StringListType = 1;

    /// <summary>True when the bytes are a zip archive, which is what every iWork document is; anything else is shown as plain text.</summary>
    public static bool IsZip(byte[] content) => content.Length >= 4 && content[0] == 'P' && content[1] == 'K';

    public static IReadOnlyList<string> Read(byte[] content)
    {
        List<string> lines = [];
        try
        {
            using ZipArchive zip = new(new MemoryStream(content), ZipArchiveMode.Read);
            IwaDocument document = new();
            int archives = 0;
            foreach (ZipArchiveEntry entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                if (IsArchive(entry.FullName) && TryAdd(document, entry))
                {
                    archives++;
                }
                else if (entry.FullName.Equals("Index.zip", StringComparison.OrdinalIgnoreCase))
                {
                    archives += AddNested(document, entry);
                }
            }

            if (archives == 0)
            {
                bool iWork09 = zip.GetEntry("index.xml") is not null;
                lines.Add(iWork09
                    ? "[This is an iWork '09 document, which is not read]"
                    : "[Could not read the document: no iWork content was found, or it is encrypted]");
                return lines;
            }

            if (document.Objects.Any(o => o.Message(SheetType) is not null))
            {
                AppendNumbers(lines, document);
            }
            else
            {
                AppendStorages(lines, document);
            }

            if (lines.Count == 0)
            {
                lines.Add("[The document has no text]");
            }
        }
        catch (Exception exception)
        {
            lines.Add($"[Could not read the document: {exception.Message}]");
        }

        return OfficeTextRenderer.Trim(lines);
    }

    private static bool IsArchive(string name) =>
        name.StartsWith("Index/", StringComparison.Ordinal) && name.EndsWith(".iwa", StringComparison.OrdinalIgnoreCase);

    /// <summary>Adds one archive. A file that does not decompress (an encrypted one, say) is left out rather than failing the rest.</summary>
    private static bool TryAdd(IwaDocument document, ZipArchiveEntry entry)
    {
        try
        {
            document.AddArchive(ReadEntry(entry));
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Some iWork files keep their Index folder inside an Index.zip.</summary>
    private static int AddNested(IwaDocument document, ZipArchiveEntry entry)
    {
        using ZipArchive nested = new(new MemoryStream(ReadEntry(entry)), ZipArchiveMode.Read);
        int added = 0;
        foreach (ZipArchiveEntry inner in nested.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            if (IsArchive(inner.FullName) && TryAdd(document, inner))
            {
                added++;
            }
        }

        return added;
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using MemoryStream memory = new();
        using (Stream stream = entry.Open())
        {
            stream.CopyTo(memory);
        }

        return memory.ToArray();
    }

    /// <summary>A Numbers document: its sheets in order, each with the text of its tables and text boxes.</summary>
    private static void AppendNumbers(List<string> lines, IwaDocument document)
    {
        lines.Add("Numeric and formula cells are not read; the text cells of each table are listed under its sheet.");
        foreach (IwaObject sheet in document.Objects.Where(o => o.Message(SheetType) is not null))
        {
            List<WireField> fields = ProtobufWire.Parse(sheet.Message(SheetType)!);
            lines.Add(string.Empty);
            OfficeTextRenderer.AppendHeading(lines, $"Sheet: {ProtobufWire.String(fields, 1) ?? "(unnamed)"}");

            int tables = 0;
            foreach (ReadOnlyMemory<byte> reference in ProtobufWire.AllBytes(fields, 2))
            {
                ulong? identifier = ProtobufWire.Varint(ProtobufWire.Parse(reference), 1);
                IwaObject? drawable = identifier is { } id ? document.Find(id) : null;
                if (drawable is null)
                {
                    continue;
                }

                if (drawable.Message(TableInfoType) is not null)
                {
                    List<string> strings = TableStrings(document, drawable);
                    tables++;
                    lines.Add(string.Empty);
                    lines.Add($"Table {tables}" + (strings.Count == 0 ? " (no text cells)" : string.Empty));
                    foreach (string text in strings)
                    {
                        lines.Add("  " + text);
                    }
                }
                else if ((drawable.Message(TextStorageType) ?? drawable.Message(OtherTextStorageType)) is { } storage)
                {
                    lines.Add(string.Empty);
                    AppendText(lines, storage);
                }
            }

            if (tables == 0)
            {
                lines.Add("(no tables)");
            }
        }
    }

    /// <summary>The text cells of a table: its info, then the model, its data store and that store's string list.</summary>
    private static List<string> TableStrings(IwaDocument document, IwaObject tableInfo)
    {
        List<string> strings = [];
        IwaObject? model = document.Follow(ProtobufWire.Parse(tableInfo.Message(TableInfoType)!), 2);
        if (model?.Message(TableModelType) is not { } modelPayload)
        {
            return strings;
        }

        ReadOnlyMemory<byte>? store = ProtobufWire.Bytes(ProtobufWire.Parse(modelPayload), 4);
        if (store is not { } storeBytes)
        {
            return strings;
        }

        IwaObject? list = document.Follow(ProtobufWire.Parse(storeBytes), 4);
        if (list?.Message(TableDataListType) is not { } listPayload)
        {
            return strings;
        }

        List<WireField> listFields = ProtobufWire.Parse(listPayload);
        if (ProtobufWire.Varint(listFields, 1) == StringListType)
        {
            AddEntries(strings, listFields);
        }

        // A large list keeps its entries in segments, each a message of the same shape as the list.
        foreach (ReadOnlyMemory<byte> segment in ProtobufWire.AllBytes(listFields, 4))
        {
            ulong? identifier = ProtobufWire.Varint(ProtobufWire.Parse(segment), 1);
            IwaObject? segmentObject = identifier is { } id ? document.Find(id) : null;
            if (segmentObject?.Messages.Count > 0)
            {
                AddEntries(strings, ProtobufWire.Parse(segmentObject.Messages[0].Payload));
            }
        }

        return strings;
    }

    private static void AddEntries(List<string> strings, List<WireField> fields)
    {
        foreach (ReadOnlyMemory<byte> entry in ProtobufWire.AllBytes(fields, 3))
        {
            string? text = ProtobufWire.String(ProtobufWire.Parse(entry), 3);
            if (!string.IsNullOrEmpty(text))
            {
                strings.Add(text.Replace("\n", " ").Replace('\u2028', ' ').Replace('\u2029', ' ').Trim());
            }
        }
    }

    /// <summary>A Pages or Keynote document: its text storages, the body (kind 0) first and the rest in the order they came.</summary>
    private static void AppendStorages(List<string> lines, IwaDocument document)
    {
        List<(int Kind, byte[] Payload)> storages = [];
        foreach (IwaObject obj in document.Objects)
        {
            byte[]? payload = obj.Message(TextStorageType) ?? obj.Message(OtherTextStorageType);
            if (payload is not null)
            {
                int kind = (int)(ProtobufWire.Varint(ProtobufWire.Parse(payload), 1) ?? 0);
                storages.Add((kind, payload));
            }
        }

        foreach ((int _, byte[] payload) in storages.OrderBy(s => s.Kind))
        {
            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            AppendText(lines, payload);
        }
    }

    /// <summary>The paragraphs of one text storage: its strings are split at line and paragraph breaks, and attachment characters are dropped.</summary>
    private static void AppendText(List<string> lines, byte[] storage)
    {
        foreach (ReadOnlyMemory<byte> run in ProtobufWire.AllBytes(ProtobufWire.Parse(storage), 3))
        {
            string text = Encoding.UTF8.GetString(run.Span).Replace("￼", string.Empty).Replace('\u0007', ' ');
            foreach (string line in text.Split(['\n', '\r', '\u2028', '\u2029']))
            {
                lines.Add(line.TrimEnd());
            }
        }
    }
}
