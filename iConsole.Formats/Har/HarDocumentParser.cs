using System.Text.Json;
using iConsole.Har;

namespace iConsole.Formats.Har;

/// <summary>
/// Reads a HAR file into a <see cref="HarDocument"/>. Lives in <c>iConsole.Formats</c>, behind <see cref="IHarDocumentParser"/>,
/// so the HAR viewer stays free of the JSON reading.
/// </summary>
public sealed class HarDocumentParser : IHarDocumentParser
{
    /// <summary>
    /// Reads a HAR document. Throws <see cref="InvalidDataException"/> when the JSON has no <c>log.entries</c> list, and
    /// <see cref="JsonException"/> when the text is not JSON at all. Blocking CPU work: call it from a worker thread.
    /// </summary>
    public HarDocument Parse(string json)
    {
        // The document is not disposed: each entry keeps a JsonElement, which keeps its document alive.
        JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement log = HarJson.Property(root, "log");
        JsonElement entries = HarJson.Property(log, "entries");
        if (entries.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("no log.entries list");
        }

        var rows = new List<HarEntry>();
        bool truncated = false;
        foreach (JsonElement source in entries.EnumerateArray())
        {
            if (source.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (rows.Count == HarDocument.MaxEntries)
            {
                truncated = true;
                break;
            }

            rows.Add(ReadEntry(source, rows.Count + 1));
        }

        return new HarDocument(rows, truncated);
    }

    private static HarEntry ReadEntry(JsonElement source, int number)
    {
        JsonElement request = HarJson.Property(source, "request");
        JsonElement response = HarJson.Property(source, "response");
        JsonElement content = HarJson.Property(response, "content");

        // HAR uses -1 for "unknown"; the body's own size is the better number, and the transfer size the fallback.
        long? size = HarJson.Integer(content, "size") ?? HarJson.Integer(response, "bodySize");
        if (size is < 0)
        {
            size = null;
        }

        return new HarEntry(
            number,
            HarJson.Text(request, "method"),
            HarJson.Text(request, "url"),
            (int)(HarJson.Integer(response, "status") ?? 0),
            HarJson.Text(response, "statusText"),
            HarJson.Text(content, "mimeType"),
            size,
            HarJson.Number(source, "time") ?? 0,
            HarJson.Text(source, "startedDateTime"),
            source);
    }
}
