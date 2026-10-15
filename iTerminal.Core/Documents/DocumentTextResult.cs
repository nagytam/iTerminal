using iTerminal.Core.Data.Documents;

namespace iTerminal.Core.Documents;

/// <summary>
/// What an <see cref="IDocumentTextExtractor"/> got out of one file: the text, whatever metadata
/// came with it, and — just as importantly — a status saying why there is no text when there is
/// none. A scanned PDF and a PDF that failed to parse both yield an empty string, and the index
/// would be lying if it recorded them the same way.
/// </summary>
public sealed record DocumentTextResult(string Text, DocumentExtractionStatus Status)
{
    /// <summary>A result carrying nothing but a status, for the "there was no text" cases.</summary>
    public static DocumentTextResult None(DocumentExtractionStatus status, string note = "") =>
        new(string.Empty, status) { Note = note };

    /// <summary>
    /// The document's own title, when it has one. Never guessed from the file name — that is the
    /// index's job, and a title invented here would be indistinguishable from a real one.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    /// <summary>Pages, where the format has any. 0 otherwise.</summary>
    public int PageCount { get; init; }

    /// <summary>Why the result looks the way it does, when that is worth recording. Usually empty.</summary>
    public string Note { get; init; } = string.Empty;
}
