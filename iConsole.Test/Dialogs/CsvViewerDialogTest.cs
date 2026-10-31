using System.Text;
using iConsole.Csv;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Dialogs;

[TestClass]
public class CsvViewerDialogTest
{
    private static CsvViewerDialog CreateDialog(string path, string text)
    {
        var document = new Url(path);
        Mock<IMemoryFile> file = new();
        file.SetupGet(f => f.Content).Returns(Encoding.UTF8.GetBytes(text));
        Mock<IContentModel> fileSystem = new();
        fileSystem.Setup(f => f.ReadFileAsync(document)).ReturnsAsync(file.Object);
        Mock<IContentModelService> models = new();
        models.Setup(m => m.GetFocusedModel()).Returns(fileSystem.Object);
        return new CsvViewerDialog(models.Object, Mock.Of<IFileToTextConverterService>(), Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(), new ThemeService()) { Url = document };
    }

    [TestMethod]
    public async Task OpensWithEveryRowAndTheCounts()
    {
        CsvViewerDialog dialog = CreateDialog("file:///data/sales.csv", "name,amount\nA,1\nB,2\n");

        await dialog.OnInitializeAsync();

        Assert.AreEqual(2, dialog.Grid.RowCount);
        Assert.AreEqual(2, dialog.Grid.ColumnCount);
        Assert.AreEqual("2 of 2 row(s), 2 column(s)", dialog.Status);
    }

    [TestMethod]
    public async Task ATsvFileIsReadWithTabsEvenWhenItsValuesHaveCommas()
    {
        CsvViewerDialog dialog = CreateDialog("file:///data/notes.tsv", "name\tnote\nA\tone, two\n");

        await dialog.OnInitializeAsync();

        Assert.AreEqual('\t', dialog.Document!.Delimiter);
        Assert.AreEqual(2, dialog.Grid.ColumnCount);
        Assert.AreEqual("one, two", dialog.Grid.CellAt(0, 1));
    }

    [TestMethod]
    public async Task AQueryLandsOnTheNextIdlePassAndTheStatusSaysHowManyMatched()
    {
        CsvViewerDialog dialog = CreateDialog("file:///data/sales.csv", "name,amount\nA,1\nB,20\nC,30\n");
        await dialog.OnInitializeAsync();

        dialog.StartQuery("SELECT name WHERE amount > 10");
        await Task.WhenAny(dialog.QueryTask!);
        dialog.OnIdle();

        Assert.AreEqual(2, dialog.Grid.RowCount);
        Assert.AreEqual(1, dialog.Grid.ColumnCount);
        Assert.AreEqual("2 of 3 row(s), 1 column(s)", dialog.Status);
    }

    [TestMethod]
    public async Task AQueryWithAnErrorKeepsTheRowsAndSaysWhy()
    {
        CsvViewerDialog dialog = CreateDialog("file:///data/sales.csv", "name,amount\nA,1\n");
        await dialog.OnInitializeAsync();

        dialog.StartQuery("SELECT colour");
        await Task.WhenAny(dialog.QueryTask!);
        dialog.OnIdle();

        Assert.AreEqual(1, dialog.Grid.RowCount, "the grid keeps what it showed");
        StringAssert.Contains(dialog.Status, "'colour'");
    }

    [TestMethod]
    public async Task AnEmptyFileSaysSo()
    {
        CsvViewerDialog dialog = CreateDialog("file:///data/empty.csv", string.Empty);

        await dialog.OnInitializeAsync();

        Assert.AreEqual("The file is empty", dialog.Status);
    }

    [TestMethod]
    public void ThePathDecidesWhichFilesAreCsvAndWhichAreTsv()
    {
        Assert.IsTrue(CsvFileTypes.IsCsvFile("/x/a.csv"));
        Assert.IsTrue(CsvFileTypes.IsCsvFile("/x/a.TSV"));
        Assert.IsFalse(CsvFileTypes.IsCsvFile("/x/a.txt"));
    }
}
