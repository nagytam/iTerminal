namespace iTerminal.Core.Data.Documents;

/// <summary>
/// What kind of document a file is, decided from its extension by
/// <see cref="Core.Documents.DocumentTextExtractorFactory"/>. The kind is stored on the row rather
/// than re-derived on every draw because the results list groups and colours by it, and because it
/// is what tells the user why a file has no text: an <see cref="Image"/> with no text was never
/// OCR'd, a <see cref="Pdf"/> with no text is a scan.
/// </summary>
public enum DocumentKind
{
    /// <summary>No extractor claims the extension; the file is indexed by name and size only.</summary>
    Unknown = 0,

    /// <summary>Plain text, read as-is: .txt, .md, .csv, .log, source files.</summary>
    Text = 1,

    /// <summary>Tag markup whose text is what is left once the tags are gone: .html, .xml.</summary>
    Markup = 2,

    /// <summary>PDF, whose text lives in compressed content streams.</summary>
    Pdf = 3,

    /// <summary>A raster image: text comes from its embedded metadata, and from OCR when available.</summary>
    Image = 4,

    /// <summary>An Office Open XML package (.docx/.xlsx/.pptx) — a zip of XML parts.</summary>
    Office = 5,
}
