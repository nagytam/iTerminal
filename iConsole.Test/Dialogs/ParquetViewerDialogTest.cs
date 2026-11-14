using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Parquet;
using iConsole.Services;

namespace iConsole.Test.Dialogs;

[TestClass]
public class ParquetViewerDialogTest
{
    private static readonly ParquetPreview Sample = new(
        ["id (int64)", "name (string)"],
        [["1", "Alpha"], ["2", ""]],
        TotalRows: 2,
        TotalColumns: 2);

    private sealed class FakeReader : IParquetFileReader
    {
        public Func<ParquetPreview> Preview { get; set; } = () => Sample;

        /// <summary>When set, the read waits for it before it returns, so a test can close the dialog while the read runs.</summary>
        public Task? Gate { get; set; }

        public string? LastPath { get; private set; }

        public int LastMaxRows { get; private set; }

        public int LastMaxColumns { get; private set; }

        public int LastMaxCells { get; private set; }

        public Task<ParquetPreview> ReadPreviewAsync(string path, int maxRows, int maxColumns, int maxCells, CancellationToken cancellationToken)
        {
            LastPath = path;
            LastMaxRows = maxRows;
            LastMaxColumns = maxColumns;
            LastMaxCells = maxCells;
            return ReadAsync();
        }

        private async Task<ParquetPreview> ReadAsync()
        {
            if (Gate is { } gate)
            {
                await gate;
            }

            return Preview();
        }
    }

    private static ParquetViewerDialog CreateDialog(IParquetFileReader reader) =>
        new(reader, new ThemeService()) { Url = new Url("file:///data/sample.parquet") };

    /// <summary>Waits for the running read, then lets the dialog take it on an idle pass, as the UI does.</summary>
    private static async Task LandAsync(ParquetViewerDialog dialog)
    {
        try
        {
            await dialog.ReadTask!;
        }
        catch
        {
            // A failed or cancelled read is handled by OnIdle, which is what is under test.
        }

        dialog.OnIdle();
    }

    [TestMethod]
    public async Task OpensTheFirstRowsWithTheSchemaInTheHeaders()
    {
        var reader = new FakeReader();
        ParquetViewerDialog dialog = CreateDialog(reader);

        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        Assert.AreEqual("/data/sample.parquet", reader.LastPath);
        Assert.AreEqual(ParquetViewerDialog.MaxRows, reader.LastMaxRows);
        Assert.AreEqual(ParquetViewerDialog.MaxColumns, reader.LastMaxColumns);
        Assert.AreEqual(ParquetViewerDialog.MaxCells, reader.LastMaxCells);
        Assert.AreEqual("1", dialog.Grid.CellAt(0, 0));
        Assert.AreEqual("Alpha", dialog.Grid.CellAt(0, 1));
        Assert.AreEqual(string.Empty, dialog.Grid.CellAt(1, 1));
        Assert.AreEqual("2 rows, 2 columns", dialog.Status);
    }

    [TestMethod]
    public async Task AFileWithMoreRowsOrColumnsThanTheGridKeepsSaysSo()
    {
        var reader = new FakeReader
        {
            Preview = () => new ParquetPreview(["id (int64)"], [["1"], ["2"]], TotalRows: 50_000, TotalColumns: 3),
        };
        ParquetViewerDialog dialog = CreateDialog(reader);

        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        Assert.AreEqual(
            "50,000 rows, 3 columns; only the first 2 rows are shown; only the first 1 columns are shown",
            dialog.Status);
    }

    [TestMethod]
    public async Task AnEmptyFileHasNoRowsAndSaysSo()
    {
        var reader = new FakeReader
        {
            Preview = () => new ParquetPreview(["id (int64)"], [], TotalRows: 0, TotalColumns: 1),
        };
        ParquetViewerDialog dialog = CreateDialog(reader);

        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        Assert.AreEqual("0 rows, 1 columns", dialog.Status);
        Assert.AreEqual(0, dialog.Grid.RowCount);
    }

    [TestMethod]
    public async Task AFailedReadSaysWhyAndKeepsTheGridEmpty()
    {
        var reader = new FakeReader
        {
            Preview = () => throw new ParquetReadException("The file could not be read as Parquet: bad footer"),
        };
        ParquetViewerDialog dialog = CreateDialog(reader);

        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        Assert.AreEqual("The file could not be read as Parquet: bad footer", dialog.Status);
        Assert.AreEqual(0, dialog.Grid.RowCount);
    }

    [TestMethod]
    public async Task AnUnexpectedFailureSaysThatTheFileCouldNotBeRead()
    {
        var reader = new FakeReader { Preview = () => throw new InvalidOperationException("boom") };
        ParquetViewerDialog dialog = CreateDialog(reader);

        await dialog.OnInitializeAsync();
        try
        {
            await dialog.ReadTask!;
        }
        catch (InvalidOperationException)
        {
            // Handled by OnIdle below.
        }

        dialog.OnIdle();

        Assert.AreEqual("The file could not be read: boom", dialog.Status);
    }

    [TestMethod]
    public async Task WithNoUrlItSaysThereIsNothingToShow()
    {
        var dialog = new ParquetViewerDialog(new FakeReader(), new ThemeService());

        await dialog.OnInitializeAsync();

        Assert.IsNull(dialog.ReadTask);
        Assert.AreEqual("Nothing to show", dialog.Status);
    }

    [TestMethod]
    public async Task CloseCancelsTheReadAndItLandsNowhere()
    {
        var gate = new TaskCompletionSource();
        var reader = new FakeReader { Gate = gate.Task };
        ParquetViewerDialog dialog = CreateDialog(reader);

        await dialog.OnInitializeAsync();
        Assert.IsNotNull(dialog.ReadTask, "the read is running");
        dialog.Close();
        gate.SetResult();

        Assert.IsNull(dialog.ReadTask);
        dialog.OnIdle();
        Assert.AreEqual(0, dialog.Grid.RowCount);
        Assert.AreEqual(string.Empty, dialog.Status);
    }

    [TestMethod]
    public async Task OpeningAnotherFileResetsTheGridAndTheStatusLine()
    {
        var reader = new FakeReader();
        ParquetViewerDialog dialog = CreateDialog(reader);
        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        dialog.Url = new Url("file:///data/other.parquet");
        await dialog.OnInitializeAsync();

        Assert.AreEqual(string.Empty, dialog.Status);
        Assert.AreEqual(0, dialog.Grid.RowCount);
        await LandAsync(dialog);
        Assert.AreEqual("/data/other.parquet", reader.LastPath);
    }
}
