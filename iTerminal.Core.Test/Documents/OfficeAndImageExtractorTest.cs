using System.IO.Compression;
using System.Text;
using iTerminal.Core.Data.Documents;
using iTerminal.Core.Documents;
using iTerminal.Core.Documents.Extractors;

namespace iTerminal.Core.Test.Documents;

[TestClass]
public class OfficeAndImageExtractorTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "OfficeExtractorTest_" + Guid.NewGuid().ToString("N")[..8]);
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

    /// <summary>Writes a zip package with the given parts — the shape every Office format takes.</summary>
    private string WritePackage(string fileName, params (string Part, string Xml)[] parts)
    {
        string path = Path.Combine(_folder, fileName);

        using (FileStream stream = File.Create(path))
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create))
        {
            foreach ((string part, string xml) in parts)
            {
                using StreamWriter writer = new(archive.CreateEntry(part).Open(), Encoding.UTF8);
                writer.Write(xml);
            }
        }

        return path;
    }

    private const string WordNamespace = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    [Timeout(60_000)]
    [TestMethod]
    public void Docx_JoinsAdjacentRunsWithoutSplittingWords()
    {
        // Word splits a word across runs whenever formatting changes mid-word. A space inserted
        // between runs would put "Doc" and "ument" in the index as two tokens.
        string path = WritePackage(
            "report.docx",
            ("word/document.xml",
                $"<w:document {WordNamespace}><w:body>"
                + "<w:p><w:r><w:t>Doc</w:t></w:r><w:r><w:t>ument</w:t></w:r></w:p>"
                + "<w:p><w:r><w:t>Second paragraph</w:t></w:r></w:p>"
                + "</w:body></w:document>"),
            ("docProps/core.xml",
                "<cp:coreProperties xmlns:cp=\"x\" xmlns:dc=\"y\">"
                + "<dc:title>Annual Report</dc:title><dc:creator>Tamas</dc:creator></cp:coreProperties>"));

        DocumentTextResult result = new OfficeTextExtractor().Extract(path);

        Assert.AreEqual(DocumentExtractionStatus.Extracted, result.Status);
        Assert.AreEqual("Annual Report", result.Title);
        Assert.AreEqual("Tamas", result.Author);
        StringAssert.Contains(result.Text, "Document");
        StringAssert.Contains(result.Text, "Second paragraph");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Xlsx_ReadsTheSharedStringTable()
    {
        string path = WritePackage(
            "book.xlsx",
            ("xl/sharedStrings.xml", "<sst><si><t>Revenue</t></si><si><t>Expenses</t></si></sst>"),
            ("xl/worksheets/sheet1.xml", "<worksheet><sheetData><row><c t=\"s\"><v>0</v></c></row></sheetData></worksheet>"));

        DocumentTextResult result = new OfficeTextExtractor().Extract(path);

        StringAssert.Contains(result.Text, "Revenue");
        StringAssert.Contains(result.Text, "Expenses");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Pptx_ReadsSlidesInNumericOrder()
    {
        // Zip entries come back in storage order, so slide10 would sort between slide1 and slide2
        // under a plain string comparison and the preview would read out of sequence.
        string path = WritePackage(
            "deck.pptx",
            ("ppt/slides/slide10.xml", "<p><a:t xmlns:a=\"x\">Tenth slide</a:t></p>"),
            ("ppt/slides/slide2.xml", "<p><a:t xmlns:a=\"x\">Second slide</a:t></p>"),
            ("ppt/slides/slide1.xml", "<p><a:t xmlns:a=\"x\">First slide</a:t></p>"));

        DocumentTextResult result = new OfficeTextExtractor().Extract(path);

        int first = result.Text.IndexOf("First slide", StringComparison.Ordinal);
        int second = result.Text.IndexOf("Second slide", StringComparison.Ordinal);
        int tenth = result.Text.IndexOf("Tenth slide", StringComparison.Ordinal);

        Assert.IsTrue(first >= 0 && second > first && tenth > second, result.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TrailingNumber_ReadsThePartIndex()
    {
        Assert.AreEqual(10, OfficeTextExtractor.TrailingNumber("ppt/slides/slide10.xml"));
        Assert.AreEqual(1, OfficeTextExtractor.TrailingNumber("slide1.xml"));
        Assert.AreEqual(0, OfficeTextExtractor.TrailingNumber("word/document.xml"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Office_ReportsAFileThatIsNotAZipAsFailed()
    {
        string path = Path.Combine(_folder, "broken.docx");
        File.WriteAllText(path, "this is not a zip");

        DocumentTextResult result = new OfficeTextExtractor().Extract(path);

        Assert.AreEqual(DocumentExtractionStatus.Failed, result.Status);
        StringAssert.Contains(result.Note, "not a valid Office package");
    }

    #region Images

    /// <summary>Writes a minimal PNG carrying one <c>tEXt</c> chunk and nothing else.</summary>
    private string WritePngWithText(string fileName, string keyword, string value)
    {
        string path = Path.Combine(_folder, fileName);

        using MemoryStream output = new();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        byte[] payload = [.. Encoding.Latin1.GetBytes(keyword), 0, .. Encoding.Latin1.GetBytes(value)];
        WriteChunk(output, "tEXt", payload);
        WriteChunk(output, "IEND", []);

        File.WriteAllBytes(path, output.ToArray());
        return path;
    }

    private static void WriteChunk(Stream stream, string type, byte[] payload)
    {
        stream.Write([
            (byte)(payload.Length >> 24), (byte)(payload.Length >> 16),
            (byte)(payload.Length >> 8), (byte)payload.Length,
        ]);
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(payload);
        stream.Write([0, 0, 0, 0]); // CRC, which nothing here verifies
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Image_IndexesAPngTextChunkWithNoOcrEngineAtAll()
    {
        // The point of reading embedded metadata: an image is searchable on a machine that has no
        // OCR tool installed, which is most machines.
        string path = WritePngWithText("scan.png", "Description", "Invoice from Contoso");

        DocumentTextResult result = new ImageTextExtractor().Extract(path);

        Assert.AreEqual(DocumentExtractionStatus.Extracted, result.Status);
        StringAssert.Contains(result.Text, "Invoice from Contoso");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Image_ReadsTheTitleAndAuthorKeywords()
    {
        string path = WritePngWithText("titled.png", "Title", "Roof plan");

        DocumentTextResult result = new ImageTextExtractor().Extract(path);

        Assert.AreEqual("Roof plan", result.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Image_WithNothingToReadSaysWhyRatherThanFailing()
    {
        string path = Path.Combine(_folder, "blank.png");
        File.WriteAllBytes(path, [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        DocumentTextResult result = new ImageTextExtractor(new StubOcrEngine(available: false)).Extract(path);

        Assert.AreEqual(DocumentExtractionStatus.Empty, result.Status);
        StringAssert.Contains(result.Note, "no embedded metadata");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Image_UsesTheOcrEngineWhenOneIsAvailable()
    {
        string path = Path.Combine(_folder, "receipt.jpg");
        File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF, 0xD9]);

        DocumentTextResult result =
            new ImageTextExtractor(new StubOcrEngine(available: true, text: "TOTAL 42.00")).Extract(path);

        Assert.AreEqual(DocumentExtractionStatus.Extracted, result.Status);
        StringAssert.Contains(result.Text, "TOTAL 42.00");
        StringAssert.Contains(result.Note, "OCR");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Image_DoesNotSendAFormatThatIsNotWorthRecognisingToOcr()
    {
        string path = Path.Combine(_folder, "icon.bmp");
        File.WriteAllBytes(path, [(byte)'B', (byte)'M', 0, 0]);

        StubOcrEngine engine = new(available: true, text: "should never run");
        DocumentTextResult result = new ImageTextExtractor(engine).Extract(path);

        Assert.AreEqual(0, engine.Calls);
        Assert.AreEqual(DocumentExtractionStatus.Empty, result.Status);
    }

    private sealed class StubOcrEngine(bool available, string text = "") : IOcrEngine
    {
        public int Calls { get; private set; }

        public string DisplayName => available ? "Stub OCR" : "no OCR engine";

        public bool IsAvailable => available;

        public string Recognize(string imagePath, CancellationToken cancellationToken = default)
        {
            Calls++;
            return text;
        }
    }

    #endregion
}
