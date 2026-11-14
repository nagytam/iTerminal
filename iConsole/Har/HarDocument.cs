using System.Text.Json;

namespace iConsole.Har;

/// <summary>
/// One request and its response, as the grid lists it. <see cref="Source"/> is the entry's own JSON: the details read the
/// headers and bodies from it when a row is opened, so a large file does not copy every body up front.
/// </summary>
public sealed record HarEntry(
    int Number,
    string Method,
    string Url,
    int Status,
    string StatusText,
    string MimeType,
    long? Size,
    double TimeMs,
    string StartedDateTime,
    JsonElement Source);

/// <summary>
/// A HAR file (HTTP Archive 1.2, the network log a browser's developer tools export): the entries of its <c>log</c>, in order.
/// It is read in one pass; a file past <see cref="MaxEntries"/> keeps its first entries and says so.
/// </summary>
public sealed class HarDocument
{
    /// <summary>The most entries the viewer lists. A longer log is cut here, and <see cref="Truncated"/> says so.</summary>
    public const int MaxEntries = 100_000;

    private HarDocument(IReadOnlyList<HarEntry> entries, bool truncated)
    {
        Entries = entries;
        Truncated = truncated;
    }

    public IReadOnlyList<HarEntry> Entries { get; }

    /// <summary>True when the file had more entries than <see cref="MaxEntries"/>.</summary>
    public bool Truncated { get; }

    /// <summary>
    /// Reads a HAR document. Throws <see cref="InvalidDataException"/> when the JSON has no <c>log.entries</c> list, and
    /// <see cref="JsonException"/> when the text is not JSON at all. Blocking CPU work: call it from a worker thread.
    /// </summary>
    public static HarDocument Parse(string json)
    {
        // The document is not disposed: each entry keeps a JsonElement, which keeps its document alive.
        JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement log = Property(root, "log");
        JsonElement entries = Property(log, "entries");
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

            if (rows.Count == MaxEntries)
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
        JsonElement request = Property(source, "request");
        JsonElement response = Property(source, "response");
        JsonElement content = Property(response, "content");

        // HAR uses -1 for "unknown"; the body's own size is the better number, and the transfer size the fallback.
        long? size = Integer(content, "size") ?? Integer(response, "bodySize");
        if (size is < 0)
        {
            size = null;
        }

        return new HarEntry(
            number,
            Text(request, "method"),
            Text(request, "url"),
            (int)(Integer(response, "status") ?? 0),
            Text(response, "statusText"),
            Text(content, "mimeType"),
            size,
            Number(source, "time") ?? 0,
            Text(source, "startedDateTime"),
            source);
    }

    /// <summary>The property's value, or an undefined element when the object has no such property (or is not an object).</summary>
    internal static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) ? value : default;

    internal static string Text(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? string.Empty : string.Empty;

    internal static double? Number(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetDouble(out double number) ? number : null;

    internal static long? Integer(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out long number) ? number : null;
}
