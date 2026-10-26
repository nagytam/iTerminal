namespace iConsole.Markdown;

/// <summary>Lays blocks out as screen rows: word-wrapped text, indented list items and quotes, and a blank row between blocks.</summary>
public static class MarkdownLayout
{
    public static IReadOnlyList<MarkdownLine> Wrap(IEnumerable<MarkdownBlock> blocks, int width)
    {
        width = Math.Max(1, width);
        var lines = new List<MarkdownLine>();
        var list = blocks.ToList();

        for (var index = 0; index < list.Count; index++)
        {
            var block = list[index];
            switch (block.Kind)
            {
                case MarkdownBlockKind.Rule:
                    lines.Add(new MarkdownLine([new MarkdownSpan(new string('─', width))], block.Kind, 0));
                    break;

                case MarkdownBlockKind.Code:
                    foreach (var span in block.Spans)
                    {
                        // Code is not wrapped: a line that is too long is cut at the edge, as a terminal would.
                        var text = span.Text.Length > width ? span.Text[..width] : span.Text;
                        lines.Add(new MarkdownLine([span with { Text = text }], block.Kind, 0));
                    }
                    break;

                case MarkdownBlockKind.Heading:
                    WrapInto(lines, Bold(block.Spans), width, string.Empty, string.Empty, block.Kind, block.Level);
                    break;

                case MarkdownBlockKind.ListItem:
                    var indent = new string(' ', block.Level * 2);
                    var hanging = new string(' ', indent.Length + block.Marker.Length + 1);
                    WrapInto(lines, block.Spans, width, indent + block.Marker + " ", hanging, block.Kind, block.Level);
                    break;

                case MarkdownBlockKind.Quote:
                    WrapInto(lines, block.Spans, width, "│ ", "│ ", block.Kind, 0);
                    break;

                default:
                    WrapInto(lines, block.Spans, width, string.Empty, string.Empty, block.Kind, 0);
                    break;
            }

            // Consecutive list items, code lines and quote lines stay together; every other block is followed by a blank row.
            var joinsNext = index + 1 < list.Count && JoinsNext(block.Kind, list[index + 1].Kind);
            if (!joinsNext && index + 1 < list.Count)
            {
                lines.Add(new MarkdownLine([], MarkdownBlockKind.Paragraph, 0));
            }
        }

        return lines;
    }

    /// <summary>
    /// The parser makes one block per source line for list items, code and quotes, so a run of them is one unit: a
    /// code block of five lines is five rows, not five rows with a blank between each.
    /// </summary>
    private static bool JoinsNext(MarkdownBlockKind current, MarkdownBlockKind next) =>
        current == next && current is MarkdownBlockKind.ListItem or MarkdownBlockKind.Code or MarkdownBlockKind.Quote;

    private static IReadOnlyList<MarkdownSpan> Bold(IReadOnlyList<MarkdownSpan> spans) =>
        spans.Select(span => span with { Style = span.Style | MarkdownStyle.Bold }).ToList();

    private static void WrapInto(List<MarkdownLine> lines, IReadOnlyList<MarkdownSpan> spans, int width,
        string firstPrefix, string restPrefix, MarkdownBlockKind kind, int level)
    {
        var prefix = firstPrefix;
        var content = new List<MarkdownSpan>();
        var contentWidth = 0;
        var pendingSpace = string.Empty;

        void Commit()
        {
            var line = new List<MarkdownSpan>(content.Count + 1);
            if (prefix.Length > 0)
            {
                line.Add(new MarkdownSpan(prefix));
            }

            line.AddRange(content);
            lines.Add(new MarkdownLine(line, kind, level));
            content.Clear();
            contentWidth = 0;
            pendingSpace = string.Empty;
            prefix = restPrefix;
        }

        foreach (var (text, style) in Words(spans))
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                // A space at the start of a row is dropped; one between words is kept until the next word says whether it fits.
                pendingSpace = contentWidth > 0 ? text : string.Empty;
                continue;
            }

            var available = Math.Max(1, width - prefix.Length);
            if (contentWidth > 0 && contentWidth + pendingSpace.Length + text.Length > available)
            {
                Commit();
                available = Math.Max(1, width - prefix.Length);
            }

            if (contentWidth > 0 && pendingSpace.Length > 0)
            {
                content.Add(new MarkdownSpan(pendingSpace, style));
                contentWidth += pendingSpace.Length;
                pendingSpace = string.Empty;
            }

            // A word longer than a whole row is broken across rows.
            var word = text;
            while (word.Length > available)
            {
                content.Add(new MarkdownSpan(word[..available], style));
                contentWidth += available;
                Commit();
                available = Math.Max(1, width - prefix.Length);
                word = word[available..];
            }

            if (word.Length > 0)
            {
                content.Add(new MarkdownSpan(word, style));
                contentWidth += word.Length;
            }
        }

        if (content.Count > 0)
        {
            Commit();
        }
    }

    /// <summary>Splits spans into words and the spaces between them, keeping each piece's style.</summary>
    private static IEnumerable<(string Text, MarkdownStyle Style)> Words(IEnumerable<MarkdownSpan> spans)
    {
        foreach (var span in spans)
        {
            var start = 0;
            for (var i = 1; i <= span.Text.Length; i++)
            {
                if (i == span.Text.Length || char.IsWhiteSpace(span.Text[i]) != char.IsWhiteSpace(span.Text[start]))
                {
                    yield return (span.Text[start..i], span.Style);
                    start = i;
                }
            }
        }
    }
}
