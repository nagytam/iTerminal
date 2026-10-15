using iTerminal.Core.Data.Documents;

namespace iTerminal.Core.Documents.Extractors;

/// <summary>
/// Images. Text comes from two places and they are kept separate on purpose.
///
/// <para>The first is what the image says about itself — captions, titles, EXIF descriptions, XMP —
/// read by <see cref="ImageMetadataReader"/> straight from the header bytes. It is free, it always
/// works, and for a photograph it is usually the only text there is worth finding.</para>
///
/// <para>The second is OCR, which is neither free nor always available. It runs only when an
/// <see cref="IOcrEngine"/> reports itself installed, and it is what makes a folder of scanned
/// receipts searchable. An image with neither is still indexed by name and recorded as
/// <see cref="DocumentExtractionStatus.Empty"/> with a note naming the reason, so a user looking at
/// an unsearchable scan is told it needs OCR rather than left to guess.</para>
/// </summary>
public sealed class ImageTextExtractor(IOcrEngine? ocrEngine = null) : IDocumentTextExtractor
{
    private readonly IOcrEngine? _ocrEngine = ocrEngine;

    /// <summary>
    /// Formats worth putting through OCR. GIF and BMP are indexed for their metadata but not
    /// recognised: they are screenshots and icons in practice, and OCR on an icon costs a process
    /// start to find nothing.
    /// </summary>
    private static readonly HashSet<string> OcrExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp",
    };

    public IReadOnlyCollection<string> Extensions { get; } =
    [
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp",
    ];

    public DocumentTextResult Extract(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            ImageMetadataReader.ImageMetadata metadata = ImageMetadataReader.Read(path);
            string ocrText = RunOcr(path, cancellationToken);

            string combined = DocumentTextLimits.Normalize(
                string.Join('\n', new[] { metadata.ToText(), ocrText }.Where(part => part.Length > 0)));

            if (combined.Length > 0)
            {
                return new DocumentTextResult(combined, DocumentExtractionStatus.Extracted)
                {
                    Title = metadata.Title,
                    Author = metadata.Author,
                    Note = ocrText.Length > 0 ? "text recognised by OCR" : string.Empty,
                };
            }

            return DocumentTextResult.None(DocumentExtractionStatus.Empty, DescribeEmptyResult(path)) with
            {
                Title = metadata.Title,
                Author = metadata.Author,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return DocumentTextResult.None(DocumentExtractionStatus.Failed, exception.Message);
        }
    }

    private string RunOcr(string path, CancellationToken cancellationToken)
    {
        if (_ocrEngine is null
            || !_ocrEngine.IsAvailable
            || !OcrExtensions.Contains(System.IO.Path.GetExtension(path)))
        {
            return string.Empty;
        }

        return _ocrEngine.Recognize(path, cancellationToken).Trim();
    }

    /// <summary>
    /// Why an image produced nothing — the distinction the user actually needs, since "no OCR engine
    /// installed" is fixable and "this picture has no text in it" is not.
    /// </summary>
    private string DescribeEmptyResult(string path)
    {
        if (!OcrExtensions.Contains(System.IO.Path.GetExtension(path)))
        {
            return "no embedded metadata; this format is not sent to OCR";
        }

        return _ocrEngine is { IsAvailable: true }
            ? "no embedded metadata and OCR found no text"
            : $"no embedded metadata; {_ocrEngine?.DisplayName ?? "no OCR engine"}";
    }
}
