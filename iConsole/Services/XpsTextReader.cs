using System.IO.Compression;
using System.Xml.Linq;

namespace iConsole.Services;

/// <summary>
/// Reads an XPS or OpenXPS document (.xps, .oxps) into lines, page by page in document order. The package's
/// <c>FixedDocumentSequence</c> lists the documents, each <c>FixedDocument</c> lists its pages, and a page's text is
/// its <c>Glyphs</c> runs, grouped into lines by their vertical position and read left to right.
/// </summary>
public static class XpsTextReader
{
    private const string FixedDocumentSequenceName = "FixedDocumentSequence.fdseq";
    private static readonly XNamespace Xps = "http://schemas.microsoft.com/xps/2005/06";
    private const double LineTolerance = 2.0;

    public static IReadOnlyList<string> Read(byte[] content)
    {
        List<string> lines = [];
        try
        {
            using ZipArchive archive = new(new MemoryStream(content), ZipArchiveMode.Read);
            ZipArchiveEntry? sequence = archive.Entries
                .Where(e => e.FullName.EndsWith(FixedDocumentSequenceName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName.Length)
                .FirstOrDefault();
            if (sequence is null)
            {
                lines.Add("[Could not read the document: FixedDocumentSequence.fdseq is missing]");
                return lines;
            }

            int number = 0;
            foreach (string documentPath in References(archive, sequence.FullName, "DocumentReference"))
            {
                foreach (string pagePath in References(archive, documentPath, "PageContent"))
                {
                    number++;
                    ZipArchiveEntry? page = archive.GetEntry(pagePath);
                    if (page is null)
                    {
                        continue;
                    }

                    OfficeTextRenderer.AppendHeading(lines, $"Page {number}");
                    lines.AddRange(PageLines(page));
                    lines.Add(string.Empty);
                }
            }
        }
        catch (Exception exception)
        {
            lines.Add($"[Could not read the document: {exception.Message}]");
        }

        return OfficeTextRenderer.Trim(lines);
    }

    /// <summary>The package paths a document or sequence refers to, in order, resolved against that document's own path.</summary>
    private static IEnumerable<string> References(ZipArchive archive, string partPath, string element)
    {
        ZipArchiveEntry entry = archive.GetEntry(partPath)
            ?? throw new InvalidDataException($"{partPath} is missing");
        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);
        foreach (XElement reference in document.Descendants(Xps + element))
        {
            if (reference.Attribute("Source")?.Value is { Length: > 0 } source)
            {
                yield return Resolve(partPath, source);
            }
        }
    }

    /// <summary>A package path from a reference: relative to the referring part, or absolute from the package root.</summary>
    private static string Resolve(string basePath, string reference)
    {
        Uri target = new(new Uri("http://xps/" + basePath), reference);
        return Uri.UnescapeDataString(target.AbsolutePath.TrimStart('/'));
    }

    /// <summary>The page's text: glyph runs sorted into lines by their origin, each line's runs joined with spaces.</summary>
    private static IEnumerable<string> PageLines(ZipArchiveEntry page)
    {
        using Stream stream = page.Open();
        XDocument document = XDocument.Load(stream);
        List<(double Y, double X, string Text)> glyphs = [.. document.Descendants(Xps + "Glyphs")
            .Select(g => (
                Y: ParseCoordinate(g.Attribute("OriginY")?.Value),
                X: ParseCoordinate(g.Attribute("OriginX")?.Value),
                Text: (string?)g.Attribute("UnicodeString") ?? string.Empty))
            .Where(g => g.Text.Length > 0)
            .OrderBy(g => g.Y)
            .ThenBy(g => g.X)];

        List<List<string>> lines = [];
        double lineY = double.NaN;
        foreach ((double y, double _, string text) in glyphs)
        {
            if (lines.Count == 0 || Math.Abs(y - lineY) > LineTolerance)
            {
                lines.Add([]);
                lineY = y;
            }

            lines[^1].Add(text);
        }

        return lines.Select(runs => string.Join(" ", runs));
    }

    private static double ParseCoordinate(string? value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number)
            ? number
            : 0;
}
