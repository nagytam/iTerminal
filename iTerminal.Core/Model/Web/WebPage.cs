using System.Text.RegularExpressions;

namespace iTerminal.Core.Model.Web;

[Flags]
internal enum WebStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strike = 8,
    Code = 16,
    Heading = 32,
    Link = 64,
    Quote = 128,
    Dim = 256,
    Mark = 512,
}

/// <summary>
/// A run of text on one line. A span with <see cref="Field"/> set is a form control: its text is only
/// the blanks that reserve the room, and what shows is drawn from the field's current state.
/// </summary>
internal readonly record struct WebSpan(string Text, WebStyle Style, int Link = -1, int Field = -1);

/// <summary>One finished line of a page. Immutable, so a laid-out block can be reused.</summary>
internal sealed class WebLine(WebSpan[] spans, string[]? anchors = null)
{
    public static readonly WebLine Empty = new([]);

    public WebSpan[] Spans { get; } = spans;

    /// <summary>The <c>id</c>s and <c>name</c>s of the elements that start on this line, for <c>#fragment</c> links.</summary>
    public string[]? Anchors { get; } = anchors;

    public int Length
    {
        get
        {
            var length = 0;
            foreach (var span in Spans)
            {
                length += span.Text.Length;
            }

            return length;
        }
    }

    public bool IsBlank => Spans.All(s => s.Field < 0 && string.IsNullOrWhiteSpace(s.Text));

    public string Text => string.Concat(Spans.Select(s => s.Text));
}

/// <summary>Something on the page the keyboard can land on: a link or a form control.</summary>
internal readonly record struct WebItem(bool IsField, int Id, int Line, int Column);

/// <summary>A page laid out for one window width.</summary>
internal sealed class WebPage
{
    public required int Width { get; init; }
    public required IReadOnlyList<WebLine> Lines { get; init; }
    public required IReadOnlyList<WebItem> Items { get; init; }
    public required IReadOnlyDictionary<string, int> Anchors { get; init; }

    public static WebPage FromText(string text, int width, WebStyle style = WebStyle.None)
    {
        List<WebLine> lines = [];
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Replace("\t", "    ");
            if (line.Length == 0)
            {
                lines.Add(WebLine.Empty);
                continue;
            }

            for (var start = 0; start < line.Length; start += Math.Max(1, width))
            {
                lines.Add(new WebLine([new WebSpan(line.Substring(start, Math.Min(Math.Max(1, width), line.Length - start)), style)]));
            }
        }

        return new WebPage { Width = width, Lines = lines, Items = [], Anchors = new Dictionary<string, int>() };
    }
}

/// <summary>Turning what a person typed into the address bar into something that can be fetched.</summary>
internal static partial class WebAddress
{
    public const string Blank = "about:blank";
    public const string DefaultSearch = "https://html.duckduckgo.com/html/?q={0}";

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*:")]
    private static partial Regex SchemeRegex();

    [GeneratedRegex(@"^[a-zA-Z]:[\\/]")]
    private static partial Regex DrivePathRegex();

    public static string FromInput(string input, string? searchTemplate = null)
    {
        var text = input.Trim();
        if (text.Length == 0)
        {
            return Blank;
        }

        if (DrivePathRegex().IsMatch(text) || text.StartsWith('/') || text.StartsWith("~/", StringComparison.Ordinal) || text.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return FileUrl(text);
        }

        if (SchemeRegex().IsMatch(text))
        {
            var scheme = text[..text.IndexOf(':')].ToLowerInvariant();
            // "localhost:8080" and "host:80" look like a scheme; a real scheme is followed by "//" or is one we know.
            if (scheme is "http" or "https" or "file" or "data" or "about" or "view-source" || text.Contains("://", StringComparison.Ordinal))
            {
                return text;
            }
        }

        var looksLikeHost = !text.Contains(' ') && (text.Contains('.') || text.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || text.StartsWith('['));
        if (looksLikeHost)
        {
            var local = text.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || text.StartsWith("127.", StringComparison.Ordinal);
            return (local ? "http://" : "https://") + text;
        }

        return string.Format(searchTemplate is { Length: > 0 } ? searchTemplate : DefaultSearch, Uri.EscapeDataString(text).Replace("%20", "+"));
    }

    public static string FileUrl(string path)
    {
        if (path.StartsWith("~/", StringComparison.Ordinal))
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        }

        try
        {
            return new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }
        catch (Exception)
        {
            return "file:///" + path.Replace('\\', '/');
        }
    }

    public static string Resolve(string baseUrl, string href)
    {
        href = href.Trim();
        if (href.Length == 0)
        {
            return baseUrl;
        }

        if (SchemeRegex().IsMatch(href) && !DrivePathRegex().IsMatch(href))
        {
            return href;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parent) || parent.Scheme is "about" or "data")
        {
            return href;
        }

        return Uri.TryCreate(parent, href, out var combined) ? combined.AbsoluteUri : href;
    }

    public static string WithoutFragment(string url)
    {
        var index = url.IndexOf('#');
        return index < 0 ? url : url[..index];
    }

    public static string Fragment(string url)
    {
        var index = url.IndexOf('#');
        return index < 0 ? string.Empty : Uri.UnescapeDataString(url[(index + 1)..]);
    }

    public static bool IsSameDocument(string a, string b) =>
        string.Equals(WithoutFragment(a), WithoutFragment(b), StringComparison.Ordinal);
}
