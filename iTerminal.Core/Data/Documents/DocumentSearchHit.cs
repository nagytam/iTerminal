namespace iTerminal.Core.Data.Documents;

/// <summary>
/// One hit from <see cref="Services.Documents.IDocumentLibraryService.SearchAsync"/>: the document,
/// where the match was, and a snippet of the surrounding text.
///
/// <para>The snippet is produced by the search itself rather than by the results list, because only
/// the search knows where in a document the terms actually matched — and the list must never open
/// the file to find out.</para>
/// </summary>
/// <param name="Entry">The indexed document that matched.</param>
/// <param name="Match">Which part of the document the query hit.</param>
/// <param name="Snippet">
/// A one-line excerpt around the match, already collapsed to single spaces. Empty for a name-only
/// hit, where the file name in the row is the excerpt.
/// </param>
/// <param name="Rank">
/// Lower sorts first, matching SQLite FTS5's own <c>rank</c> convention (it is a negative
/// relevance score). Name hits are given a rank below any content hit so an exact file name is
/// never buried under documents that merely mention it.
/// </param>
public sealed record DocumentSearchHit(
    DocumentEntry Entry,
    DocumentSearchMatch Match,
    string Snippet,
    double Rank);

/// <summary>Where in a document a query matched, which is what the results list labels each row with.</summary>
public enum DocumentSearchMatch
{
    /// <summary>The file name or the folder path matched; the body may not mention the terms at all.</summary>
    Name,

    /// <summary>The document's own title or author metadata matched.</summary>
    Metadata,

    /// <summary>The extracted body text matched — the hit the snippet is drawn from.</summary>
    Content,
}
