using System.IO.Compression;
using System.Text;
using System.Xml;
using iTerminal.Core.Data.Documents;

namespace iTerminal.Core.Documents.Extractors;

/// <summary>
/// Office Open XML (.docx/.xlsx/.pptx) and OpenDocument (.odt/.ods/.odp). Both families are a zip
/// of XML parts, which is what makes them readable here with nothing but the framework — no Office
/// interop, no third-party package, and no process to shell out to.
///
/// <para>Which parts hold the text differs per format and per family, so the extractor is a table
/// of "these parts, these elements" and one XML reader that walks whatever it is given. The reader
/// is deliberately namespace-blind and matches on local names: <c>w:t</c>, <c>a:t</c> and
/// <c>text:p</c> all mean "text is in here", and the alternative is carrying three sets of
/// namespace URIs that a future format revision would silently break.</para>
/// </summary>
public sealed class OfficeTextExtractor : IDocumentTextExtractor
{
    /// <summary>
    /// A guard against zip bombs and against a single enormous embedded part. Uncompressed bytes
    /// read across all parts of one package, not the packaged size — that is the number an attacker
    /// (or a badly generated spreadsheet) controls.
    /// </summary>
    private const long MaxUncompressedBytes = 256L * 1024 * 1024;

    public IReadOnlyCollection<string> Extensions { get; } =
    [
        ".docx", ".docm", ".xlsx", ".xlsm", ".pptx", ".pptm", ".odt", ".ods", ".odp",
    ];

    public DocumentTextResult Extract(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(path);

            long uncompressed = archive.Entries.Sum(entry => entry.Length);
            if (uncompressed > MaxUncompressedBytes)
            {
                return DocumentTextResult.None(
                    DocumentExtractionStatus.TooLarge,
                    $"package expands to {uncompressed / (1024 * 1024)} MB");
            }

            (string Title, string Author) properties = ReadProperties(archive);
            StringBuilder builder = new();

            foreach (ZipArchiveEntry entry in OrderParts(archive, System.IO.Path.GetExtension(path)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendPartText(entry, builder);
            }

            string text = DocumentTextLimits.Normalize(builder.ToString());

            return text.Length == 0
                ? DocumentTextResult.None(DocumentExtractionStatus.Empty) with
                {
                    Title = properties.Title,
                    Author = properties.Author,
                }
                : new DocumentTextResult(text, DocumentExtractionStatus.Extracted)
                {
                    Title = properties.Title,
                    Author = properties.Author,
                };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            // The extension said Office, the bytes said otherwise. A common enough thing in a real
            // folder that it deserves a readable note rather than a zip library's own wording.
            return DocumentTextResult.None(DocumentExtractionStatus.Failed, "not a valid Office package");
        }
        catch (Exception exception)
        {
            return DocumentTextResult.None(DocumentExtractionStatus.Failed, exception.Message);
        }
    }

