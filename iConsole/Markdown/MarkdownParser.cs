using System.Text;
using System.Text.RegularExpressions;

namespace iConsole.Markdown;

/// <summary>
/// Reads the common subset of Markdown into <see cref="MarkdownBlock"/>s: ATX headings, paragraphs, bullet and numbered
/// lists (nested by indentation), block quotes, fenced code, horizontal rules, and the inline emphasis, code and link
/// forms. Anything else is kept as plain text rather than rejected, so a document always shows.
/// </summary>
public static partial class MarkdownParser
{
    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^(\s*)([-*+]|\d+[.)])\s+(.*)$")]
    private static partial Regex ListPattern();

    public static IReadOnlyList<MarkdownBlock> Parse(string? markdown)
    {
        var blocks = new List<MarkdownBlock>();
        var paragraph = new List<string>();
        var inFence = false;

        foreach (var raw in (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = raw.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph(blocks, paragraph);
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Code, [new MarkdownSpan(raw.TrimEnd(), MarkdownStyle.Code)]));
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushParagraph(blocks, paragraph);
                continue;
            }

            if (IsRule(trimmed))
            {
                FlushParagraph(blocks, paragraph);
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Rule, []));
                continue;
            }

            var heading = HeadingPattern().Match(trimmed);
            if (heading.Success)
            {
                FlushParagraph(blocks, paragraph);
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Heading, MarkdownInline.Parse(heading.Groups[2].Value), heading.Groups[1].Length));
                continue;
            }

            var item = ListPattern().Match(raw.TrimEnd());
            if (item.Success)
            {
                FlushParagraph(blocks, paragraph);
                var depth = item.Groups[1].Length / 2;
                var marker = item.Groups[2].Value;
                var bullet = marker is "-" or "*" or "+" ? "•" : marker.TrimEnd(')', '.') + ".";
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.ListItem, MarkdownInline.Parse(item.Groups[3].Value), depth, bullet));
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                FlushParagraph(blocks, paragraph);
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Quote, MarkdownInline.Parse(trimmed[1..].TrimStart())));
                continue;
            }

            paragraph.Add(trimmed);
        }

        // An unclosed fence runs to the end of the document, which is what most renderers do.
        FlushParagraph(blocks, paragraph);
        return blocks;
    }

    private static void FlushParagraph(List<MarkdownBlock> blocks, List<string> paragraph)
    {
        if (paragraph.Count == 0)
        {
            return;
        }

        blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, MarkdownInline.Parse(string.Join(' ', paragraph))));
        paragraph.Clear();
    }

    /// <summary>Three or more of the same rule character (<c>-</c>, <c>*</c> or <c>_</c>), with optional spaces between.</summary>
    private static bool IsRule(string trimmed)
    {
        var compact = trimmed.Replace(" ", string.Empty);
        return compact.Length >= 3 && compact.All(c => c == compact[0]) && (compact[0] is '-' or '*' or '_');
    }
}

/// <summary>Inline emphasis, code and links. Delimiters that are not closed are kept as literal text.</summary>
public static class MarkdownInline
{
    public static IReadOnlyList<MarkdownSpan> Parse(string text) => Parse(text, MarkdownStyle.Plain);

    private static List<MarkdownSpan> Parse(string text, MarkdownStyle style)
    {
        var spans = new List<MarkdownSpan>();
        var plain = new StringBuilder();

        void FlushPlain()
        {
            if (plain.Length > 0)
            {
                spans.Add(new MarkdownSpan(plain.ToString(), style));
                plain.Clear();
            }
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];

            if (c == '\\' && i + 1 < text.Length)
            {
                plain.Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (c == '`')
            {
                var close = text.IndexOf('`', i + 1);
                if (close > i)
                {
                    FlushPlain();
                    spans.Add(new MarkdownSpan(text[(i + 1)..close], style | MarkdownStyle.Code));
                    i = close + 1;
                    continue;
                }
            }

            if ((c is '*' or '_') && CanOpen(text, i))
            {
                var doubled = i + 1 < text.Length && text[i + 1] == c;
                var delimiter = doubled ? new string(c, 2) : c.ToString();
                var close = FindClosing(text, delimiter, i + delimiter.Length);
                if (close > i + delimiter.Length)
                {
                    FlushPlain();
                    var inner = text[(i + delimiter.Length)..close];
                    spans.AddRange(Parse(inner, style | (doubled ? MarkdownStyle.Bold : MarkdownStyle.Italic)));
                    i = close + delimiter.Length;
                    continue;
                }
            }

            if (c == '[')
            {
                var labelEnd = text.IndexOf("](", i + 1, StringComparison.Ordinal);
                var urlEnd = labelEnd > 0 ? text.IndexOf(')', labelEnd + 2) : -1;
                if (urlEnd > 0)
                {
                    FlushPlain();
                    spans.AddRange(Parse(text[(i + 1)..labelEnd], style | MarkdownStyle.Link));
                    i = urlEnd + 1;
                    continue;
                }
            }

            plain.Append(c);
            i++;
        }

        FlushPlain();
        return spans;
    }

    /// <summary>
    /// An underscore inside a word (<c>file_name</c>) is not emphasis. Stars are always emphasis.
    /// </summary>
    private static bool CanOpen(string text, int index)
    {
        if (text[index] != '_' || index == 0)
        {
            return true;
        }

        return !char.IsLetterOrDigit(text[index - 1]);
    }

    private static int FindClosing(string text, string delimiter, int from)
    {
        var index = text.IndexOf(delimiter, from, StringComparison.Ordinal);
        while (index > 0 && delimiter[0] == '_' && index + delimiter.Length < text.Length && char.IsLetterOrDigit(text[index + delimiter.Length]))
        {
            index = text.IndexOf(delimiter, index + 1, StringComparison.Ordinal);
        }

        return index;
    }
}
