using iTerminal.Core.Data.Documents;
using iTerminal.Core.Documents.Extractors;

namespace iTerminal.Core.Documents;

/// <summary>
/// Decides which extractor — if any — handles a file, and what kind of document it is.
///
/// <para>The mapping is built once from each extractor's own <see cref="IDocumentTextExtractor.Extensions"/>
/// rather than written out here a second time, so adding a format means touching one file. The
/// factory is constructed with the extractors instead of newing them up so the OCR engine can be
/// injected all the way down, and so a test can hand in a stub extractor and index a fake format.</para>
/// </summary>
public sealed class DocumentTextExtractorFactory
{
    private readonly Dictionary<string, IDocumentTextExtractor> _byExtension =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The production set. The OCR engine is optional and only reaches
    /// <see cref="ImageTextExtractor"/>; passing null means images are indexed by their embedded
    /// metadata alone, which is exactly what happens on a machine with no OCR tool installed.
    /// </summary>
    public DocumentTextExtractorFactory(IOcrEngine? ocrEngine = null)
        : this(
        [
            new PlainTextExtractor(),
            new MarkupTextExtractor(),
            new OfficeTextExtractor(),
            new PdfTextExtractor(),
            new ImageTextExtractor(ocrEngine),
        ])
    {
    }

    public DocumentTextExtractorFactory(IReadOnlyCollection<IDocumentTextExtractor> extractors)
    {
        foreach (IDocumentTextExtractor extractor in extractors)
        {
            foreach (string extension in extractor.Extensions)
            {
                // First registration wins, so the order of the list above is the precedence order.
                // Nothing overlaps today, but an extractor added later that claims .xml as well
                // should not silently steal it from the markup one.
                _byExtension.TryAdd(extension, extractor);
            }
        }
    }

    /// <summary>Every extension some extractor claims, for the settings page's hint line.</summary>
    public IReadOnlyCollection<string> SupportedExtensions => _byExtension.Keys;

    /// <summary>The extractor for <paramref name="path"/>, or null when nothing claims its extension.</summary>
    public IDocumentTextExtractor? GetExtractor(string path) =>
        _byExtension.GetValueOrDefault(System.IO.Path.GetExtension(path));

    /// <summary>Whether text can be extracted from <paramref name="path"/> at all.</summary>
    public bool IsSupported(string path) => GetExtractor(path) is not null;

    /// <summary>
    /// The plain-text extensions, taken from the extractor itself so the two cannot drift apart.
    /// Static because <see cref="GetKind"/> is called once per file during a scan and once per row
    /// while the results list draws.
    /// </summary>
    private static readonly HashSet<string> PlainTextExtensions =
        new(new PlainTextExtractor().Extensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What kind of document a file is. Derived from the extension rather than from the extractor
    /// that claims it, because the kind is a fact about the file that the results list shows even
    /// for files no extractor handles.
    /// </summary>
    public static DocumentKind GetKind(string path)
    {
        string extension = System.IO.Path.GetExtension(path);

        return extension.ToLowerInvariant() switch
        {
            ".pdf" => DocumentKind.Pdf,

            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".webp" =>
                DocumentKind.Image,

            ".docx" or ".docm" or ".xlsx" or ".xlsm" or ".pptx" or ".pptm" or ".odt" or ".ods" or ".odp" =>
                DocumentKind.Office,

            ".html" or ".htm" or ".xhtml" or ".xml" or ".xaml" or ".svg" or ".rss" or ".atom"
                or ".csproj" or ".slnx" or ".config" =>
                DocumentKind.Markup,

            _ => PlainTextExtensions.Contains(extension) ? DocumentKind.Text : DocumentKind.Unknown,
        };
    }
}
