using System.IO.Compression;
using System.Text;
using iTerminal.Core.Data.Documents;
using iTerminal.Core.Documents;
using iTerminal.Core.Documents.Extractors;

namespace iTerminal.Core.Test.Documents;

/// <summary>
/// The PDFs here are written by hand rather than taken from a fixture folder, so each test states
/// exactly which structural case it is about — a wrong <c>/Length</c>, a compressed stream, an
/// encrypted file — instead of hiding it inside a binary nobody can read in a diff.
/// </summary>
[TestClass]
public class PdfTextExtractorTest
{
    /// <summary>
    /// Assembles a PDF around one content stream. The declared length is deliberately left wrong by
    /// default: producers get it wrong in the wild, and the extractor is supposed to fall back to
    /// searching for <c>endstream</c> rather than truncating.
    /// </summary>
    private static byte[] BuildPdf(string content, string extras = "", bool compress = false, int? declaredLength = null)
    {
        byte[] payload = compress ? Deflate(content) : Encoding.Latin1.GetBytes(content);
        string filter = compress ? " /Filter /FlateDecode" : string.Empty;

        MemoryStream output = new();
        void Append(string text) => output.Write(Encoding.Latin1.GetBytes(text));

        Append("%PDF-1.4\n");
        Append("1 0 obj\n<< /Type /Catalog >>\nendobj\n");
        Append("2 0 obj\n<< /Type /Page >>\nendobj\n");
        Append($"3 0 obj\n<< /Length {declaredLength ?? 9999}{filter} >>\nstream\n");
        output.Write(payload);
        Append("\nendstream\nendobj\n");
        Append(extras);
        Append("trailer\n<< /Root 1 0 R >>\n%%EOF\n");

        return output.ToArray();
    }

