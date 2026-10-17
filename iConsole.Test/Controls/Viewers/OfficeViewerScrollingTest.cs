using DocumentFormat.OpenXml.Packaging;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Services;
using Moq;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace iConsole.Test.Controls.Viewers;

[TestClass]
public class OfficeViewerScrollingTest
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "office-scroll-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    [TestMethod]
    public async Task TallWordDocument_ScrollsVertically()
    {
        string path = Path.Combine(_directory, "tall.docx");
        using (WordprocessingDocument doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            MainDocumentPart main = doc.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(
                Enumerable.Range(1, 200).Select(i => new W.Paragraph(new W.Run(new W.Text($"Paragraph {i}"))))));
        }

        await AssertScrollsAsync(path);
    }

    [TestMethod]
    public async Task TallSpreadsheet_ScrollsVertically()
    {
        string path = Path.Combine(_directory, "tall.xlsx");
        using (SpreadsheetDocument doc = SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            WorkbookPart workbook = doc.AddWorkbookPart();
            workbook.Workbook = new S.Workbook(new S.Sheets());
            WorksheetPart sheet = workbook.AddNewPart<WorksheetPart>();
            sheet.Worksheet = new S.Worksheet(new S.SheetData(
                Enumerable.Range(1, 200).Select(i => new S.Row(
                    new S.Cell
                    {
                        CellReference = "A" + i,
                        DataType = S.CellValues.InlineString,
                        InlineString = new S.InlineString(new S.Text("Row " + i)),
                    }) { RowIndex = (uint)i })));
            workbook.Workbook.Sheets!.Append(new S.Sheet { Id = workbook.GetIdOfPart(sheet), SheetId = 1, Name = "Data" });
        }

        await AssertScrollsAsync(path);
    }

    private static async Task AssertScrollsAsync(string path)
    {
        ThemeService theme = new();
        Window parent = new(theme) { ViewSize = new Size(100, 30) };
        TextViewerControl viewer = new(false, theme) { Parent = parent, ViewSize = new Size(100, 30) };

        await new FileToTextConverterService().ReadFileAsync(new Url(path), new LocalFileSystemAdapter(), viewer.TextModel);
        Assert.IsTrue(viewer.TextModel.Lines.Count > 100, "document should be taller than the view");
        viewer.OnInitializeLayout();

        Command pageDown = (Command)viewer.Commands.First(c => c is ScrollPageDownCommand);
        Command down = (Command)viewer.Commands.First(c => c is ScrollDownCommand);
        Command pageUp = (Command)viewer.Commands.First(c => c is ScrollPageUpCommand);

        await pageDown.ExecuteAsync(null);
        Assert.IsTrue(viewer.ScrollableModel.ScrollPosition.Y > 0, "PageDown must scroll");

        long afterPage = viewer.ScrollableModel.ScrollPosition.Y;
        for (int i = 0; i < 60; i++)
        {
            await down.ExecuteAsync(null);
        }
        Assert.IsTrue(viewer.ScrollableModel.ScrollPosition.Y > afterPage, "Down arrow must keep scrolling");

        await pageUp.ExecuteAsync(null);
        Assert.IsTrue(viewer.ScrollableModel.ScrollPosition.Y < afterPage + 60);
    }
}
