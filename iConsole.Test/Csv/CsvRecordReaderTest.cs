using iConsole.Csv;

namespace iConsole.Test.Csv;

/// <summary>The record reader's fast paths (a field copied straight from the text) and the cases that still go through the builder.</summary>
[TestClass]
public class CsvRecordReaderTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void QuotedFieldsEndedByDelimiterAndCrLfAreReadWhole()
    {
        CsvDocument document = CsvDocument.Parse("\"a\",\"b\"\r\n\"c\",d\r\n");

        CollectionAssert.AreEqual(new[] { "a", "b" }, document.Headers.ToArray());
        CollectionAssert.AreEqual(new[] { "c", "d" }, document.Rows[0]);
        Assert.AreEqual(1, document.Rows.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ADoubledQuoteInsideAQuotedFieldIsOneQuote()
    {
        CsvDocument document = CsvDocument.Parse("h1,h2\n\"x \"\"y\"\"\",z\n");

        CollectionAssert.AreEqual(new[] { "x \"y\"", "z" }, document.Rows[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextAfterAClosingQuoteJoinsTheField()
    {
        CsvDocument document = CsvDocument.Parse("h1,h2\n\"ab\"cd,e\n");

        CollectionAssert.AreEqual(new[] { "abcd", "e" }, document.Rows[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AQuoteInsideAnUnquotedFieldIsALiteral()
    {
        CsvDocument document = CsvDocument.Parse("h\na\"b,c\n");

        CollectionAssert.AreEqual(new[] { "a\"b", "c" }, document.Rows[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALastRecordWithoutALineEndIsKept()
    {
        CsvDocument document = CsvDocument.Parse("h1,h2\n1,\n2");

        Assert.AreEqual(2, document.Rows.Count);
        CollectionAssert.AreEqual(new[] { "1", "" }, document.Rows[0]);
        CollectionAssert.AreEqual(new[] { "2" }, document.Rows[1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ACarriageReturnAloneEndsARecord()
    {
        CsvDocument document = CsvDocument.Parse("a,b\rc,d\r");

        Assert.AreEqual(1, document.Rows.Count);
        CollectionAssert.AreEqual(new[] { "c", "d" }, document.Rows[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AQuotedLineBreakStaysInsideItsField()
    {
        CsvDocument document = CsvDocument.Parse("h1,h2\n\"line one\nline two\",x\n");

        CollectionAssert.AreEqual(new[] { "line one\nline two", "x" }, document.Rows[0]);
    }
}
