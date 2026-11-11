using System.IO.Compression;
using System.Xml.Linq;

namespace iConsole.Xps;

/// <summary>
/// An XPS or OpenXPS package read into its pages. The package's <c>FixedDocumentSequence</c> lists the documents, each
/// <c>FixedDocument</c> lists its pages, and a page carries its size and its <c>Glyphs</c> runs grouped into lines.
///
/// <para>The package is found through the <c>fixedrepresentation</c> relationship and its parts are matched by local
/// name, so an OpenXPS package (which uses its own namespace) reads the same as an XPS one. A package that cannot be
/// read is returned with <see cref="Error"/> set rather than thrown: the viewer shows that text.</para>
/// </summary>
public sealed class XpsDocument
{
    private const string FixedRepresentationSuffix = "/fixedrepresentation";

    private XpsDocument(IReadOnlyList<XpsPage> pages, string? title, string? creator, string? error)
    {
        Pages = pages;
        Title = title;
        Creator = creator;
        Error = error;
    }

    /// <summary>The pages in document order; empty when the package could not be read.</summary>
    public IReadOnlyList<XpsPage> Pages { get; }

    /// <summary>The document title from the package's core properties, when it has one.</summary>
    public string? Title { get; }

    /// <summary>The document creator from the package's core properties, when it has one.</summary>
    public string? Creator { get; }

    /// <summary>The reason the package could not be read, or null when it was read.</summary>
    public string? Error { get; }

    /// <summary>True when the package was read, even when it holds no pages.</summary>
    public bool IsValid => Error is null;

    /// <summary>Reads an XPS or OpenXPS package. Never throws: a failure is returned in <see cref="Error"/>.</summary>
    public static XpsDocument Read(byte[] content)
    {
        try
        {
            using ZipArchive archive = new(new MemoryStream(content), ZipArchiveMode.Read);
            if (FindSequence(archive) is not { } sequencePath)
            {
                return Failed("FixedDocumentSequence.fdseq is missing");
            }

            List<XpsPage> pages = [];
            int number = 0;
            int documentNumber = 0;
            foreach (string documentPath in References(archive, sequencePath, "DocumentReference"))
            {
                documentNumber++;
                int pageInDocument = 0;
                foreach (string pagePath in References(archive, documentPath, "PageContent"))
                {
                    number++;
                    pageInDocument++;
                    if (archive.GetEntry(pagePath) is { } page)
                    {
                        pages.Add(ReadPage(page, number, documentNumber, pageInDocument));
                    }
                }
            }

            (string? title, string? creator) = ReadProperties(archive);
            return new XpsDocument(pages, title, creator, error: null);
        }
        catch (Exception exception)
        {
            return Failed(exception.Message);
        }
    }

    private static XpsDocument Failed(string reason) =>
        new([], title: null, creator: null, $"[Could not read the document: {reason}]");

    /// <summary>
    /// The sequence part: the target of the root <c>fixedrepresentation</c> relationship when the package declares
    /// one, else any <c>.fdseq</c> part, the shortest name first because the sequence lives at the package root.
    /// </summary>
    private static string? FindSequence(ZipArchive archive)
    {
        if (archive.GetEntry("_rels/.rels") is { } relationships)
        {
            using Stream stream = relationships.Open();
            XDocument document = XDocument.Load(stream);
            foreach (XElement relationship in document.Descendants().Where(e => e.Name.LocalName == "Relationship"))
            {
                string type = (string?)relationship.Attribute("Type") ?? string.Empty;
                string target = (string?)relationship.Attribute("Target") ?? string.Empty;
                if (target.Length == 0 || !type.EndsWith(FixedRepresentationSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string path = Uri.UnescapeDataString(target.TrimStart('/'));
                if (archive.GetEntry(path) is not null)
                {
                    return path;
                }
            }
        }

        return archive.Entries
            .Where(entry => entry.FullName.EndsWith(".fdseq", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName.Length)
            .Select(entry => entry.FullName)
            .FirstOrDefault();
    }

    /// <summary>The package paths a document or sequence refers to, in order, resolved against that document's own path.</summary>
    private static IEnumerable<string> References(ZipArchive archive, string partPath, string element)
    {
        ZipArchiveEntry entry = archive.GetEntry(partPath)
            ?? throw new InvalidDataException($"{partPath} is missing");
        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);
        foreach (XElement reference in document.Descendants().Where(e => e.Name.LocalName == element))
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

    private static XpsPage ReadPage(ZipArchiveEntry page, int number, int documentNumber, int pageInDocument)
    {
        using Stream stream = page.Open();
        XDocument document = XDocument.Load(stream);
        XElement? fixedPage = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "FixedPage");
        return new XpsPage(
            number,
            documentNumber,
            pageInDocument,
            ParseCoordinate(fixedPage?.Attribute("Width")?.Value),
            ParseCoordinate(fixedPage?.Attribute("Height")?.Value),
            PageLines(document));
    }

    /// <summary>The page's text: glyph runs sorted into lines by their origin, each line's runs joined with spaces.</summary>
    private static IReadOnlyList<string> PageLines(XDocument document)
    {
        List<(double Y, double X, string Text)> glyphs = [.. document.Descendants()
            .Where(e => e.Name.LocalName == "Glyphs")
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
            if (lines.Count == 0 || Math.Abs(y - lineY) > 2.0)
            {
                lines.Add([]);
                lineY = y;
            }

            lines[^1].Add(text);
        }

        return lines.Select(runs => string.Join(" ", runs)).ToList();
    }

    /// <summary>The package's core properties: the title and creator, when the package has them.</summary>
    private static (string? Title, string? Creator) ReadProperties(ZipArchive archive)
    {
        if (archive.GetEntry("docProps/core.xml") is not { } properties)
        {
            return (null, null);
        }

        try
        {
            using Stream stream = properties.Open();
            XDocument document = XDocument.Load(stream);
            return (
                Property(document, "title"),
                Property(document, "creator"));
        }
        catch (Exception)
        {
            // Properties are a nicety: a malformed block must not lose the pages that were read.
            return (null, null);
        }
    }

    private static string? Property(XDocument document, string localName)
    {
        string? value = document.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static double ParseCoordinate(string? value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number)
            ? number
            : 0;
}

/// <summary>One page of an XPS or OpenXPS package: its place in the sequence, its size in device-independent pixels, and its text.</summary>
public sealed class XpsPage
{
    public XpsPage(int number, int documentNumber, int pageInDocument, double width, double height, IReadOnlyList<string> lines)
    {
        Number = number;
        DocumentNumber = documentNumber;
        PageInDocument = pageInDocument;
        Width = width;
        Height = height;
        Lines = lines;
    }

    /// <summary>The page's number in the whole package, from 1.</summary>
    public int Number { get; }

    /// <summary>The document the page belongs to, from 1; a package can hold more than one.</summary>
    public int DocumentNumber { get; }

    /// <summary>The page's number inside its own document, from 1.</summary>
    public int PageInDocument { get; }

    /// <summary>The page width, zero when the page does not declare one.</summary>
    public double Width { get; }

    /// <summary>The page height, zero when the page does not declare one.</summary>
    public double Height { get; }

    /// <summary>The page's text, one entry per line; empty for a page drawn only with pictures or paths.</summary>
    public IReadOnlyList<string> Lines { get; }
}
