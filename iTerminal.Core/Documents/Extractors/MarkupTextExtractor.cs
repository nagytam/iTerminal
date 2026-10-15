using System.Net;
using System.Text;
using iTerminal.Core.Data.Documents;

namespace iTerminal.Core.Documents.Extractors;

/// <summary>
/// HTML and XML: the text is what is left once the tags are gone.
///
/// <para>Stripping is done by a hand-written scanner rather than a parser because the input is
/// whatever happens to be in the folder — half-saved pages, XML fragments, files that are HTML in
/// name only. A scanner reads all of them and never throws; a parser would reject exactly the files
/// most in need of being searchable. <c>&lt;script&gt;</c> and <c>&lt;style&gt;</c> bodies are
/// dropped whole: minified JavaScript is not text anyone will ever search for, and one page of it
/// can outweigh the prose on the page it came from.</para>
/// </summary>
public sealed class MarkupTextExtractor : IDocumentTextExtractor
{
    public IReadOnlyCollection<string> Extensions { get; } =
    [
        ".html", ".htm", ".xhtml", ".xml", ".xaml", ".svg", ".rss", ".atom", ".csproj", ".slnx", ".config",
    ];

    public DocumentTextResult Extract(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            string markup = PlainTextExtractor.Decode(File.ReadAllBytes(path));
            string title = ExtractTitle(markup);
            string text = DocumentTextLimits.Normalize(StripTags(markup));

            return text.Length == 0
                ? DocumentTextResult.None(DocumentExtractionStatus.Empty) with { Title = title }
                : new DocumentTextResult(text, DocumentExtractionStatus.Extracted) { Title = title };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return DocumentTextResult.None(DocumentExtractionStatus.Failed, exception.Message);
        }
    }

    /// <summary>
    /// The contents of the first <c>&lt;title&gt;</c> element, which is the one piece of an HTML
    /// document that is genuinely its title. Empty for XML, which has no such convention.
    /// </summary>
    internal static string ExtractTitle(string markup)
    {
        int open = markup.IndexOf("<title", StringComparison.OrdinalIgnoreCase);
        if (open < 0)
        {
            return string.Empty;
        }

        int contentStart = markup.IndexOf('>', open);
        if (contentStart < 0)
        {
            return string.Empty;
        }

        int close = markup.IndexOf("</title", contentStart, StringComparison.OrdinalIgnoreCase);
        if (close < 0)
        {
            return string.Empty;
        }

        string title = WebUtility.HtmlDecode(markup[(contentStart + 1)..close]);
        return CollapseWhitespace(title).Trim();
    }

    /// <summary>
    /// Removes tags, comments and the bodies of script and style elements, decoding entities in
    /// what is left. Block-level tags become line breaks so the preview keeps the shape of the page
    /// instead of arriving as one paragraph.
    /// </summary>
    internal static string StripTags(string markup)
    {
        StringBuilder builder = new(markup.Length);
        int index = 0;

        while (index < markup.Length)
        {
            char character = markup[index];

            if (character != '<')
            {
                builder.Append(character);
                index++;
                continue;
            }

            if (StartsWith(markup, index, "<!--"))
            {
                int end = markup.IndexOf("-->", index, StringComparison.Ordinal);
                index = end < 0 ? markup.Length : end + 3;
                continue;
            }

            string? skipped = SkippedElement(markup, index);
            if (skipped is not null)
            {
                int end = markup.IndexOf($"</{skipped}", index, StringComparison.OrdinalIgnoreCase);
                if (end < 0)
                {
                    index = markup.Length;
                    continue;
                }

                int after = markup.IndexOf('>', end);
                index = after < 0 ? markup.Length : after + 1;
                continue;
            }

            int tagEnd = markup.IndexOf('>', index);
            if (tagEnd < 0)
            {
                // An unterminated '<' is a literal less-than sign, not the start of a tag.
                builder.Append(markup[index..]);
                break;
            }

            if (IsBlockTag(markup, index, tagEnd))
            {
                builder.Append('\n');
            }
            else
            {
                // An inline tag still separates words: "a<b>b</b>" is two words, not "ab".
                builder.Append(' ');
            }

            index = tagEnd + 1;
        }

        return CollapseWhitespace(WebUtility.HtmlDecode(builder.ToString()));
    }

    private static readonly string[] SkippedElements = ["script", "style"];

    private static readonly string[] BlockTags =
    [
        "p", "div", "br", "li", "tr", "td", "th", "h1", "h2", "h3", "h4", "h5", "h6",
        "section", "article", "header", "footer", "blockquote", "pre", "table", "ul", "ol",
    ];

    private static string? SkippedElement(string markup, int start)
    {
        foreach (string element in SkippedElements)
        {
            if (!StartsWith(markup, start + 1, element))
            {
                continue;
            }

            // "<scriptable>" is not "<script>"; the name has to end where the element's does.
            int after = start + 1 + element.Length;
            if (after >= markup.Length || markup[after] == '>' || char.IsWhiteSpace(markup[after]))
            {
                return element;
            }
        }

        return null;
    }

    private static bool IsBlockTag(string markup, int start, int tagEnd)
    {
        int nameStart = start + 1;
        if (nameStart < markup.Length && markup[nameStart] == '/')
        {
            nameStart++;
        }

        int nameEnd = nameStart;
        while (nameEnd < tagEnd && !char.IsWhiteSpace(markup[nameEnd]) && markup[nameEnd] != '/')
        {
            nameEnd++;
        }

        if (nameEnd <= nameStart)
        {
            return false;
        }

        ReadOnlySpan<char> name = markup.AsSpan(nameStart, nameEnd - nameStart);
        foreach (string blockTag in BlockTags)
        {
            if (name.Equals(blockTag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StartsWith(string text, int index, string value) =>
        index >= 0 && index + value.Length <= text.Length
        && text.AsSpan(index, value.Length).Equals(value, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Squeezes horizontal whitespace runs to one space while keeping line breaks, so the tag
    /// separators inserted above do not leave the preview full of gaps.
    /// </summary>
    internal static string CollapseWhitespace(string text)
    {
        StringBuilder builder = new(text.Length);
        bool pendingSpace = false;

        foreach (char character in text)
        {
            if (character == '\n' || character == '\r')
            {
                pendingSpace = false;
                builder.Append('\n');
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
