namespace iConsole.Markdown;

/// <summary>How a run of text is drawn. Flags combine: bold code is <c>Bold | Code</c>.</summary>
[Flags]
public enum MarkdownStyle
{
    Plain = 0,
    Bold = 1,
    Italic = 2,
    Code = 4,
    Link = 8,
}

/// <summary>A run of text with one style.</summary>
public sealed record MarkdownSpan(string Text, MarkdownStyle Style = MarkdownStyle.Plain);

public enum MarkdownBlockKind
{
    Paragraph,
    Heading,
    ListItem,
    Quote,
    Code,
    Rule,
}

/// <summary>
/// One block of a document. <see cref="Level"/> is the heading level (1–6) or the list nesting depth;
/// <see cref="Marker"/> is a list item's bullet or number.
/// </summary>
public sealed record MarkdownBlock(MarkdownBlockKind Kind, IReadOnlyList<MarkdownSpan> Spans, int Level = 0, string Marker = "");

/// <summary>One screen row of a laid-out document.</summary>
public sealed record MarkdownLine(IReadOnlyList<MarkdownSpan> Spans, MarkdownBlockKind Kind, int Level);
