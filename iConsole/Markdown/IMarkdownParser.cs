namespace iConsole.Markdown;

/// <summary>
/// Reads Markdown into the blocks the viewer lays out. The implementation is in <c>iConsole.Formats</c>, so the Markdown viewer
/// here never references the parser.
/// </summary>
public interface IMarkdownParser
{
    /// <summary>Reads the text into blocks. Anything that is not Markdown is kept as plain text, so a document always shows.</summary>
    IReadOnlyList<MarkdownBlock> Parse(string? markdown);
}
