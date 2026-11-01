using System.IO.Compression;
using iConsole.Data;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Test.Services;

/// <summary>Reading an OpenDocument spreadsheet (.ods) into the same grids an Excel workbook gets.</summary>
[TestClass]
public class OpenDocumentSpreadsheetTest
{
    private const string Header = """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content
            xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
            xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
            xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0">
          <office:body><office:spreadsheet>
        """;

    private const string Footer = "</office:spreadsheet></office:body></office:document-content>";

    private string _directory = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "ods-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch
        {
        }
    }

    private string MakeOds(string sheets, string name = "book.ods")
    {
        string path = Path.Combine(_directory, name);
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using StreamWriter writer = new(zip.CreateEntry("content.xml").Open());
        writer.Write(Header + sheets + Footer);
        return path;
    }

    private static string Text(string value) => $"<table:table-cell office:value-type=\"string\"><text:p>{value}</text:p></table:table-cell>";

    [Timeout(60_000)]
    [TestMethod]
    public void ASheet_IsAGridWithColumnLettersRowNumbersAndItsName()
    {
        string path = MakeOds($"""
            <table:table table:name="Fruit">
              <table:table-row>{Text("Name")}{Text("Qty")}</table:table-row>
              <table:table-row>{Text("Apple")}<table:table-cell office:value-type="float" office:value="3"><text:p>3</text:p></table:table-cell></table:table-row>
            </table:table>
            """);

        List<string> lines = [.. OfficeTextRenderer.RenderOpenDocumentSpreadsheet(path)];

        Assert.IsTrue(lines.Any(l => l.Contains("Fruit")), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(l => l.Contains("│ A ") && l.Contains(" B ")), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(l => l.Contains("│ 1 │ Name") && l.Contains("Qty")), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(l => l.Contains("│ 2 │ Apple") && l.TrimEnd().EndsWith("3 │")), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(l => l.StartsWith('┌')) && lines.Any(l => l.StartsWith('└')));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SeveralSheets_EachGetTheirOwnHeading()
    {
        string path = MakeOds($"""
            <table:table table:name="First"><table:table-row>{Text("a")}</table:table-row></table:table>
            <table:table table:name="Second"><table:table-row>{Text("b")}</table:table-row></table:table>
            <table:table table:name="Empty"><table:table-row><table:table-cell/></table:table-row></table:table>
            """);

        List<string> lines = [.. OfficeTextRenderer.RenderOpenDocumentSpreadsheet(path)];

        int first = lines.FindIndex(l => l.Contains("First"));
        int second = lines.FindIndex(l => l.Contains("Second"));
        int empty = lines.FindIndex(l => l.Contains("Empty"));
        Assert.IsTrue(first >= 0 && second > first && empty > second, string.Join("\n", lines));
        Assert.AreEqual("  (empty)", lines[empty + 1].TrimEnd().Length > 0 ? lines[empty + 1] : lines[empty + 2]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Cells_ShowWhatTheSpreadsheetDisplays_ElseTheirStoredValue()
    {
        string path = MakeOds("""
            <table:table table:name="Types">
              <table:table-row>
                <table:table-cell office:value-type="percentage" office:value="0.25"><text:p>25%</text:p></table:table-cell>
                <table:table-cell office:value-type="float" office:value="1234.5"/>
                <table:table-cell office:value-type="date" office:date-value="2024-05-06"/>
                <table:table-cell office:value-type="date" office:date-value="2024-05-06T13:30:00"/>
                <table:table-cell office:value-type="time" office:time-value="PT01H30M05S"/>
                <table:table-cell office:value-type="boolean" office:boolean-value="true"/>
                <table:table-cell office:value-type="string" office:string-value="stored text"/>
              </table:table-row>
            </table:table>
            """);

        string joined = string.Join("\n", OfficeTextRenderer.RenderOpenDocumentSpreadsheet(path));

        StringAssert.Contains(joined, "25%");
        StringAssert.Contains(joined, "1234.5");
        StringAssert.Contains(joined, "2024-05-06 ");
        StringAssert.Contains(joined, "2024-05-06 13:30:00");
        StringAssert.Contains(joined, "01:30:05");
        StringAssert.Contains(joined, "TRUE");
        StringAssert.Contains(joined, "stored text");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RepeatedColumns_AreExpanded_AndEmptyOnesOnlyMoveTheColumn()
    {
        string path = MakeOds($"""
            <table:table table:name="Repeats">
              <table:table-row>
                <table:table-cell table:number-columns-repeated="2" office:value-type="string"><text:p>x</text:p></table:table-cell>
                <table:table-cell table:number-columns-repeated="3"/>
                {Text("y")}
              </table:table-row>
            </table:table>
            """);

        List<string> lines = [.. OfficeTextRenderer.RenderOpenDocumentSpreadsheet(path)];

        string header = lines.First(l => l.Contains("│ A "));
        StringAssert.Contains(header, " F ");
        string row = lines.First(l => l.Contains("│ 1 │"));
        Assert.AreEqual(2, row.Split('x').Length - 1);
        Assert.IsTrue(row.TrimEnd().EndsWith("y │"), row);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RepeatedRows_KeepTheirNumbers_AndABlockOfEmptyRowsIsSkippedNotExpanded()
    {
        string path = MakeOds($"""
            <table:table table:name="Rows">
              <table:table-row>{Text("top")}</table:table-row>
              <table:table-row table:number-rows-repeated="1048570"><table:table-cell table:number-columns-repeated="1024"/></table:table-row>
              <table:table-row table:number-rows-repeated="2">{Text("dup")}</table:table-row>
              <table:table-row table:number-rows-repeated="1000000"><table:table-cell table:number-columns-repeated="1024"/></table:table-row>
            </table:table>
            """);

        List<string> lines = [.. OfficeTextRenderer.RenderOpenDocumentSpreadsheet(path)];

        Assert.IsTrue(lines.Any(l => l.Contains(" 1 │ top")), string.Join("\n", lines.Take(12)));
        Assert.IsTrue(lines.Any(l => l.Contains("1048572 │ dup")), string.Join("\n", lines.Take(12)));
        Assert.IsTrue(lines.Any(l => l.Contains("1048573 │ dup")));
        Assert.IsTrue(lines.Count < 20, lines.Count.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TooManyRowsOrColumns_AreCut_WithALineSayingSo()
    {
        string manyRows = string.Concat(Enumerable.Range(0, 5000 + 5).Select(i => $"<table:table-row>{Text("r" + i)}</table:table-row>"));
        string wide = $"<table:table-row><table:table-cell table:number-columns-repeated=\"{100 + 20}\" office:value-type=\"string\"><text:p>w</text:p></table:table-cell></table:table-row>";
        string path = MakeOds($"<table:table table:name=\"Tall\">{manyRows}</table:table><table:table table:name=\"Wide\">{wide}</table:table>");

        List<string> lines = [.. OfficeTextRenderer.RenderOpenDocumentSpreadsheet(path)];

        Assert.IsTrue(lines.Any(l => l.Contains("only the first") && l.Contains("rows")), "rows cut");
        Assert.AreEqual(2, lines.Count(l => l.Contains("only the first")));
        Assert.IsFalse(lines.Any(l => l.Contains("r" + (5000 + 3))));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CoveredCells_OfAMergedAreaStayEmpty()
    {
        string path = MakeOds($"""
            <table:table table:name="Merged">
              <table:table-row>
                <table:table-cell table:number-columns-spanned="2" office:value-type="string"><text:p>wide</text:p></table:table-cell>
                <table:covered-table-cell/>
                {Text("after")}
              </table:table-row>
            </table:table>
            """);

        string row = OfficeTextRenderer.RenderOpenDocumentSpreadsheet(path).First(l => l.Contains("│ 1 │"));

        StringAssert.Contains(row, "wide");
        StringAssert.Contains(row, "after");
        Assert.AreEqual(5, row.Count(c => c == (char)0x2502), row);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ADamagedFile_OrOneWithoutContent_ReportsInsteadOfThrowing()
    {
        string bad = Path.Combine(_directory, "bad.ods");
        File.WriteAllText(bad, "not a zip");
        Assert.IsTrue(OfficeTextRenderer.RenderOpenDocumentSpreadsheet(bad).Single().StartsWith("[Could not read the workbook"));

        string noContent = Path.Combine(_directory, "empty.ods");
        using (ZipArchive zip = ZipFile.Open(noContent, ZipArchiveMode.Create))
        {
            zip.CreateEntry("mimetype");
        }

        Assert.AreEqual("[Could not read the workbook: content.xml is missing]", OfficeTextRenderer.RenderOpenDocumentSpreadsheet(noContent).Single());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheViewerConverter_ReadsOdsAndOtsFiles()
    {
        string ods = MakeOds($"<table:table table:name=\"S\"><table:table-row>{Text("hello")}</table:table-row></table:table>");
        string ots = Path.Combine(_directory, "template.ots");
        File.Copy(ods, ots);

        foreach (string file in new[] { ods, ots })
        {
            TextModel model = new();
            await new FileToTextConverterService().ReadFileAsync(new Url(file), new LocalFileSystemAdapter(), model);
            Assert.IsTrue(model.Lines.Any(l => l.Contains("hello")), file);
            Assert.IsTrue(model.Lines.Any(l => l.StartsWith('┌')), file);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnOdsIsNotShownAsTextInAnEncoding()
    {
        Assert.IsFalse(FileToTextConverterService.IsEncodingRelevant(new Url(Path.Combine(_directory, "a.ods"))));
        Assert.IsFalse(FileToTextConverterService.IsEncodingRelevant(new Url(Path.Combine(_directory, "a.ots"))));
        Assert.IsTrue(FileToTextConverterService.IsEncodingRelevant(new Url(Path.Combine(_directory, "a.csv"))));
    }
}
