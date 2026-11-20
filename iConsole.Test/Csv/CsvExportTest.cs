using iConsole.Csv;

namespace iConsole.Test.Csv;

[TestClass]
public class CsvExportTest
{
    [TestMethod]
    public void WritesTheHeaderAndEachRowWithCrLfEndings()
    {
        string text = CsvExport.Build(new[] { "name", "amount" }, new[] { new[] { "A", "1" }, new[] { "B", "2" } });

        Assert.AreEqual("name,amount\r\nA,1\r\nB,2\r\n", text);
    }

    [TestMethod]
    public void QuotesAValueWithACommaAQuoteOrALineBreakAndDoublesItsQuotes()
    {
        string text = CsvExport.Build(new[] { "note" }, new[] { new[] { "one, two" }, new[] { "say \"hi\"" }, new[] { "line1\nline2" } });

        Assert.AreEqual("note\r\n\"one, two\"\r\n\"say \"\"hi\"\"\"\r\n\"line1\nline2\"\r\n", text);
    }

    [TestMethod]
    public void LeavesAValueWithoutSpecialCharactersUnquoted()
    {
        string text = CsvExport.Build(new[] { "name" }, new[] { new[] { "plain value" } });

        Assert.AreEqual("name\r\nplain value\r\n", text);
    }

    [TestMethod]
    public void PadsAShortRowWithEmptyValues()
    {
        string text = CsvExport.Build(new[] { "a", "b", "c" }, new[] { new[] { "1" } });

        Assert.AreEqual("a,b,c\r\n1,,\r\n", text);
    }

    [TestMethod]
    public void WritesOnlyTheHeaderWhenThereAreNoRows()
    {
        string text = CsvExport.Build(new[] { "a", "b" }, Array.Empty<string[]>());

        Assert.AreEqual("a,b\r\n", text);
    }
}
