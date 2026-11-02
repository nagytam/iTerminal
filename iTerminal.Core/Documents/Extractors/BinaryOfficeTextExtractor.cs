using iConsole.Services;
using iTerminal.Core.Data.Documents;

namespace iTerminal.Core.Documents.Extractors;

/// <summary>
/// The Office formats <see cref="OfficeTextExtractor"/> cannot read: the Office 97–2003 binaries (.doc, .xls, .ppt and
/// their templates and shows), RTF, and the flat OpenDocument files (.fodt, .fods, .fodp, .fodg), which are XML with
/// no zip around them.
///
/// <para>The text comes from the same readers the F3 viewer uses, so the index and the viewer cannot disagree about
/// what a file says. Those readers take bytes, so the file is read whole first; the size cap in
/// <see cref="DocumentTextLimits.MaxFileSize"/> keeps that bounded.</para>
/// </summary>
public sealed class BinaryOfficeTextExtractor : IDocumentTextExtractor
{
    private const string CouldNotReadPrefix = "[Could not read";

    public IReadOnlyCollection<string> Extensions { get; } =
    [
        ".doc", ".dot", ".xls", ".xlt", ".ppt", ".pot", ".pps", ".rtf", ".fodt", ".fods", ".fodp", ".fodg",
    ];

    public DocumentTextResult Extract(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (new FileInfo(path).Length > DocumentTextLimits.MaxFileSize)
            {
                return DocumentTextResult.None(DocumentExtractionStatus.TooLarge);
            }

            byte[] content = File.ReadAllBytes(path);
            IReadOnlyList<string> lines = Render(System.IO.Path.GetExtension(path).ToLowerInvariant(), content);

            // The renderers report a file they could not read as a bracketed line rather than throwing, so the
            // note is what tells the index it failed, not an empty document.
            if (lines.Count > 0 && lines[0].StartsWith(CouldNotReadPrefix, StringComparison.Ordinal))
            {
                return DocumentTextResult.None(DocumentExtractionStatus.Failed, lines[0].Trim('[', ']'));
            }

            string text = DocumentTextLimits.Normalize(string.Join("\n", lines));
            return text.Length == 0
                ? DocumentTextResult.None(DocumentExtractionStatus.Empty)
                : new DocumentTextResult(text, DocumentExtractionStatus.Extracted);
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

    private static IReadOnlyList<string> Render(string extension, byte[] content) => extension switch
    {
        ".doc" or ".dot" => LegacyOfficeTextRenderer.RenderWord(content),
        ".xls" or ".xlt" => LegacyOfficeTextRenderer.RenderWorkbook(content),
        ".ppt" or ".pot" or ".pps" => LegacyOfficeTextRenderer.RenderPresentation(content),
        ".rtf" => RtfTextReader.Read(content),
        ".fodt" => OfficeTextRenderer.RenderOpenDocumentText(content),
        ".fods" => OfficeTextRenderer.RenderOpenDocumentSpreadsheet(content),
        ".fodp" => OfficeTextRenderer.RenderOpenDocumentPresentation(content),
        ".fodg" => OfficeTextRenderer.RenderOpenDocumentDrawing(content),
        _ => [],
    };
}
