namespace iTerminal.Core.Documents;

/// <summary>
/// Pulls searchable text out of one family of file formats. One implementation per family, chosen
/// by extension through <see cref="DocumentTextExtractorFactory"/>.
///
/// <para>Extraction is synchronous on purpose. It is CPU and disk work with no waiting in it, so
/// there is nothing for an async signature to give back; what keeps it off the render thread is
/// that the only caller is the indexing job, which already runs on a background job thread. The one
/// extractor that really does wait on something — <see cref="Extractors.ImageTextExtractor"/>,
/// which may shell out to an OCR engine — waits inside its own bounded timeout rather than making
/// every other extractor pretend to be async.</para>
/// </summary>
public interface IDocumentTextExtractor
{
    /// <summary>Extensions this extractor claims, lower-case and including the dot.</summary>
    IReadOnlyCollection<string> Extensions { get; }

    /// <summary>
    /// Reads <paramref name="path"/> and returns its text. Never throws for a malformed or
    /// unreadable document: a file that cannot be parsed comes back as
    /// <see cref="Data.Documents.DocumentExtractionStatus.Failed"/> with a note, because one bad
    /// file must not stop a scan of ten thousand.
    /// </summary>
    DocumentTextResult Extract(string path, CancellationToken cancellationToken = default);
}
