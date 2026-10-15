namespace iTerminal.Core.Data.Documents;

/// <summary>
/// One indexed document, cached in SQLite by
/// <see cref="Services.Documents.IDocumentLibraryService"/>. This is the metadata half of the index
/// and the half that is held in memory: the extracted text is a separate row
/// (<see cref="DocumentContentEntry"/>) precisely so that listing, sorting and counting documents
/// never drags megabytes of body text along with it.
///
/// <para><see cref="Size"/> and <see cref="ModifiedUtc"/> exist only as a change stamp — a rescan
/// re-extracts a file only when one of them moved, which matters far more here than it does for
/// music tags: extracting a PDF or OCR'ing an image is seconds of work, not microseconds.</para>
/// </summary>
public class DocumentEntry
{
    /// <summary>Full path of the file. The primary key: one row per file on disk.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>File name including the extension. Stored so name search never has to split paths.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Folder the file is in, relative to the configured documents root.</summary>
    public string RelativeFolder { get; set; } = string.Empty;

    /// <summary>Lower-case extension including the dot, or empty for a file that has none.</summary>
    public string Extension { get; set; } = string.Empty;

    public DocumentKind Kind { get; set; } = DocumentKind.Unknown;

    public DocumentExtractionStatus ExtractionStatus { get; set; } = DocumentExtractionStatus.NotAttempted;

    /// <summary>Why extraction produced what it did, when that is worth saying. Usually empty.</summary>
    public string ExtractionNote { get; set; } = string.Empty;

    /// <summary>Document title from its own metadata, when it has one. Not the file name.</summary>
    public string Title { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    /// <summary>Pages for a PDF, 0 for everything else and for a PDF whose page tree could not be read.</summary>
    public int PageCount { get; set; }

    /// <summary>Words in the extracted text. What the results list shows to say "there is text here".</summary>
    public int WordCount { get; set; }

    /// <summary>Characters of extracted text. 0 means the content row holds nothing.</summary>
    public int TextLength { get; set; }

    public long Size { get; set; }

    public DateTimeOffset ModifiedUtc { get; set; }

    public DateTimeOffset IndexedUtc { get; set; }

    /// <summary>The title when the document carries one, otherwise the file name — what a list shows.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? FileName : Title;
}
