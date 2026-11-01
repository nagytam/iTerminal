using System.IO.Compression;
using System.Text;
using iTerminal.Core.Data.Documents;
using iTerminal.Core.Documents;
using iTerminal.Core.Documents.Extractors;

namespace iTerminal.Core.Test.Documents;

/// <summary>The string readers, predictor, metadata helpers and stream filters of the PDF extractor.</summary>
[TestClass]
public class PdfTextExtractorInternalsTest
{
    private static byte[] Pdf(byte[] streamBytes, string dictionaryExtras)
    {
        MemoryStream output = new();
        void Append(string text) => output.Write(Encoding.Latin1.GetBytes(text));
        Append("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\n2 0 obj\n<< /Type /Page >>\nendobj\n");
        Append($"3 0 obj\n<< /Length 9999 {dictionaryExtras} >>\nstream\n");
        output.Write(streamBytes);
        Append("\nendstream\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");
        return output.ToArray();
    }

    private static string ReadLiteral(string text)
    {
        int index = 0;
        return PdfTextExtractor.ReadLiteralString(text, ref index);
    }

    private static string Text(string content) =>
        PdfTextExtractor.ExtractFromBytes(Pdf(Encoding.Latin1.GetBytes(content), string.Empty)).Text;

    [Timeout(60_000)]
    [TestMethod]
    public void ReadLiteralString_HandlesEveryEscape()
    {
        Assert.AreEqual("a\nb\rc\td\be\ff", ReadLiteral("(a\\nb\\rc\\td\\be\\ff)"));
        Assert.AreEqual("(x)\\", ReadLiteral("(\\(x\\)\\\\)"));
        Assert.AreEqual("ab", ReadLiteral("(a\\\r\nb)"));
        Assert.AreEqual("ab", ReadLiteral("(a\\\rb)"));
        Assert.AreEqual("ab", ReadLiteral("(a\\\nb)"));
        Assert.AreEqual("A", ReadLiteral("(\\101)"));
        Assert.AreEqual("\u0001", ReadLiteral("(\\1)"));
        Assert.AreEqual("q", ReadLiteral("(\\q)"));
        Assert.AreEqual("a(b)c", ReadLiteral("(a(b)c)"));
        Assert.AreEqual("unterminated", ReadLiteral("(unterminated"));
        Assert.AreEqual("end", ReadLiteral("(end\\"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReadHexString_HandlesOddDigitsNoiseAndUtf16()
    {
        int index = 0;
        Assert.AreEqual("A@", PdfTextExtractor.ReadHexString("<4140>", ref index));

        index = 0;
        Assert.AreEqual("A@", PdfTextExtractor.ReadHexString("<41 4>", ref index));

        index = 0;
        Assert.AreEqual("AB", PdfTextExtractor.ReadHexString("<4x1 42>", ref index));

        index = 0;
        Assert.AreEqual("Hé", PdfTextExtractor.ReadHexString("<FEFF004800E9>", ref index));

        index = 0;
        Assert.AreEqual("A", PdfTextExtractor.ReadHexString("<41", ref index));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ContentStream_UnderstandsOperatorsOperandsAndNoise()
    {
        string text = Text("""
            % a comment (not text)
            BT
            /F1 12 Tf
            (first) Tj
            0 -14 Td
            (second) Tj T*
            [(th) -300 (ird) 20 (x)] TJ
            (fourth) '
            1 2 (fifth) "
            <<  /Inline << /Nested 1 >> >> BI
            <666F757274> Tj
            {1 2} [3] -.5 +1.5 ) 1.2.3 foo
            ET
            """);

        StringAssert.Contains(text, "first");
        StringAssert.Contains(text, "second");
        StringAssert.Contains(text, "th ird" + "x");
        StringAssert.Contains(text, "fourth");
        StringAssert.Contains(text, "fifth");
        StringAssert.Contains(text, "four");
        Assert.IsFalse(text.Contains("not text"));
        Assert.IsGreaterThan(3, text.Count(c => c == '\n'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AWordSpaceAdjustment_IsNotDoubledOnAnExistingSpace()
    {
        string text = Text("BT [(a ) -400 (b)] TJ ET");

        StringAssert.Contains(text, "a b");
        Assert.IsFalse(text.Contains("a  b"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UndoPngPredictor_ReversesEveryRowFilter()
    {
        // Two columns, one colour, 8 bits: one byte per pixel. Rows use filters None, Sub, Up, Average, Paeth.
        byte[] encoded =
        [
            0, 10, 20,
            1, 5, 5,
            2, 1, 1,
            3, 4, 4,
            4, 1, 1,
        ];

        byte[] decoded = PdfTextExtractor.UndoPngPredictor(encoded, columns: 2, colors: 1, bitsPerComponent: 8);

        CollectionAssert.AreEqual(new byte[] { 10, 20, 5, 10, 6, 11, 7, 13, 8, 14 }, decoded);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UndoPngPredictor_WithTooLittleData_ReturnsItUnchanged()
    {
        byte[] data = [1, 2];

        Assert.AreSame(data, PdfTextExtractor.UndoPngPredictor(data, columns: 5, colors: 1, bitsPerComponent: 8));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FlateStream_WithPredictor_IsUndoneBeforeReading()
    {
        byte[] raw = Encoding.Latin1.GetBytes("BT (predicted words) Tj ET");
        // One column per byte, "up" filter on a single row is the identity.
        byte[] rowed = [2, .. raw];
        using MemoryStream compressed = new();
        using (ZLibStream z = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(rowed);
        }

        byte[] pdf = Pdf(compressed.ToArray(), $"/Filter /FlateDecode /Predictor 12 /Columns {raw.Length}");

        StringAssert.Contains(PdfTextExtractor.ExtractFromBytes(pdf).Text, "predicted words");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FlateStream_RawDeflateAndLeadingWhitespace_AreStillInflated()
    {
        byte[] raw = Encoding.Latin1.GetBytes("BT (raw deflate) Tj ET");
        using MemoryStream deflated = new();
        using (DeflateStream d = new(deflated, CompressionLevel.Optimal, leaveOpen: true))
        {
            d.Write(raw);
        }

        StringAssert.Contains(
            PdfTextExtractor.ExtractFromBytes(Pdf(deflated.ToArray(), "/Filter /FlateDecode")).Text, "raw deflate");

        using MemoryStream zlib = new();
        using (ZLibStream z = new(zlib, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(Encoding.Latin1.GetBytes("BT (padded) Tj ET"));
        }

        byte[] padded = [.. "  \r\n"u8.ToArray(), .. zlib.ToArray()];
        StringAssert.Contains(PdfTextExtractor.ExtractFromBytes(Pdf(padded, "/Filter /FlateDecode")).Text, "padded");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UndecodableAndUnsupportedStreams_YieldNoText()
    {
        DocumentTextResult garbage = PdfTextExtractor.ExtractFromBytes(Pdf([1, 2, 3, 4, 5, 6], "/Filter /FlateDecode"));
        DocumentTextResult lzw = PdfTextExtractor.ExtractFromBytes(Pdf(Encoding.Latin1.GetBytes("BT (hidden) Tj ET"), "/Filter /LZWDecode"));
        DocumentTextResult whitespaceOnly = PdfTextExtractor.ExtractFromBytes(Pdf("   "u8.ToArray(), "/Filter /FlateDecode"));

        Assert.AreNotEqual(DocumentExtractionStatus.Extracted, garbage.Status);
        Assert.AreNotEqual(DocumentExtractionStatus.Extracted, lzw.Status);
        Assert.AreNotEqual(DocumentExtractionStatus.Extracted, whitespaceOnly.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Extract_MissingFile_FailsWithAReason_AndCancellationPropagates()
    {
        PdfTextExtractor extractor = new();

        DocumentTextResult result = extractor.Extract(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf"));

        Assert.AreEqual(DocumentExtractionStatus.Failed, result.Status);
        Assert.IsFalse(string.IsNullOrEmpty(result.Note));

        using CancellationTokenSource cts = new();
        cts.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => extractor.Extract("ignored.pdf", cts.Token));
        CollectionAssert.AreEqual(new[] { ".pdf" }, extractor.Extensions.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FindInfoValue_SkipsLookalikeNamesAndEmptyValues()
    {
        Assert.AreEqual("Real", PdfTextExtractor.FindInfoValue("/Titlecase (nope) /Title ( ) /Title (Real)", "Title"));
        Assert.AreEqual("Hex", PdfTextExtractor.FindInfoValue("/Author <486578>", "Author"));
        Assert.AreEqual(string.Empty, PdfTextExtractor.FindInfoValue("/Author << /X 1 >>", "Author"));
        Assert.AreEqual(string.Empty, PdfTextExtractor.FindInfoValue("nothing", "Title"));
        Assert.AreEqual(string.Empty, PdfTextExtractor.FindInfoValue("/Title 12 0 R", "Title"));
        Assert.AreEqual(512, PdfTextExtractor.FindInfoValue("/Title (" + new string('x', 600) + ")", "Title").Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CountPages_CountsPageObjectsNotTreeNodes()
    {
        Assert.AreEqual(2, PdfTextExtractor.CountPages("/Type /Page /Type/Page /Type /Pages /Type 5 /Type /Catalog"));
        Assert.AreEqual(0, PdfTextExtractor.CountPages("/Type"));
    }
}
