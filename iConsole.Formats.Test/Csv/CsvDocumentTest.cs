using iConsole.Formats.Csv;
using iConsole.Csv;

namespace iConsole.Formats.Test.Csv;

[TestClass]
public class CsvDocumentTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ReadsTheHeaderAndTheRows()
    {
        CsvDocument document = new CsvDocumentParser().Parse("name,amount\nApples,3\nPears,5\n");

        CollectionAssert.AreEqual(new[] { "name", "amount" }, document.Headers.ToArray());
        Assert.AreEqual(2, document.Rows.Count);
        CollectionAssert.AreEqual(new[] { "Pears", "5" }, document.Rows[1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void QuotedFieldsMayHoldTheDelimiterLineBreaksAndDoubledQuotes()
    {
        CsvDocument document = new CsvDocumentParser().Parse("a,b\r\n\"x, y\",\"line 1\nline \"\"2\"\"\"\r\n");

        CollectionAssert.AreEqual(new[] { "x, y", "line 1\nline \"2\"" }, document.Rows[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ATabSeparatedFileIsReadWithTheTabWhateverItsCommas()
    {
        CsvDocument document = new CsvDocumentParser().Parse("name\tnote\nA\tone, two, three\n", '\t');

        Assert.AreEqual('\t', document.Delimiter);
        CollectionAssert.AreEqual(new[] { "name", "note" }, document.Headers.ToArray());
        Assert.AreEqual("one, two, three", document.Rows[0][1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheDelimiterIsTheCandidateThatSplitsTheFirstLineMost()
    {
        Assert.AreEqual(';', new CsvDocumentParser().DetectDelimiter("a;b;c\n1;2;3\n"));
        Assert.AreEqual('\t', new CsvDocumentParser().DetectDelimiter("a\tb\n"));
        Assert.AreEqual(',', new CsvDocumentParser().DetectDelimiter("just one column\n"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BlankHeaderAndRepeatedNamesGetUniqueColumnNames()
    {
        CsvDocument document = new CsvDocumentParser().Parse(",name,name\n1,2,3\n");

        CollectionAssert.AreEqual(new[] { "Column1", "name", "name_2" }, document.Headers.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AShortRowIsKeptAndAByteOrderMarkIsIgnored()
    {
        CsvDocument document = new CsvDocumentParser().Parse("﻿a,b\n1\n\n2,3\n");

        CollectionAssert.AreEqual(new[] { "a", "b" }, document.Headers.ToArray());
        Assert.AreEqual(2, document.Rows.Count, "the blank line is skipped");
        Assert.AreEqual(1, document.Rows[0].Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutAHeaderTheColumnsAreNumbered()
    {
        CsvDocument document = new CsvDocumentParser().Parse("1,2\n3,4\n", hasHeader: false);

        CollectionAssert.AreEqual(new[] { "Column1", "Column2" }, document.Headers.ToArray());
        Assert.AreEqual(2, document.Rows.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnEmptyFileHasNoColumns()
    {
        CsvDocument document = new CsvDocumentParser().Parse(string.Empty);

        Assert.AreEqual(0, document.Headers.Count);
        Assert.AreEqual(0, document.Rows.Count);
    }
}