    /// <summary>
    /// The parts worth reading, in reading order.
    ///
    /// <para>Order is the whole reason this is not just a filter. Slides and worksheets are numbered
    /// in their names, and zip entries come back in whatever order the writer happened to store
    /// them, so a deck read unordered gives a preview whose paragraphs are shuffled. They are sorted
    /// numerically rather than lexically for the obvious reason: <c>slide10.xml</c> comes after
    /// <c>slide9.xml</c>, not between <c>slide1.xml</c> and <c>slide2.xml</c>. A spreadsheet's
    /// shared-string table is forced first because every cell that references it is read afterwards
    /// anyway, and reading the strings once beats reading them per sheet.</para>
    /// </summary>
    private static IEnumerable<ZipArchiveEntry> OrderParts(ZipArchive archive, string extension) =>
        archive.Entries
            .Where(entry => IsTextPart(entry.FullName, extension))
            .OrderBy(entry => entry.FullName.Contains("sharedStrings", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(entry => TrailingNumber(entry.FullName))
            .ThenBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase);

    private static bool IsTextPart(string partName, string extension)
    {
        partName = partName.Replace('\\', '/');

        return extension.ToLowerInvariant() switch
        {
            ".docx" or ".docm" =>
                partName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase)
                || partName.StartsWith("word/header", StringComparison.OrdinalIgnoreCase)
                || partName.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase)
                || partName.Equals("word/footnotes.xml", StringComparison.OrdinalIgnoreCase)
                || partName.Equals("word/endnotes.xml", StringComparison.OrdinalIgnoreCase),

            ".xlsx" or ".xlsm" =>
                partName.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase)
                || partName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase),

            ".pptx" or ".pptm" =>
                partName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                || partName.StartsWith("ppt/notesSlides/notesSlide", StringComparison.OrdinalIgnoreCase),

            // OpenDocument keeps the body in one part regardless of which application wrote it.
            _ => partName.Equals("content.xml", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// The number at the end of a part's file name (<c>slide12.xml</c> → 12), or 0 when it has none.
    /// </summary>
    internal static int TrailingNumber(string partName)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(partName);
        int end = name.Length;
        while (end > 0 && char.IsAsciiDigit(name[end - 1]))
        {
            end--;
        }

        return end == name.Length ? 0 : int.TryParse(name[end..], out int value) ? value : 0;
    }

    /// <summary>
    /// Elements whose character content is document text, by local name.
    ///
    /// <para><c>t</c> covers Word runs (<c>w:t</c>), PowerPoint runs (<c>a:t</c>) and both shapes an
    /// Excel string can take. <c>p</c> and <c>h</c> are OpenDocument paragraphs and headings, whose
    /// text is not wrapped in anything smaller.</para>
    /// </summary>
    private static readonly HashSet<string> TextElements =
        new(StringComparer.Ordinal) { "t", "p", "h" };

    /// <summary>
    /// Elements after which a line break belongs.
    ///
    /// <para>These are what give the preview its shape, and they are also the <em>only</em> thing
    /// that separates one piece of text from the next — see <see cref="AppendPartText"/> for why a
    /// run boundary must not. That is why the list has to include the containers as well as the
    /// paragraphs: a shared string (<c>si</c>) and a cell (<c>c</c>) are separate values, and
    /// without them a spreadsheet arrives as one unbroken word made of every cell it holds.</para>
    /// </summary>
    private static readonly HashSet<string> BreakElements =
        new(StringComparer.Ordinal)
        {
            "p", "h", "br", "tab", "cr", "row", "tr", "table-row", "si", "c", "tc",
        };

    private static void AppendPartText(ZipArchiveEntry entry, StringBuilder builder)
    {
        try
        {
            using Stream stream = entry.Open();
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreWhitespace = false,
                XmlResolver = null,
                CloseInput = false,
            });

            // Depth of the innermost open text element, or 0 when there is none. A counter rather
            // than a flag because OpenDocument nests spans inside paragraphs, and a flag cleared by
            // the first closing tag would lose the rest of the paragraph.
            int textDepth = 0;

            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        if (TextElements.Contains(reader.LocalName) && !reader.IsEmptyElement)
                        {
                            textDepth++;
                        }
                        else if (reader.IsEmptyElement && BreakElements.Contains(reader.LocalName))
                        {
                            builder.Append('\n');
                        }

                        break;

                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.SignificantWhitespace:
                        if (textDepth > 0)
                        {
                            builder.Append(reader.Value);
                        }

                        break;

                    case XmlNodeType.EndElement:
                        if (TextElements.Contains(reader.LocalName) && textDepth > 0)
                        {
                            textDepth--;
                        }

                        // Nothing is appended for the end of a text element on purpose. Word splits
                        // a word across runs whenever formatting changes mid-word, so <w:t>Doc</w:t>
                        // <w:t>ument</w:t> is one word — and a separator here would put "Doc" and
                        // "ument" in the index as two tokens that no query for "document" can match.
                        // Separation comes from the break elements instead.
                        if (BreakElements.Contains(reader.LocalName))
                        {
                            builder.Append('\n');
                        }

                        break;
                }
            }
        }
        catch (XmlException)
        {
            // One malformed part costs that part. The rest of the package is still worth reading —
            // a corrupt header should not make a whole document unsearchable.
        }
    }

    /// <summary>
    /// Title and author from the package's own metadata: <c>docProps/core.xml</c> for Office Open
    /// XML, <c>meta.xml</c> for OpenDocument. Both use Dublin Core element names, so one reader
    /// covers them.
    /// </summary>
    private static (string Title, string Author) ReadProperties(ZipArchive archive)
    {
        ZipArchiveEntry? entry =
            archive.GetEntry("docProps/core.xml") ?? archive.GetEntry("meta.xml");

        if (entry is null)
        {
            return (string.Empty, string.Empty);
        }

        string title = string.Empty;
        string author = string.Empty;

        try
        {
            using Stream stream = entry.Open();
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                XmlResolver = null,
                CloseInput = false,
            });

            // Not a `while (reader.Read())` loop: ReadElementContentAsString already leaves the
            // reader on the node *after* the element it consumed, so an unconditional Read() at the
            // top of each pass would step over whatever follows a field that was just read — which
            // in core.xml is the very next field.
            while (!reader.EOF)
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    reader.Read();
                    continue;
                }

                switch (reader.LocalName)
                {
                    case "title" when title.Length == 0:
                        title = reader.ReadElementContentAsString().Trim();
                        break;

                    case "creator" when author.Length == 0:
                    case "initial-creator" when author.Length == 0:
                        author = reader.ReadElementContentAsString().Trim();
                        break;

                    default:
                        reader.Read();
                        break;
                }
            }
        }
        catch (XmlException)
        {
            // Metadata is a nicety; the body text is the point.
        }

        return (title, author);
    }
}