    private static byte[] Deflate(string content)
    {
        using MemoryStream output = new();
        using (ZLibStream compressor = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(Encoding.Latin1.GetBytes(content));
        }

        return output.ToArray();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReadsTextDrawnByTj()
    {
        byte[] pdf = BuildPdf("BT /F1 12 Tf 72 720 Td (Hello Document) Tj ET");

        DocumentTextResult result = PdfTextExtractor.ExtractFromBytes(pdf);

        Assert.AreEqual(DocumentExtractionStatus.Extracted, result.Status);
        StringAssert.Contains(result.Text, "Hello Document");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReadsTextFromACompressedStream()
    {
        byte[] pdf = BuildPdf("BT (Compressed contents) Tj ET", compress: true);

        DocumentTextResult result = PdfTextExtractor.ExtractFromBytes(pdf);

        StringAssert.Contains(result.Text, "Compressed contents");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HonoursACorrectDeclaredLength()
    {
        const string content = "BT (Exact length) Tj ET";
        byte[] pdf = BuildPdf(content, declaredLength: content.Length);

        DocumentTextResult result = PdfTextExtractor.ExtractFromBytes(pdf);

        StringAssert.Contains(result.Text, "Exact length");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TreatsATjArrayAdjustmentAsAWordSpace()
    {
        // This is how most producers write a space: two strings and a large negative kern between
        // them. Without the adjustment rule the index would hold "Invoicenumber".
        byte[] pdf = BuildPdf("BT [(Invoice) -400 (number)] TJ ET");

        DocumentTextResult result = PdfTextExtractor.ExtractFromBytes(pdf);

        StringAssert.Contains(result.Text, "Invoice number");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void KeepsBalancedParenthesesInsideALiteralString()
    {
        byte[] pdf = BuildPdf("BT (Total (net) due) Tj ET");

        DocumentTextResult result = PdfTextExtractor.ExtractFromBytes(pdf);

        StringAssert.Contains(result.Text, "Total (net) due");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DecodesOctalAndBackslashEscapes()
    {
        byte[] pdf = BuildPdf(@"BT (A\050B\051 \\ C) Tj ET");

        DocumentTextResult result = PdfTextExtractor.ExtractFromBytes(pdf);

        StringAssert.Contains(result.Text, @"A(B) \ C");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReadsTitleAuthorAndPageCount()
    {
        byte[] pdf = BuildPdf(
            "BT (Body) Tj ET",
            extras: "4 0 obj\n<< /Title (Annual Report) /Author (Tamas Nagy) >>\nendobj\n"
                    + "5 0 obj\n<< /Type /Page >>\nendobj\n");

        DocumentTextResult result = PdfTextExtractor.ExtractFromBytes(pdf);

        Assert.AreEqual("Annual Report", result.Title);
        Assert.AreEqual("Tamas Nagy", result.Author);

        // Two /Type /Page objects and one /Type /Catalog: the catalog must not be counted.
        Assert.AreEqual(2, result.PageCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReadsAHexEncodedUtf16Title()
    {
        // <FEFF…> is how a title with anything outside ASCII is written.
        byte[] titleBytes = Encoding.BigEndianUnicode.GetBytes("Ünnepi");
        string hex = "FEFF" + Convert.ToHexString(titleBytes);

        byte[] pdf = BuildPdf("BT (Body) Tj ET", extras: $"4 0 obj\n<< /Title <{hex}> >>\nendobj\n");

        DocumentTextResult result = PdfTextExtractor.ExtractFromBytes(pdf);

        Assert.AreEqual("Ünnepi", result.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReportsAScannedPageAsEmptyWithAReason()
    {
        // An image XObject and no text operators: exactly what a scanned page looks like.
        byte[] pdf = BuildPdf("\xFF\xD8 binary image payload", extras: string.Empty);
        string raw = Encoding.Latin1.GetString(pdf).Replace("/Length 9999", "/Length 9999 /Subtype /Image");

        DocumentTextResult result = PdfTextExtractor.ExtractFromBytes(Encoding.Latin1.GetBytes(raw));

        Assert.AreEqual(DocumentExtractionStatus.Empty, result.Status);
        StringAssert.Contains(result.Note, "scanned");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RefusesAnEncryptedDocumentWithAReason()
    {
        byte[] pdf = BuildPdf("BT (Body) Tj ET", extras: "trailer\n<< /Encrypt 9 0 R >>\n");

        DocumentTextResult result = PdfTextExtractor.ExtractFromBytes(pdf);

        Assert.AreEqual(DocumentExtractionStatus.Failed, result.Status);
        StringAssert.Contains(result.Note, "encrypted");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RejectsAFileThatIsNotAPdf()
    {
        DocumentTextResult result = PdfTextExtractor.ExtractFromBytes(Encoding.ASCII.GetBytes("just some text"));

        Assert.AreEqual(DocumentExtractionStatus.Failed, result.Status);
        StringAssert.Contains(result.Note, "not a PDF");
    }

    /// <summary>
    /// A subset CID font draws glyph indices, not characters, and what comes back is private-use
    /// code points. Indexing that would fill the index with tokens nothing can ever match — a silent
    /// failure, which is the worst kind — so the extractor throws the result away instead.
    ///
    /// <para>Asserted against the check itself rather than through a synthesised PDF: the code
    /// points that reproduce the problem cannot survive a round trip through a Latin-1 content
    /// stream, which is the only shape a hand-built fixture can take.</para>
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void LooksLikeText_AcceptsProseAndRejectsGlyphSoup()
    {
        Assert.IsTrue(PdfTextExtractor.LooksLikeText(
            "The quick brown fox jumps over the lazy dog, repeatedly and at some length."));

        // U+E000 is in the private use area: not a letter, digit, mark or symbol in any
        // language, which is exactly what an unmapped glyph index decodes to.
        Assert.IsFalse(PdfTextExtractor.LooksLikeText(new string('', 100)));

        // Short strings are too small a sample to judge, and are let through rather than guessed at.
        Assert.IsTrue(PdfTextExtractor.LooksLikeText(""));
    }
}
