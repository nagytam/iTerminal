using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using iConsole.Services;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace iConsole.Test.Services;

[TestClass]
public class OfficeTextRendererCoverageTest
{
    private const string OdfHeader = """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content
            xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
            xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
            xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0">
        """;

    private const int MaxRows = 5000;
    private const int MaxColumns = 100;

    private string _directory = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "office-render-cov-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    private string WriteOdt(string bodyXml, string entryName = "content.xml")
    {
        string path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".odt");
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using StreamWriter writer = new(zip.CreateEntry(entryName).Open());
        writer.Write(OdfHeader + "<office:body><office:text>" + bodyXml + "</office:text></office:body></office:document-content>");
        return path;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OpenDocumentText_MissingContentXml_Reports()
    {
        string path = WriteOdt("<text:p>x</text:p>", entryName: "other.xml");

        IReadOnlyList<string> lines = OfficeTextRenderer.RenderOpenDocumentText(path);

        Assert.AreEqual("[Could not read the document: content.xml is missing]", lines.Single());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OpenDocumentText_NoBody_RendersNothing()
    {
        string path = Path.Combine(_directory, "nobody.odt");
        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using StreamWriter writer = new(zip.CreateEntry("content.xml").Open());
            writer.Write(OdfHeader + "</office:document-content>");
        }

        Assert.AreEqual(0, OfficeTextRenderer.RenderOpenDocumentText(path).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OpenDocumentText_RendersTitleListsSectionsAndInlineElements()
    {
        string path = WriteOdt("""
            <text:p text:style-name="Title">Big title</text:p>
            <text:h>Level defaults to one</text:h>
            <text:h text:outline-level="9">Clamped deep heading</text:h>
            <text:h text:outline-level="2">   </text:h>
            <text:p>one<text:tab/>two<text:line-break/>three<text:s/>four<text:note><text:p>footnote</text:p></text:note><text:span>span</text:span></text:p>
            <text:p>   </text:p>
            <text:p>after blank</text:p>
            <text:list>
              <text:list-header><text:p>list header</text:p></text:list-header>
              <text:list-item>
                <text:p>outer</text:p>
                <text:list><text:list-item><text:p>inner</text:p></text:list-item></text:list>
              </text:list-item>
            </text:list>
            <text:section><text:p>in section</text:p></text:section>
            <table:table>
              <table:table-row>
                <table:table-cell table:number-columns-repeated="2"><text:p>rep</text:p></table:table-cell>
              </table:table-row>
              <table:table-row/>
            </table:table>
            """);

        IReadOnlyList<string> lines = OfficeTextRenderer.RenderOpenDocumentText(path);
        string all = string.Join("\n", lines);

        CollectionAssert.Contains(lines.ToList(), "Big title");
        CollectionAssert.Contains(lines.ToList(), new string('═', "Big title".Length));
        CollectionAssert.Contains(lines.ToList(), "# Level defaults to one");
        CollectionAssert.Contains(lines.ToList(), "###### Clamped deep heading");
        Assert.IsFalse(all.Contains("##   "), "an empty heading is left out\n" + all);
        CollectionAssert.Contains(lines.ToList(), "one    two three four" + "span");
        CollectionAssert.Contains(lines.ToList(), "  • list header");
        CollectionAssert.Contains(lines.ToList(), "  • outer");
        CollectionAssert.Contains(lines.ToList(), "    • inner");
        CollectionAssert.Contains(lines.ToList(), "in section");
        Assert.IsFalse(all.Contains("footnote"), all);
        Assert.AreEqual(2, lines.First(l => l.Contains("rep")).Split("rep").Length - 1);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Word_RendersHeaderFooterAndNestedContent()
    {
        string path = Path.Combine(_directory, "full.docx");
        using (WordprocessingDocument doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            MainDocumentPart main = doc.AddMainDocumentPart();
            HeaderPart header = main.AddNewPart<HeaderPart>();
            header.Header = new W.Header(new W.Paragraph(new W.Run(new W.Text("Top line"))));
            HeaderPart emptyHeader = main.AddNewPart<HeaderPart>();
            emptyHeader.Header = new W.Header(new W.Paragraph());
            FooterPart footer = main.AddNewPart<FooterPart>();
            footer.Footer = new W.Footer(new W.Paragraph(new W.Run(new W.Text("Bottom line"))));

            W.Paragraph Para(string text, string? style = null) =>
                new(style is null ? null! : new W.ParagraphProperties(new W.ParagraphStyleId { Val = style }),
                    new W.Run(new W.Text(text)));

            main.Document = new W.Document(new W.Body(
                Para("First"),
                new W.Paragraph(),
                new W.Paragraph(),
                Para("Second", "Heading3"),
                Para("Ignored heading", "Headingx"),
                new W.Paragraph(new W.Run(new W.Text("a"), new W.TabChar(), new W.Text("b"), new W.Break(), new W.Text("c"))),
                new W.Paragraph(
                    new W.ParagraphProperties(new W.NumberingProperties()),
                    new W.Run(new W.Text("plain bullet"))),
                new W.SdtBlock(new W.SdtContentBlock(Para("inside sdt"))),
                new W.Table(
                    new W.TableRow(
                        new W.TableCell(new W.Paragraph(new W.Run(new W.Text("x"))), new W.Paragraph(new W.Run(new W.Text("y")))),
                        new W.TableCell(new W.Paragraph(new W.Run(new W.Text(new string('z', 80)))))))));
        }

        IReadOnlyList<string> lines = OfficeTextRenderer.RenderWord(path);
        string all = string.Join("\n", lines);

        Assert.IsTrue(all.Contains("Header"), all);
        Assert.IsTrue(all.Contains("Top line"), all);
        Assert.IsTrue(all.Contains("Footer"), all);
        Assert.IsTrue(all.Contains("Bottom line"), all);
        CollectionAssert.Contains(lines.ToList(), "### Second");
        CollectionAssert.Contains(lines.ToList(), "a    bc".Replace("bc", "b c"));
        CollectionAssert.Contains(lines.ToList(), "  • plain bullet");
        CollectionAssert.Contains(lines.ToList(), "inside sdt");
        Assert.IsTrue(all.Contains("x y"), "paragraphs in a cell are joined\n" + all);
        Assert.IsTrue(all.Contains("…"), "a long cell is cut with an ellipsis\n" + all);
        Assert.AreEqual(1, lines.Count(l => l == "Top line"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Word_EmptyDocument_RendersNothing()
    {
        string path = Path.Combine(_directory, "empty.docx");
        using (WordprocessingDocument doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart();
        }

        Assert.AreEqual(0, OfficeTextRenderer.RenderWord(path).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Spreadsheet_RendersEveryCellKind()
    {
        string path = Path.Combine(_directory, "kinds.xlsx");
        using (SpreadsheetDocument doc = SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            WorkbookPart workbook = doc.AddWorkbookPart();
            workbook.Workbook = new S.Workbook(new S.Sheets());

            SharedStringTablePart sharedPart = workbook.AddNewPart<SharedStringTablePart>();
            sharedPart.SharedStringTable = new S.SharedStringTable(new S.SharedStringItem(new S.Text("shared one")));

            WorkbookStylesPart styles = workbook.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new S.Stylesheet(
                new S.NumberingFormats(
                    new S.NumberingFormat { NumberFormatId = 164, FormatCode = "yyyy\\-mm\\-dd hh:mm" },
                    new S.NumberingFormat { NumberFormatId = 165, FormatCode = "\"d\"0.00[Red]" }),
                new S.CellFormats(
                    new S.CellFormat { NumberFormatId = 0 },
                    new S.CellFormat { NumberFormatId = 164 },
                    new S.CellFormat { NumberFormatId = 165 },
                    new S.CellFormat { NumberFormatId = 20 }));

            WorksheetPart sheet = workbook.AddNewPart<WorksheetPart>();
            S.Cell Raw(string reference, string value, S.CellValues? type = null, uint style = 0)
            {
                S.Cell cell = new() { CellReference = reference, CellValue = new S.CellValue(value), StyleIndex = style };
                if (type is { } t)
                {
                    cell.DataType = t;
                }

                return cell;
            }

            sheet.Worksheet = new S.Worksheet(new S.SheetData(
                new S.Row(
                    Raw("A1", "0", S.CellValues.SharedString),
                    Raw("B1", "99", S.CellValues.SharedString),
                    Raw("C1", "text", S.CellValues.String),
                    Raw("D1", "#DIV/0!", S.CellValues.Error),
                    Raw("E1", "0", S.CellValues.Boolean)),
                new S.Row(
                    Raw("A2", "45000.5", style: 1),
                    Raw("B2", "12", style: 2),
                    Raw("C2", "1E30", style: 1),
                    Raw("D2", "not a number"),
                    Raw("E2", "0.25", style: 3)),
                new S.Row(
                    Raw("A3", "1", S.CellValues.Date),
                    new S.Cell { DataType = S.CellValues.InlineString, InlineString = new S.InlineString(new S.Text("no reference")) })));
            workbook.Workbook.Sheets!.Append(new S.Sheet { Id = workbook.GetIdOfPart(sheet), SheetId = 1, Name = "Kinds" });
        }

        IReadOnlyList<string> lines = OfficeTextRenderer.RenderSpreadsheet(path);
        string all = string.Join("\n", lines);

        Assert.IsTrue(all.Contains("shared one"), all);
        Assert.IsTrue(all.Contains("#DIV/0!"), all);
        Assert.IsTrue(all.Contains("FALSE"), all);
        Assert.IsTrue(all.Contains("2023-03-15 12:00"), "a custom date format with a time part\n" + all);
        Assert.IsTrue(all.Contains("not a number"), all);
        Assert.IsTrue(all.Contains("1E+30"), "a date style outside the OADate range falls back to the number\n" + all);
        Assert.IsTrue(all.Contains("no reference"), all);
        Assert.IsTrue(all.Contains("1899-12-31"), "a cell typed as Date\n" + all);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Spreadsheet_EmptySheetsAndMissingSheetData_AreMarked()
    {
        string path = Path.Combine(_directory, "empty.xlsx");
        using (SpreadsheetDocument doc = SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            WorkbookPart workbook = doc.AddWorkbookPart();
            workbook.Workbook = new S.Workbook(new S.Sheets());

            WorksheetPart noData = workbook.AddNewPart<WorksheetPart>();
            noData.Worksheet = new S.Worksheet();
            WorksheetPart blank = workbook.AddNewPart<WorksheetPart>();
            blank.Worksheet = new S.Worksheet(new S.SheetData(
                new S.Row(new S.Cell { CellReference = "A1", DataType = S.CellValues.String, CellValue = new S.CellValue("") })));
            workbook.Workbook.Sheets!.Append(new S.Sheet { Id = workbook.GetIdOfPart(noData), SheetId = 1, Name = "NoData" });
            workbook.Workbook.Sheets.Append(new S.Sheet { Id = workbook.GetIdOfPart(blank), SheetId = 2, Name = "Blank" });
            workbook.Workbook.Sheets.Append(new S.Sheet { SheetId = 3 });
        }

        IReadOnlyList<string> lines = OfficeTextRenderer.RenderSpreadsheet(path);

        Assert.AreEqual(2, lines.Count(l => l == "  (empty)"), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(l => l.Contains("(sheet)")), string.Join("\n", lines));
        Assert.IsTrue(lines.Contains("  (not a worksheet)"), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Spreadsheet_WithoutSheets_RendersNothing()
    {
        string path = Path.Combine(_directory, "nosheets.xlsx");
        using (SpreadsheetDocument doc = SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            doc.AddWorkbookPart().Workbook = new S.Workbook();
        }

        Assert.AreEqual(0, OfficeTextRenderer.RenderSpreadsheet(path).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Spreadsheet_TooManyRowsAndColumns_AreTruncatedWithANote()
    {
        string path = Path.Combine(_directory, "big.xlsx");
        using (SpreadsheetDocument doc = SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            WorkbookPart workbook = doc.AddWorkbookPart();
            workbook.Workbook = new S.Workbook(new S.Sheets());
            WorksheetPart sheet = workbook.AddNewPart<WorksheetPart>();
            S.SheetData data = new();
            for (int r = 1; r <= MaxRows + 2; r++)
            {
                S.Row row = new() { RowIndex = (uint)r };
                row.Append(new S.Cell { CellReference = "A" + r, CellValue = new S.CellValue(r.ToString()) });
                if (r == 1)
                {
                    row.Append(new S.Cell
                    {
                        CellReference = OfficeTextRenderer.ColumnName(MaxColumns + 1) + "1",
                        CellValue = new S.CellValue("1"),
                    });
                }

                data.Append(row);
            }

            sheet.Worksheet = new S.Worksheet(data);
            workbook.Workbook.Sheets!.Append(new S.Sheet { Id = workbook.GetIdOfPart(sheet), SheetId = 1, Name = "Big" });
        }

        IReadOnlyList<string> lines = OfficeTextRenderer.RenderSpreadsheet(path);

        Assert.IsTrue(lines.Any(l => l.Contains("only the first")), "a note says the sheet was cut");
        Assert.IsFalse(lines.Any(l => l.StartsWith("│ " + (MaxRows + 1))));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Spreadsheet_Damaged_Reports()
    {
        string path = Path.Combine(_directory, "bad.xlsx");
        File.WriteAllText(path, "nope");

        Assert.IsTrue(OfficeTextRenderer.RenderSpreadsheet(path).Single().StartsWith("[Could not read the workbook"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Presentation_RendersTablesBreaksAndSkipsBrokenSlides()
    {
        string path = Path.Combine(_directory, "tables.pptx");
        using (PresentationDocument doc = PresentationDocument.Create(path, DocumentFormat.OpenXml.PresentationDocumentType.Presentation))
        {
            PresentationPart presentation = doc.AddPresentationPart();
            presentation.Presentation = new P.Presentation(new P.SlideIdList());

            SlidePart slide = presentation.AddNewPart<SlidePart>();
            P.GraphicFrame frame = new(
                new P.NonVisualGraphicFrameProperties(
                    new P.NonVisualDrawingProperties { Id = 2, Name = "t" },
                    new P.NonVisualGraphicFrameDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.Transform(),
                new A.Graphic(new A.GraphicData(
                    new A.Table(
                        new A.TableRow(
                            new A.TableCell(new A.TextBody(new A.BodyProperties(), new A.Paragraph(new A.Run(new A.Text("cell one"))))),
                            new A.TableCell(new A.TextBody(new A.BodyProperties(), new A.Paragraph(new A.Run(new A.Text("cell two"))))))))));
            P.Shape shape = new(
                new P.NonVisualShapeProperties(new P.NonVisualDrawingProperties { Id = 3, Name = "s" },
                    new P.NonVisualShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
                new P.ShapeProperties(),
                new P.TextBody(new A.BodyProperties(),
                    new A.Paragraph(new A.Run(new A.Text("first")), new A.Break(), new A.Run(new A.Text("second"))),
                    new A.Paragraph(new A.Run(new A.Text("   ")))));
            slide.Slide = new P.Slide(new P.CommonSlideData(new P.ShapeTree(shape, frame)));

            presentation.Presentation.SlideIdList!.Append(new P.SlideId { Id = 256, RelationshipId = presentation.GetIdOfPart(slide) });
            presentation.Presentation.SlideIdList.Append(new P.SlideId { Id = 257 });
        }

        IReadOnlyList<string> lines = OfficeTextRenderer.RenderPresentation(path);
        string all = string.Join("\n", lines);

        Assert.IsTrue(lines[0].Contains("Slide 1") && !lines[0].Contains(':'), all);
        Assert.IsTrue(lines.Contains("  • first second"), all);
        Assert.IsTrue(all.Contains("cell one") && all.Contains("cell two"), all);
        Assert.IsFalse(all.Contains("Slide 2"), "a slide without a part is skipped\n" + all);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Presentation_WithoutSlideList_RendersNothing()
    {
        string path = Path.Combine(_directory, "none.pptx");
        using (PresentationDocument doc = PresentationDocument.Create(path, DocumentFormat.OpenXml.PresentationDocumentType.Presentation))
        {
            doc.AddPresentationPart().Presentation = new P.Presentation();
        }

        Assert.AreEqual(0, OfficeTextRenderer.RenderPresentation(path).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Presentation_Damaged_Reports()
    {
        string path = Path.Combine(_directory, "bad.pptx");
        File.WriteAllText(path, "nope");

        Assert.IsTrue(OfficeTextRenderer.RenderPresentation(path).Single().StartsWith("[Could not read the presentation"));
    }
}
