using System.Text;
using iConsole.Csv;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Dialogs;

[TestClass]
public class CsvViewerDialogTest
{
    private static CsvViewerDialog CreateDialog(string path, string text) => CreateDialog(path, Encoding.UTF8.GetBytes(text));

    private static CsvViewerDialog CreateDialog(string path, byte[] content)
    {
        var document = new Url(path);
        Mock<IMemoryFile> file = new();
        file.SetupGet(f => f.Content).Returns(content);
        Mock<IContentModel> fileSystem = new();
        fileSystem.Setup(f => f.ReadFileAsync(document)).ReturnsAsync(file.Object);
        Mock<IContentModelService> models = new();
        models.Setup(m => m.GetFocusedModel()).Returns(fileSystem.Object);
        return new CsvViewerDialog(models.Object, Mock.Of<IFileToTextConverterService>(), Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(), new ThemeService()) { Url = document };
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpensWithEveryRowAndTheCounts()
    {
        CsvViewerDialog dialog = CreateDialog("file:///data/sales.csv", "name,amount\nA,1\nB,2\n");

        await dialog.OnInitializeAsync();

        Assert.AreEqual(2, dialog.Grid.RowCount);
        Assert.AreEqual(2, dialog.Grid.ColumnCount);
        Assert.AreEqual("2 of 2 row(s), 2 column(s)", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATsvFileIsReadWithTabsEvenWhenItsValuesHaveCommas()
    {
        CsvViewerDialog dialog = CreateDialog("file:///data/notes.tsv", "name\tnote\nA\tone, two\n");

        await dialog.OnInitializeAsync();

        Assert.AreEqual('\t', dialog.Document!.Delimiter);
        Assert.AreEqual(2, dialog.Grid.ColumnCount);
        Assert.AreEqual("one, two", dialog.Grid.CellAt(0, 1));
    }

    [Timeout(60_000)]
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

    [Timeout(60_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptyFileSaysSo()
    {
        CsvViewerDialog dialog = CreateDialog("file:///data/empty.csv", string.Empty);

        await dialog.OnInitializeAsync();

        Assert.AreEqual("The file is empty", dialog.Status);
    }

    // Windows-1250 bytes that are not valid UTF-8, so the first reading falls to the machine's ANSI page: what that page is differs
    // from machine to machine, which is why no test below asserts the cells of the first reading.
    private static byte[] Windows1250File() =>
        TextEncodings.Available.First(e => e.CodePage == 1250).GetBytes("t\u0171r\u0151,n\u00e9v\nalma,3");

    private static async Task<bool> PressAsync(CsvViewerDialog dialog, ConsoleKey key, bool shift = false)
    {
        bool handled = await dialog.HandleKeyEventAsync(new KeyEvent('\0', key, shift, false, false));
        if (dialog.SwitchTask is { } reread)
        {
            await reread;
            dialog.OnIdle();
        }

        return handled;
    }

    private static async Task SwitchToAsync(CsvViewerDialog dialog, int codePage)
    {
        for (int i = 0; i <= TextEncodings.Available.Count && dialog.Encoding!.CodePage != codePage; i++)
        {
            await PressAsync(dialog, ConsoleKey.F8);
        }

        Assert.AreEqual(codePage, dialog.Encoding!.CodePage);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileInALegacyCodePageOpensAndF8ReachesTheRightOne()
    {
        CsvViewerDialog dialog = CreateDialog("file:///data/hu.csv", Windows1250File());
        Assert.IsNull(dialog.Encoding, "nothing is read yet");

        await dialog.OnInitializeAsync();

        Assert.IsNotNull(dialog.Encoding);
        Assert.AreEqual(1, dialog.Grid.RowCount);
        Assert.AreEqual(2, dialog.Grid.ColumnCount);

        await SwitchToAsync(dialog, 1250);

        Assert.AreEqual("t\u0171r\u0151", dialog.Document!.Headers[0]);
        Assert.AreEqual("n\u00e9v", dialog.Document.Headers[1]);
        Assert.IsFalse(dialog.Document.Headers.Any(h => h.Contains('\uFFFD')));
        Assert.AreEqual("alma", dialog.Grid.CellAt(0, 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftF8GoesBackAndF8ComesBackToTheSameReading()
    {
        CsvViewerDialog dialog = CreateDialog("file:///data/hu.csv", Windows1250File());
        await dialog.OnInitializeAsync();
        await SwitchToAsync(dialog, 1250);

        await PressAsync(dialog, ConsoleKey.F8, shift: true);
        Assert.AreNotEqual(1250, dialog.Encoding!.CodePage);

        await PressAsync(dialog, ConsoleKey.F8);
        Assert.AreEqual(1250, dialog.Encoding.CodePage);
        Assert.AreEqual("t\u0171r\u0151", dialog.Document!.Headers[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheTitleNamesTheCurrentCodePage()
    {
        CsvViewerDialog dialog = CreateDialog("file:///data/hu.csv", Windows1250File());
        await dialog.OnInitializeAsync();
        StringAssert.Contains(dialog.Title, TextEncodings.DisplayName(dialog.Encoding!), "named as soon as the file is read");

        await PressAsync(dialog, ConsoleKey.F8);

        StringAssert.Contains(dialog.Title, TextEncodings.DisplayName(dialog.Encoding!));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AByteOrderMarkNeverReachesTheFirstHeaderUnderAnyCodePage()
    {
        byte[] file = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("name,amount\nA,1\n")];
        CsvViewerDialog dialog = CreateDialog("file:///data/bom.csv", file);
        await dialog.OnInitializeAsync();

        for (int i = 0; i < TextEncodings.Available.Count; i++)
        {
            string first = dialog.Document!.Headers.FirstOrDefault() ?? string.Empty;
            Assert.IsFalse(first.Contains('﻿') || first.Contains("ï»¿"), TextEncodings.DisplayName(dialog.Encoding!));
            await PressAsync(dialog, ConsoleKey.F8);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASwitchDropsTheResultOfAQueryThatWasRunning()
    {
        CsvViewerDialog dialog = CreateDialog("file:///data/sales.csv", "name,amount\nA,1\nB,20\nC,30\n");
        await dialog.OnInitializeAsync();
        dialog.StartQuery("SELECT name WHERE amount > 10");
        Task<CsvQueryResult> query = dialog.QueryTask!;

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F8, false, false, false));
        await query;
        CsvDocument reread = await dialog.SwitchTask!;
        dialog.OnIdle();

        // The next code page reads this UTF-8 file as UTF-16, so the rows differ from the query's two.
        Assert.AreSame(reread, dialog.Document);
        Assert.AreEqual(reread.Rows.Count, dialog.Grid.RowCount, "the new reading's rows, not the query's");
        Assert.AreNotEqual(2, dialog.Grid.RowCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F8InTheTextViewIsHandledAndChangesNothing()
    {
        CsvViewerDialog dialog = CreateDialog("file:///data/hu.csv", Windows1250File());
        await dialog.OnInitializeAsync();
        await dialog.ToggleTextModeAsync();
        Encoding? before = dialog.Encoding;

        bool handled = await PressAsync(dialog, ConsoleKey.F8);

        Assert.IsTrue(handled, "never falls through to the panel's Delete");
        Assert.AreSame(before, dialog.Encoding);
        Assert.IsNull(dialog.SwitchTask);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F8BeforeTheFileIsReadIsHandledAndChangesNothing()
    {
        CsvViewerDialog dialog = CreateDialog("file:///data/hu.csv", Windows1250File());

        bool handled = await PressAsync(dialog, ConsoleKey.F8, shift: true);

        Assert.IsTrue(handled);
        Assert.IsNull(dialog.Encoding);
        Assert.IsNull(dialog.SwitchTask);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void F8IsListedInTheKeyHints()
    {
        CsvViewerDialog dialog = CreateDialog("file:///data/a.csv", "a\n1\n");

        Assert.IsTrue(dialog.GetKeyHints().Any(h => h.Key == "F8"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DecodeNoLongerTurnsALegacyCodePageIntoReplacementCharacters()
    {
        Assert.AreNotEqual("\uFFFD", CsvViewerDialog.Decode([0xE9]));
        Assert.IsFalse(CsvViewerDialog.Decode(Windows1250File()).Contains('\uFFFD'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThePathDecidesWhichFilesAreCsvAndWhichAreTsv()
    {
        Assert.IsTrue(CsvFileTypes.IsCsvFile("/x/a.csv"));
        Assert.IsTrue(CsvFileTypes.IsCsvFile("/x/a.TSV"));
        Assert.IsFalse(CsvFileTypes.IsCsvFile("/x/a.txt"));
    }
}
