using System.Text;
using iTerminal.Core.Data.Documents;
using iTerminal.Core.Documents;
using iTerminal.Core.Documents.Extractors;

namespace iTerminal.Core.Test.Documents;

[TestClass]
public class PlainAndMarkupExtractorTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "DocExtractorTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
        }
    }

    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [TestMethod]
    public void PlainText_ReadsUtf8WithoutABom()
    {
        // No BOM, but the bytes are valid UTF-8: the sniff has to notice, or the accented characters
        // come back as Latin-1 mojibake and stop matching a query that spells them correctly.
        string path = Write("notes.txt", Encoding.UTF8.GetBytes("Árvíztűrő tükörfúrógép"));

        DocumentTextResult result = new PlainTextExtractor().Extract(path);

        Assert.AreEqual(DocumentExtractionStatus.Extracted, result.Status);
        Assert.AreEqual("Árvíztűrő tükörfúrógép", result.Text);
    }

    [TestMethod]
    public void PlainText_FallsBackToLatin1ForBytesThatAreNotUtf8()
    {
        // 0xE9 alone is 'é' in Latin-1 and an invalid UTF-8 lead byte. Decoding it as UTF-8 would
        // put U+FFFD in the index — a token no query can ever match.
        string path = Write("legacy.txt", [(byte)'c', (byte)'a', (byte)'f', 0xE9]);

        DocumentTextResult result = new PlainTextExtractor().Extract(path);

        Assert.AreEqual("café", result.Text);
    }

    [TestMethod]
    public void PlainText_ReportsAnEmptyFileAsEmptyRatherThanExtracted()
    {
        string path = Write("empty.txt", []);

        DocumentTextResult result = new PlainTextExtractor().Extract(path);

        Assert.AreEqual(DocumentExtractionStatus.Empty, result.Status);
    }

    [TestMethod]
    public void PlainText_TruncatesAtTheTextLimitAndSaysSo()
    {
        string path = Write("huge.txt", Encoding.ASCII.GetBytes(new string('a', DocumentTextLimits.MaxTextLength + 5_000)));

        DocumentTextResult result = new PlainTextExtractor().Extract(path);

        Assert.AreEqual(DocumentExtractionStatus.Extracted, result.Status);
        Assert.IsTrue(result.Text.Length <= DocumentTextLimits.MaxTextLength + DocumentTextLimits.TruncationMarker.Length);
        Assert.IsTrue(result.Text.EndsWith("[truncated]", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Markup_DropsTagsAndScriptBodiesAndKeepsTheTitle()
    {
        const string html = """
            <html><head><title>Quarterly Report</title>
            <style>body { color: red; }</style></head>
            <body><h1>Revenue</h1><p>Sales rose by <b>12</b> percent.</p>
            <script>var secret = "do not index me";</script></body></html>
            """;

        string path = Write("report.html", Encoding.UTF8.GetBytes(html));

        DocumentTextResult result = new MarkupTextExtractor().Extract(path);

        Assert.AreEqual("Quarterly Report", result.Title);
        StringAssert.Contains(result.Text, "Revenue");
        StringAssert.Contains(result.Text, "Sales rose by 12 percent.");
        Assert.IsFalse(result.Text.Contains("do not index me", StringComparison.Ordinal));
        Assert.IsFalse(result.Text.Contains("color: red", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Markup_DecodesEntities()
    {
        string path = Write("entities.html", Encoding.UTF8.GetBytes("<p>Ben &amp; Jerry&rsquo;s</p>"));

        DocumentTextResult result = new MarkupTextExtractor().Extract(path);

        StringAssert.Contains(result.Text, "Ben & Jerry");
    }

    [TestMethod]
    public void Markup_DoesNotMistakeAnElementWhoseNameMerelyStartsWithScript()
    {
        // "<scripted>" is not "<script>"; treating it as one would silently swallow the paragraph.
        string path = Write("prefix.html", Encoding.UTF8.GetBytes("<p>before</p><scripted>middle</scripted><p>after</p>"));

        DocumentTextResult result = new MarkupTextExtractor().Extract(path);

        StringAssert.Contains(result.Text, "middle");
        StringAssert.Contains(result.Text, "after");
    }

    [TestMethod]
    public void Factory_MapsExtensionsToKindsAndExtractors()
    {
        DocumentTextExtractorFactory factory = new();

        Assert.IsInstanceOfType<PdfTextExtractor>(factory.GetExtractor("a.pdf"));
        Assert.IsInstanceOfType<OfficeTextExtractor>(factory.GetExtractor("a.docx"));
        Assert.IsInstanceOfType<ImageTextExtractor>(factory.GetExtractor("a.PNG"));
        Assert.IsInstanceOfType<PlainTextExtractor>(factory.GetExtractor("a.md"));
        Assert.IsNull(factory.GetExtractor("a.exe"));

        Assert.AreEqual(DocumentKind.Pdf, DocumentTextExtractorFactory.GetKind("a.pdf"));
        Assert.AreEqual(DocumentKind.Image, DocumentTextExtractorFactory.GetKind("a.JPEG"));
        Assert.AreEqual(DocumentKind.Office, DocumentTextExtractorFactory.GetKind("a.xlsx"));
        Assert.AreEqual(DocumentKind.Text, DocumentTextExtractorFactory.GetKind("a.txt"));
        Assert.AreEqual(DocumentKind.Unknown, DocumentTextExtractorFactory.GetKind("a.exe"));
        Assert.AreEqual(DocumentKind.Unknown, DocumentTextExtractorFactory.GetKind("README"));
    }

    [TestMethod]
    public void Normalize_CollapsesLineEndingsAndDropsControlCharacters()
    {
        string normalized = DocumentTextLimits.Normalize("a\r\nb\r\n\r\n\r\n\r\ncd\te");

        Assert.AreEqual("a\nb\n\ncd e", normalized);
    }

    [TestMethod]
    public void CountWords_CountsWhitespaceSeparatedRuns()
    {
        Assert.AreEqual(0, DocumentTextLimits.CountWords("   "));
        Assert.AreEqual(3, DocumentTextLimits.CountWords(" one  two\nthree "));
    }
}
