using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using iConsole.Localization;

namespace iConsole.Har;

/// <summary>
/// The text of one HAR entry as the viewer shows it: the request line, the status and timings, the query string, the request
/// and response headers, and both bodies. A JSON body is pretty-printed; a text body (a base64 one included) is shown as text;
/// any other body is named by its size and not shown.
/// </summary>
public static class HarEntryDetails
{
    /// <summary>A body is shown up to this many characters; the rest is cut and the cut is named.</summary>
    public const int MaxBodyCharacters = 100_000;

    private static readonly string[] TimingNames = ["blocked", "dns", "connect", "send", "wait", "receive", "ssl"];

    public static IReadOnlyList<string> Build(HarEntry entry)
    {
        JsonElement source = entry.Source;
        JsonElement request = HarDocument.Property(source, "request");
        JsonElement response = HarDocument.Property(source, "response");
        JsonElement content = HarDocument.Property(response, "content");
        JsonElement postData = HarDocument.Property(request, "postData");

        var lines = new List<string>
        {
            entry.Method + " " + entry.Url,
            Label("Har.Column.Status", "Status") + ": " + (entry.Status > 0 ? entry.Status.ToString(CultureInfo.InvariantCulture) : "-") + " " + entry.StatusText,
            Label("Har.Detail.Started", "Started") + ": " + entry.StartedDateTime,
            Label("Har.Detail.Time", "Time") + ": " + entry.TimeMs.ToString("N1", CultureInfo.CurrentCulture) + " ms",
        };

        string server = HarDocument.Text(source, "serverIPAddress");
        if (server.Length > 0)
        {
            lines.Add(Label("Har.Detail.Server", "Server") + ": " + server);
        }

        AddTimings(lines, HarDocument.Property(source, "timings"));
        AddPairs(lines, HarDocument.Property(request, "queryString"), Label("Har.Detail.QueryString", "Query string"), " = ");
        AddPairs(lines, HarDocument.Property(request, "headers"), Label("Har.Detail.RequestHeaders", "Request headers"), ": ");

        if (postData.ValueKind == JsonValueKind.Object)
        {
            string mime = HarDocument.Text(postData, "mimeType");
            AddSection(lines, Labelled("Har.Detail.RequestBody", "Request body", mime));
            if (HarDocument.Property(postData, "params") is { ValueKind: JsonValueKind.Array } parameters && parameters.GetArrayLength() > 0)
            {
                lines.AddRange(PairLines(parameters, " = "));
            }
            else
            {
                lines.AddRange(BodyLines(HarDocument.Text(postData, "text"), mime, base64: false));
            }
        }

        AddPairs(lines, HarDocument.Property(response, "headers"), Label("Har.Detail.ResponseHeaders", "Response headers"), ": ");

        string responseMime = HarDocument.Text(content, "mimeType");
        AddSection(lines, Labelled("Har.Detail.ResponseBody", "Response body", responseMime));
        lines.AddRange(BodyLines(HarDocument.Text(content, "text"), responseMime, HarDocument.Text(content, "encoding") == "base64"));
        return lines;
    }

    private static void AddTimings(List<string> lines, JsonElement timings)
    {
        if (timings.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var phases = new List<string>();
        foreach (string name in TimingNames)
        {
            // HAR uses -1 for a phase that did not happen.
            if (HarDocument.Number(timings, name) is { } milliseconds and >= 0)
            {
                phases.Add($"  {name} {milliseconds.ToString("N1", CultureInfo.CurrentCulture)} ms");
            }
        }

        if (phases.Count > 0)
        {
            AddSection(lines, Label("Har.Detail.Timings", "Timings"));
            lines.AddRange(phases);
        }
    }

    /// <summary>Adds a titled list of <c>name</c>/<c>value</c> objects (headers, the query string) when there are any.</summary>
    private static void AddPairs(List<string> lines, JsonElement array, string title, string separator)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
        {
            return;
        }

        AddSection(lines, title);
        lines.AddRange(PairLines(array, separator));
    }

    private static IEnumerable<string> PairLines(JsonElement array, string separator) =>
        array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => "  " + HarDocument.Text(item, "name") + separator + HarDocument.Text(item, "value"));

    private static void AddSection(List<string> lines, string title)
    {
        lines.Add(string.Empty);
        lines.Add(title);
    }

    private static IReadOnlyList<string> BodyLines(string text, string mimeType, bool base64)
    {
        if (text.Length == 0)
        {
            return ["  " + Label("Har.Detail.NoBody", "(no body)")];
        }

        if (base64)
        {
            long size = Base64Size(text);
            if (!IsTextMime(mimeType))
            {
                return ["  " + Format("Har.Detail.Binary", "(binary, {0:N0} bytes, not shown)", size)];
            }

            try
            {
                text = Encoding.UTF8.GetString(Convert.FromBase64String(text));
            }
            catch (FormatException)
            {
                return ["  " + Format("Har.Detail.Binary", "(binary, {0:N0} bytes, not shown)", size)];
            }
        }

        bool cut = text.Length > MaxBodyCharacters;
        string shown = cut ? text[..MaxBodyCharacters] : text;
        if (!cut && mimeType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            shown = PrettyJson(shown) ?? shown;
        }

        var lines = shown.Replace("\r\n", "\n").Split('\n').Select(line => "  " + line).ToList();
        if (cut)
        {
            lines.Add("  " + Format("Har.Detail.Truncated", "(cut at {0:N0} characters)", MaxBodyCharacters));
        }

        return lines;
    }

    /// <summary>The bytes a base64 string decodes to, worked out from its length so that a binary body is not decoded.</summary>
    private static long Base64Size(string text)
    {
        int padding = text.EndsWith("==", StringComparison.Ordinal) ? 2 : text.EndsWith('=') ? 1 : 0;
        return text.Length / 4 * 3L - padding;
    }

    private static bool IsTextMime(string mimeType)
    {
        string mime = mimeType.ToLowerInvariant();
        return mime.StartsWith("text/", StringComparison.Ordinal)
            || mime.Contains("json", StringComparison.Ordinal)
            || mime.Contains("xml", StringComparison.Ordinal)
            || mime.Contains("javascript", StringComparison.Ordinal)
            || mime.Contains("x-www-form-urlencoded", StringComparison.Ordinal);
    }

    private static string? PrettyJson(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
                // Keep non-ASCII text readable: the default encoder writes it as \u escapes.
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Labelled(string key, string fallback, string mimeType) =>
        mimeType.Length == 0 ? Label(key, fallback) : Label(key, fallback) + " (" + mimeType + ")";

    private static string Label(string key, string fallback) => UiText.Get(key, fallback);

    private static string Format(string key, string fallback, params object[] arguments) =>
        string.Format(UiText.Get(key, fallback), arguments);
}
