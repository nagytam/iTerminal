using System.Text.Json;
using System.Xml.Linq;
using iConsole.Csv;

namespace iConsole.Test.Csv;

[TestClass]
public class TableExportTest
{
    [TestMethod]
    public void CsvIsTheUnchangedCsvWriter()
    {
        string[] headers = ["name", "amount"];
        string[][] rows = [["A", "1"], ["B", "2"]];

        Assert.AreEqual(CsvExport.Build(headers, rows), TableExport.Build(TableExportFormat.Csv, headers, rows));
    }

    [TestMethod]
    public void JsonIsAnArrayOfObjectsKeyedByTheColumnNames()
    {
        string text = TableExport.Build(TableExportFormat.Json, ["name", "amount"], [["A", "1"], ["B", "2"]]);

        using JsonDocument document = JsonDocument.Parse(text);
        Assert.AreEqual(2, document.RootElement.GetArrayLength());
        Assert.AreEqual("A", document.RootElement[0].GetProperty("name").GetString());
        Assert.AreEqual("1", document.RootElement[0].GetProperty("amount").GetString());
        Assert.AreEqual("B", document.RootElement[1].GetProperty("name").GetString());
    }

    [TestMethod]
    public void JsonEscapesQuotesBackslashesAndControlCharactersSoItParsesBackToTheSameText()
    {
        const string value = "say \"hi\" \\ tab\there \u0001 line\nend";

        string text = TableExport.Build(TableExportFormat.Json, ["note"], [[value]]);

        using JsonDocument document = JsonDocument.Parse(text);
        Assert.AreEqual(value, document.RootElement[0].GetProperty("note").GetString());
    }

    [TestMethod]
    public void JsonPadsAShortRowWithEmptyValues()
    {
        string text = TableExport.Build(TableExportFormat.Json, ["name", "amount"], [["A"]]);

        using JsonDocument document = JsonDocument.Parse(text);
        Assert.AreEqual(string.Empty, document.RootElement[0].GetProperty("amount").GetString());
    }

    [TestMethod]
    public void JsonWithNoRowsIsAnEmptyArray()
    {
        string text = TableExport.Build(TableExportFormat.Json, ["name"], []);

        Assert.AreEqual("[]", text.Trim());
    }

    [TestMethod]
    public void XmlHasOneCellPerColumnWithItsNameInAnAttribute()
    {
        string text = TableExport.Build(TableExportFormat.Xml, ["name", "amount"], [["A", "1"]]);

        XDocument document = XDocument.Parse(text);
        XElement row = document.Root!.Elements("row").Single();
        var cells = row.Elements("c").ToList();
        Assert.AreEqual("table", document.Root.Name.LocalName);
        Assert.AreEqual(2, cells.Count);
        Assert.AreEqual("name", cells[0].Attribute("name")?.Value);
        Assert.AreEqual("A", cells[0].Value);
        Assert.AreEqual("amount", cells[1].Attribute("name")?.Value);
        Assert.AreEqual("1", cells[1].Value);
    }

    [TestMethod]
    public void XmlEscapesMarkupInTextAndAttributes()
    {
        const string value = "x & <y> \"q\" 'z'";

        string text = TableExport.Build(TableExportFormat.Xml, ["a<b"], [[value]]);

        XElement cell = XDocument.Parse(text).Root!.Elements("row").Single().Elements("c").Single();
        Assert.AreEqual("a<b", cell.Attribute("name")?.Value);
        Assert.AreEqual(value, cell.Value);
    }

    [TestMethod]
    public void XmlDropsTheCharactersXmlCannotCarryAndKeepsTabs()
    {
        string text = TableExport.Build(TableExportFormat.Xml, ["v"], [["a\u0001b\tc￾"]]);

        XElement cell = XDocument.Parse(text).Root!.Elements("row").Single().Elements("c").Single();
        Assert.AreEqual("ab\tc", cell.Value);
    }

    [TestMethod]
    public void XmlKeepsACarriageReturnInsideAValue()
    {
        string text = TableExport.Build(TableExportFormat.Xml, ["note"], [["a\r\nb"]]);

        XElement cell = XDocument.Parse(text).Root!.Elements("row").Single().Elements("c").Single();
        Assert.AreEqual("a\r\nb", cell.Value);
    }

    [TestMethod]
    public void JsonWritesAnUnpairedSurrogateAsTheReplacementCharacter()
    {
        string text = TableExport.Build(TableExportFormat.Json, ["v"], [["a\uD800b"]]);

        using JsonDocument document = JsonDocument.Parse(text);
        Assert.AreEqual("a�b", document.RootElement[0].GetProperty("v").GetString());
    }

    [TestMethod]
    public void AnXmlHeaderMadeOnlyOfInvalidCharactersBecomesTheColumnPosition()
    {
        string text = TableExport.Build(TableExportFormat.Xml, ["name", "\u0001"], [["A", "1"]]);

        XElement cell = XDocument.Parse(text).Root!.Elements("row").Single().Elements("c").Last();
        Assert.AreEqual("column_2", cell.Attribute("name")?.Value);
    }

    [TestMethod]
    public void XmlWithNoRowsIsAnEmptyTable()
    {
        string text = TableExport.Build(TableExportFormat.Xml, ["name"], []);

        XDocument document = XDocument.Parse(text);
        Assert.AreEqual("table", document.Root!.Name.LocalName);
        Assert.AreEqual(0, document.Root.Elements().Count());
    }

    [TestMethod]
    public void AnEmptyColumnNameBecomesColumnAndItsPosition()
    {
        string text = TableExport.Build(TableExportFormat.Json, ["name", ""], [["A", "1"]]);

        using JsonDocument document = JsonDocument.Parse(text);
        Assert.AreEqual("column_2", document.RootElement[0].EnumerateObject().Last().Name);
    }

    [TestMethod]
    public void ARepeatedColumnNameGetsASuffixUntilItIsUnusedAndAnExistingSuffixIsNotReused()
    {
        string text = TableExport.Build(TableExportFormat.Json, ["a", "a", "a_2"], [["1", "2", "3"]]);

        using JsonDocument document = JsonDocument.Parse(text);
        string[] keys = document.RootElement[0].EnumerateObject().Select(property => property.Name).ToArray();
        CollectionAssert.AreEqual(new[] { "a", "a_2", "a_2_2" }, keys);
    }

    [TestMethod]
    public void ColumnNamesDifferingOnlyInCaseAreDifferentKeys()
    {
        string text = TableExport.Build(TableExportFormat.Json, ["A", "a"], [["1", "2"]]);

        using JsonDocument document = JsonDocument.Parse(text);
        CollectionAssert.AreEqual(new[] { "A", "a" }, document.RootElement[0].EnumerateObject().Select(property => property.Name).ToArray());
    }

    [TestMethod]
    public void ExtensionIsAppendedUnlessTheNameAlreadyEndsInIt()
    {
        Assert.AreEqual("out.json", TableExport.WithExtension("out", TableExportFormat.Json));
        Assert.AreEqual("out.json", TableExport.WithExtension("out.json", TableExportFormat.Json));
        Assert.AreEqual("OUT.JSON", TableExport.WithExtension("OUT.JSON", TableExportFormat.Json));
        Assert.AreEqual("out.csv.json", TableExport.WithExtension("out.csv", TableExportFormat.Json));
        Assert.AreEqual("out.txt.xml", TableExport.WithExtension("out.txt", TableExportFormat.Xml));
        Assert.AreEqual("report.csv", TableExport.WithExtension("report", TableExportFormat.Csv));
    }
}
