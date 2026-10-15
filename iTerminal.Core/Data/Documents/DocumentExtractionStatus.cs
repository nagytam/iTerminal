namespace iTerminal.Core.Data.Documents;

/// <summary>
/// How text extraction went for one file. Recorded on the row so the results list can say why a
/// document has no text without re-opening it, and so a rescan does not keep retrying a file that
/// is structurally impossible to read.
/// </summary>
public enum DocumentExtractionStatus
{
    /// <summary>The file is indexed but has not been through an extractor yet.</summary>
    NotAttempted = 0,

    /// <summary>Text was extracted and is in the content table.</summary>
    Extracted = 1,

    /// <summary>An extractor ran and found nothing — an empty file, or a scanned page with no OCR.</summary>
    Empty = 2,

    /// <summary>No extractor handles this extension. The file is still searchable by name.</summary>
    Unsupported = 3,

    /// <summary>Extraction was attempted and threw. <see cref="DocumentEntry.ExtractionNote"/> says what.</summary>
    Failed = 4,

    /// <summary>The file is too large to extract under the configured cap; only its name is indexed.</summary>
    TooLarge = 5,
}
