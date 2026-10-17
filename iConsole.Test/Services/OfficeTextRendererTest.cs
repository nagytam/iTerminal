using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using iConsole.Services;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace iConsole.Test.Services;

[TestClass]
public class OfficeTextRendererTest
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "office-render-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [TestMethod]
    public void ColumnNames_RoundTrip()
    {
        Assert.AreEqual("A", OfficeTextRenderer.ColumnName(0));
        Assert.AreEqual("Z", OfficeTextRenderer.ColumnName(25));
        Assert.AreEqual("AA", OfficeTextRenderer.ColumnName(26));
        Assert.AreEqual(27, OfficeTextRenderer.ColumnIndex("AB12"));
        Assert.AreEqual(0, OfficeTextRenderer.ColumnIndex("A1"));
    }

    [TestMethod]
    public void OpenDocumentText_RendersHeadingsBulletsAndTables()
    {
        string path = Path.Combine(_directory, "doc.odt");
        const string content = """
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content
                xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
                xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0">
              <office:body><office:text>
                <text:h text:outline-level="1">Intro</text:h>
                <text:p>Body<text:s text:c="2"/>text</text:p>
                <text:list><text:list-item><text:p>first point</text:p></text:list-item></text:list>
                <table:table>
                  <table:table-row>
                    <table:table-cell><text:p>Name</text:p></table:table-cell>
                    <table:table-cell><text:p>Qty</text:p></table:table-cell>
                  </table:table-row>
                  <table:table-row>
                    <table:table-cell><text:p>Apple</text:p></table:table-cell>
                    <table:table-cell><text:p>3</text:p></table:table-cell>
                  </table:table-row>
                </table:table>
              </office:text></office:body>
            </office:document-content>
            """;
        using (System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.Open(
            path, System.IO.Compression.ZipArchiveMode.Create))
        {
            using StreamWriter writer = new(zip.CreateEntry("content.xml").Open());
            writer.Write(content);
        }

        IReadOnlyList<string> lines = OfficeTextRenderer.RenderOpenDocumentText(path);

        CollectionAssert.Contains(lines.ToList(), "# Intro");
        CollectionAssert.Contains(lines.ToList(), "Body  text");
        CollectionAssert.Contains(lines.ToList(), "  • first point");
        Assert.IsTrue(lines.Any(l => l.StartsWith('┌')));
        Assert.IsTrue(lines.Any(l => l.Contains("│ Apple") && l.Contains("3")), string.Join("\n", lines));
    }

    [TestMethod]
    public void OpenDocumentText_DamagedFile_ReportsInsteadOfThrowing()
    {
        string path = Path.Combine(_directory, "bad.odt");
        File.WriteAllText(path, "not a zip");

        IReadOnlyList<string> lines = OfficeTextRenderer.RenderOpenDocumentText(path);

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the document"));
    }

    [TestMethod]
    public void Word_RendersHeadingsBulletsAndTables()
    {
        string path = Path.Combine(_directory, "doc.docx");
        using (WordprocessingDocument doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            MainDocumentPart main = doc.AddMainDocumentPart();
            W.Paragraph Para(string text, string? style = null, bool bullet = false)
            {
                W.Paragraph p = new(new W.Run(new W.Text(text)));
                if (style is not null || bullet)
                {
                    p.PrependChild(new W.ParagraphProperties(
                        style is null ? null! : new W.ParagraphStyleId { Val = style },
                        bullet ? new W.NumberingProperties(new W.NumberingLevelReference { Val = 1 }) : null!));
                }

                return p;
            }

            W.TableCell Cell(string text) => new(new W.Paragraph(new W.Run(new W.Text(text))));

            main.Document = new W.Document(new W.Body(
                Para("Report", "Title"),
                Para("Intro", "Heading1"),
                Para("Body text"),
                Para("first point", bullet: true),
                new W.Table(
                    new W.TableRow(Cell("Name"), Cell("Qty")),
                    new W.TableRow(Cell("Apple"), Cell("3")))));
        }

        IReadOnlyList<string> lines = OfficeTextRenderer.RenderWord(path);

        CollectionAssert.Contains(lines.ToList(), "Report");
        CollectionAssert.Contains(lines.ToList(), "# Intro");
        CollectionAssert.Contains(lines.ToList(), "Body text");
        Assert.IsTrue(lines.Contains("    • first point"), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(l => l.StartsWith('┌')));
        Assert.IsTrue(lines.Any(l => l.Contains("│ Apple") && l.Contains("3")));
    }

    [TestMethod]
    public void Spreadsheet_RendersGridWithDatesBooleansAndGaps()
    {
        string path = Path.Combine(_directory, "book.xlsx");
        using (SpreadsheetDocument doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
        {
            WorkbookPart workbook = doc.AddWorkbookPart();
            workbook.Workbook = new S.Workbook(new S.Sheets());

            WorkbookStylesPart styles = workbook.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new S.Stylesheet(
                new S.CellFormats(
                    new S.CellFormat { NumberFormatId = 0 },
                    new S.CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }));

            WorksheetPart sheet = workbook.AddNewPart<WorksheetPart>();
            sheet.Worksheet = new S.Worksheet(new S.SheetData(
                new S.Row(
                    InlineCell("A1", "Name"),
                    NumberCell("C1", "0.5")) { RowIndex = 1 },
                new S.Row(
                    new S.Cell { CellReference = "A5", CellValue = new S.CellValue("45000"), StyleIndex = 1 },
                    new S.Cell
                    {
                        CellReference = "B5",
                        DataType = S.CellValues.Boolean,
                        CellValue = new S.CellValue("1"),
                    }) { RowIndex = 5 }));
            workbook.Workbook.Sheets!.Append(new S.Sheet
            {
                Id = workbook.GetIdOfPart(sheet),
                SheetId = 1,
                Name = "Data",
            });
        }

        IReadOnlyList<string> lines = OfficeTextRenderer.RenderSpreadsheet(path);
        string all = string.Join("\n", lines);

        Assert.IsTrue(lines[0].Contains("Data"), all);
        Assert.IsTrue(all.Contains("│ A "), all);
        Assert.IsTrue(all.Contains("│ C "), all);
        Assert.IsTrue(all.Contains("2023-03-15"), "style 14 must render as a date\n" + all);
        Assert.IsTrue(all.Contains("TRUE"), all);
        Assert.IsTrue(all.Contains("0.5"), all);
        // Rows 2-4 are empty and are not drawn; the row numbers keep the gap visible.
        Assert.IsFalse(lines.Any(l => l.StartsWith("│ 3 ")), all);
        Assert.IsTrue(lines.Any(l => l.StartsWith("│ 5 ")), all);
    }

    [TestMethod]
    public void Presentation_RendersTitleBulletsAndNotes()
    {
        string path = Path.Combine(_directory, "deck.pptx");
        using (PresentationDocument doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation))
        {
            PresentationPart presentation = doc.AddPresentationPart();
            presentation.Presentation = new P.Presentation(new P.SlideIdList());

            SlidePart slide = presentation.AddNewPart<SlidePart>();
            slide.Slide = new P.Slide(new P.CommonSlideData(new P.ShapeTree(
                TextShape("Quarterly review", P.PlaceholderValues.Title),
                TextShape("Revenue up", null, level: 0),
                TextShape("Europe", null, level: 1))));

            NotesSlidePart notes = slide.AddNewPart<NotesSlidePart>();
            notes.NotesSlide = new P.NotesSlide(new P.CommonSlideData(new P.ShapeTree(
                TextShape("Mention the new region", P.PlaceholderValues.Body))));

            presentation.Presentation.SlideIdList!.Append(new P.SlideId
            {
                Id = 256,
                RelationshipId = presentation.GetIdOfPart(slide),
            });
        }

        IReadOnlyList<string> lines = OfficeTextRenderer.RenderPresentation(path);
        string all = string.Join("\n", lines);

        Assert.IsTrue(lines[0].Contains("Slide 1: Quarterly review"), all);
        Assert.IsTrue(lines.Contains("  • Revenue up"), all);
        Assert.IsTrue(lines.Contains("    • Europe"), all);
        Assert.IsTrue(lines.Contains("  Notes:"), all);
        Assert.IsTrue(lines.Contains("    Mention the new region"), all);
        Assert.IsFalse(all.Contains("• Quarterly review"), "the title is the heading, not a bullet");
    }

    [TestMethod]
    public void DamagedFile_ReportsInsteadOfThrowing()
    {
        string path = Path.Combine(_directory, "broken.docx");
        File.WriteAllText(path, "not a zip");

        IReadOnlyList<string> lines = OfficeTextRenderer.RenderWord(path);

        Assert.AreEqual(1, lines.Count);
        Assert.IsTrue(lines[0].StartsWith("[Could not read"), lines[0]);
    }

    private static S.Cell InlineCell(string reference, string text) => new()
    {
        CellReference = reference,
        DataType = S.CellValues.InlineString,
        InlineString = new S.InlineString(new S.Text(text)),
    };

    private static S.Cell NumberCell(string reference, string value) => new()
    {
        CellReference = reference,
        CellValue = new S.CellValue(value),
    };

    private static P.Shape TextShape(string text, P.PlaceholderValues? placeholder, int level = 0)
    {
        P.ApplicationNonVisualDrawingProperties app = new();
        if (placeholder is { } type)
        {
            app.Append(new P.PlaceholderShape { Type = type });
        }

        return new P.Shape(
            new P.NonVisualShapeProperties(new P.NonVisualDrawingProperties { Id = 1, Name = "s" },
                new P.NonVisualShapeDrawingProperties(), app),
            new P.ShapeProperties(),
            new P.TextBody(
                new A.BodyProperties(),
                new A.Paragraph(
                    new A.ParagraphProperties { Level = level },
                    new A.Run(new A.Text(text)))));
    }
}
