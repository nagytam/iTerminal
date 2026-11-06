using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Hdf;
using iConsole.Services;

namespace iConsole.Test.Dialogs;

[TestClass]
public class HdfViewerDialogTest
{
    private static readonly HdfDatasetInfo Vector = new("/vec", [3], "int32");
    private static readonly HdfDatasetInfo Matrix = new("/grp/matrix", [2, 2], "float32");
    private static readonly HdfDatasetInfo Cube = new("/cube", [3, 4, 5], "float64");
    private static readonly HdfDatasetInfo Tesseract = new("/tess", [5, 3, 4, 5], "float64");

    private sealed class FakeReader : IHdfFileReader
    {
        public Func<HdfDatasetList> List { get; set; } = () => new HdfDatasetList([Cube, Matrix, Vector], false);

        public Func<string, long[], CancellationToken, Task<HdfSlice>> Slice { get; set; } =
            (_, indices, _) => Task.FromResult(new HdfSlice(["#", "0"], [["0", "cell " + string.Join("-", indices)]], false, false));

        public List<(string Dataset, long[] Indices)> Reads { get; } = [];

        public Task<HdfDatasetList> ListDatasetsAsync(string path, int maxDatasets, CancellationToken cancellationToken) =>
            Task.FromResult(List());

        public Task<HdfSlice> ReadSliceAsync(string path, string datasetPath, long[] leadingIndices, int maxRows, int maxColumns, int maxCells, CancellationToken cancellationToken)
        {
            Reads.Add((datasetPath, [.. leadingIndices]));
            return Slice(datasetPath, leadingIndices, cancellationToken);
        }
    }

    private static HdfViewerDialog CreateDialog(FakeReader reader) =>
        new(reader, new ThemeService()) { Url = new Url("file:///data/sample.h5") };

    private static async Task LandListAsync(HdfViewerDialog dialog)
    {
        await dialog.ListTask!;
        dialog.OnIdle();
    }

    /// <summary>Waits for the running slice, then lets the dialog take it on an idle pass, as the UI does.</summary>
    private static async Task LandSliceAsync(HdfViewerDialog dialog)
    {
        try
        {
            await dialog.SliceTask!;
        }
        catch
        {
            // A failed or cancelled read is handled by OnIdle, which is what is under test.
        }

        dialog.OnIdle();
    }

    private static async Task<HdfViewerDialog> OpenAsync(FakeReader reader)
    {
        HdfViewerDialog dialog = CreateDialog(reader);
        await dialog.OnInitializeAsync();
        await LandListAsync(dialog);
        await LandSliceAsync(dialog);
        return dialog;
    }

    private static KeyEvent Key(char c, ConsoleKey key, bool shift = false, bool alt = false, bool control = false) =>
        new(c, key, shift, alt, control);

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpensOnTheFirstDatasetAndShowsItsSlice()
    {
        var reader = new FakeReader();

        HdfViewerDialog dialog = await OpenAsync(reader);

        Assert.AreEqual(3, dialog.Picker.Items.Count);
        Assert.AreEqual("/cube", dialog.CurrentDataset!.Path);
        Assert.AreEqual("cell 0", dialog.Grid.CellAt(0, 1));
        Assert.AreEqual("/cube  float64 [3 × 4 × 5]", dialog.Status);
        CollectionAssert.AreEqual(new long[] { 0 }, dialog.LeadingIndices.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADatasetOfTwoDimensionsHasNoSliceLineAndNoLeadingIndices()
    {
        var reader = new FakeReader { List = () => new HdfDatasetList([Matrix], false) };

        HdfViewerDialog dialog = await OpenAsync(reader);

        Assert.AreEqual(string.Empty, dialog.SliceText);
        Assert.AreEqual(0, dialog.LeadingIndices.Count);
        Assert.AreEqual("/grp/matrix  float32 [2 × 2]", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AScalarSaysSoOnTheStatusLine()
    {
        var reader = new FakeReader { List = () => new HdfDatasetList([new HdfDatasetInfo("/answer", [], "int32")], false) };

        HdfViewerDialog dialog = await OpenAsync(reader);

        Assert.AreEqual("/answer  int32 [scalar]", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ThePlusKeyStepsTheIndexAndReadsTheNewSlice()
    {
        var reader = new FakeReader();
        HdfViewerDialog dialog = await OpenAsync(reader);

        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key('+', ConsoleKey.OemPlus, shift: true)));
        await LandSliceAsync(dialog);

        CollectionAssert.AreEqual(new long[] { 1 }, dialog.LeadingIndices.ToArray());
        Assert.AreEqual("cell 1", dialog.Grid.CellAt(0, 1));
        Assert.AreEqual("/cube", reader.Reads[^1].Dataset);
        CollectionAssert.AreEqual(new long[] { 1 }, reader.Reads[^1].Indices);
        StringAssert.Contains(dialog.SliceText, "[<1>, *, *]");
        StringAssert.Contains(dialog.SliceText, "index 2 of 3");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheIndexStopsAtBothEndsOfTheDimension()
    {
        var reader = new FakeReader();
        HdfViewerDialog dialog = await OpenAsync(reader);
        int reads = reader.Reads.Count;

        dialog.StepIndex(-1);
        Assert.AreEqual(reads, reader.Reads.Count, "already at 0: nothing to read");

        dialog.StepIndex(10);
        Assert.AreEqual(2, dialog.LeadingIndices[0], "a step past the end stops at the last index");
        await LandSliceAsync(dialog);
        reads = reader.Reads.Count;

        dialog.StepIndex(1);
        Assert.AreEqual(reads, reader.Reads.Count, "already at the last index: nothing to read");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheKeysAreMatchedOnTheirCharacterSoEveryLayoutWorks()
    {
        var reader = new FakeReader { List = () => new HdfDatasetList([Tesseract], false) };
        HdfViewerDialog dialog = await OpenAsync(reader);

        // '=' and the numpad add key step the index up, the numpad subtract key and '-' step it down.
        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key('=', ConsoleKey.OemPlus)));
        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key('+', ConsoleKey.Add)));
        Assert.AreEqual(2, dialog.LeadingIndices[0]);
        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key('-', ConsoleKey.OemMinus)));
        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key('-', ConsoleKey.Subtract)));
        Assert.AreEqual(0, dialog.LeadingIndices[0]);

        // ']' as AltGr+key on a Hungarian or German layout arrives as Ctrl+Alt; it still chooses the next dimension.
        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key(']', ConsoleKey.Oem6, alt: true, control: true)));
        Assert.AreEqual(1, dialog.ActiveDimension);
        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key('[', ConsoleKey.Oem4, alt: true, control: true)));
        Assert.AreEqual(0, dialog.ActiveDimension);

        // A real Ctrl+Alt chord carries no printable character and is not taken.
        Assert.IsFalse(await dialog.HandleKeyEventAsync(Key('\0', ConsoleKey.Oem6, alt: true, control: true)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ControlArrowsWorkWithoutAnyTypingKey()
    {
        var reader = new FakeReader { List = () => new HdfDatasetList([Tesseract], false) };
        HdfViewerDialog dialog = await OpenAsync(reader);

        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key('\0', ConsoleKey.RightArrow, control: true)));
        Assert.AreEqual(1, dialog.ActiveDimension);
        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key('\0', ConsoleKey.UpArrow, control: true)));
        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key('\0', ConsoleKey.UpArrow, control: true)));
        CollectionAssert.AreEqual(new long[] { 0, 2 }, dialog.LeadingIndices.ToArray());
        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key('\0', ConsoleKey.DownArrow, control: true)));
        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key('\0', ConsoleKey.LeftArrow, control: true)));
        CollectionAssert.AreEqual(new long[] { 0, 1 }, dialog.LeadingIndices.ToArray());
        Assert.AreEqual(0, dialog.ActiveDimension);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheDimensionChoiceStopsAtTheLeadingDimensions()
    {
        var reader = new FakeReader { List = () => new HdfDatasetList([Tesseract], false) };
        HdfViewerDialog dialog = await OpenAsync(reader);
        int reads = reader.Reads.Count;

        dialog.StepDimension(-1);
        Assert.AreEqual(0, dialog.ActiveDimension);
        dialog.StepDimension(5);
        Assert.AreEqual(1, dialog.ActiveDimension, "a tesseract has two leading dimensions");
        Assert.AreEqual(reads, reader.Reads.Count, "choosing a dimension reads nothing");
        StringAssert.Contains(dialog.SliceText, "dimension 2 of 2");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheSliceKeysDoNothingForADatasetOfTwoDimensions()
    {
        var reader = new FakeReader { List = () => new HdfDatasetList([Matrix], false) };
        HdfViewerDialog dialog = await OpenAsync(reader);
        int reads = reader.Reads.Count;

        await dialog.HandleKeyEventAsync(Key('+', ConsoleKey.OemPlus, shift: true));
        await dialog.HandleKeyEventAsync(Key('\0', ConsoleKey.UpArrow, control: true));

        Assert.AreEqual(reads, reader.Reads.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ChoosingAnotherDatasetStartsAtTheFirstSlice()
    {
        var reader = new FakeReader();
        HdfViewerDialog dialog = await OpenAsync(reader);
        dialog.StepIndex(2);
        await LandSliceAsync(dialog);

        dialog.Picker.SelectedIndex = 2;
        await LandSliceAsync(dialog);

        Assert.AreEqual("/vec", dialog.CurrentDataset!.Path);
        Assert.AreEqual(0, dialog.LeadingIndices.Count);
        Assert.AreEqual("/vec", reader.Reads[^1].Dataset);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnlyTheNewestSliceLandsWhenTheSelectionChangesQuickly()
    {
        var reader = new FakeReader();
        HdfViewerDialog dialog = await OpenAsync(reader);
        var slow = new TaskCompletionSource<HdfSlice>();
        reader.Slice = (dataset, _, _) => dataset == "/vec" ? slow.Task : Task.FromResult(new HdfSlice(["#", "0"], [["0", "from " + dataset]], false, false));

        dialog.Picker.SelectedIndex = 2; // /vec: slow
        dialog.Picker.SelectedIndex = 1; // /grp/matrix: fast
        dialog.Picker.SelectedIndex = 0; // /cube: fast, the newest
        await LandSliceAsync(dialog);
        slow.SetResult(new HdfSlice(["#", "0"], [["0", "stale"]], false, false));
        dialog.OnIdle();

        Assert.AreEqual("from /cube", dialog.Grid.CellAt(0, 1));
        Assert.AreEqual("/cube  float64 [3 × 4 × 5]", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedReadShowsItsMessageAndKeepsTheGridOnScreen()
    {
        var reader = new FakeReader();
        HdfViewerDialog dialog = await OpenAsync(reader);

        reader.Slice = (_, _, _) => Task.FromException<HdfSlice>(new HdfReadException("The dataset could not be read: boom"));
        dialog.StepIndex(1);
        await LandSliceAsync(dialog);

        Assert.AreEqual("The dataset could not be read: boom", dialog.Status);
        Assert.AreEqual("cell 0", dialog.Grid.CellAt(0, 1), "the cells of the last good slice stay on screen");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnexpectedFailureIsReportedWithoutCrashingTheDialog()
    {
        var reader = new FakeReader { List = () => throw new InvalidOperationException("surprise") };
        HdfViewerDialog dialog = CreateDialog(reader);
        await dialog.OnInitializeAsync();

        try
        {
            await dialog.ListTask!;
        }
        catch
        {
            // OnIdle reports it.
        }

        dialog.OnIdle();

        Assert.AreEqual("The file could not be read: surprise", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileThatIsNotHdf5ShowsTheReadersMessage()
    {
        var reader = new FakeReader { List = () => throw new HdfReadException("Not an HDF5 file") };
        HdfViewerDialog dialog = CreateDialog(reader);
        await dialog.OnInitializeAsync();
        try
        {
            await dialog.ListTask!;
        }
        catch
        {
            // OnIdle reports it.
        }

        dialog.OnIdle();

        Assert.AreEqual("Not an HDF5 file", dialog.Status);
        Assert.IsNull(dialog.CurrentDataset);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileWithoutDatasetsSaysSo()
    {
        var reader = new FakeReader { List = () => new HdfDatasetList([], false) };

        HdfViewerDialog dialog = CreateDialog(reader);
        await dialog.OnInitializeAsync();
        await LandListAsync(dialog);

        Assert.AreEqual("This file has no datasets", dialog.Status);
        Assert.IsNull(dialog.SliceTask, "no dataset, nothing to read");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheStatusLineSaysWhichAxesWereCutAndThatTheListWasCut()
    {
        var reader = new FakeReader
        {
            List = () => new HdfDatasetList([Matrix], Truncated: true),
            Slice = (_, _, _) => Task.FromResult(new HdfSlice(["#", "0", "1"], [["0", "a", "b"], ["1", "c", "d"], ["2", "e", "f"]], RowsTruncated: true, ColumnsTruncated: true)),
        };

        HdfViewerDialog dialog = await OpenAsync(reader);

        Assert.AreEqual(
            "/grp/matrix  float32 [2 × 2]; only the first 3 rows are shown; only the first 2 columns are shown; the list holds only the first 5,000 datasets",
            dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CloseCancelsTheRunningReadAndWritesNothing()
    {
        CancellationToken observed = default;
        var reader = new FakeReader();
        HdfViewerDialog dialog = await OpenAsync(reader);
        reader.Slice = (_, _, token) =>
        {
            observed = token;
            return Task.Delay(Timeout.Infinite, token).ContinueWith(_ => new HdfSlice([], [], false, false), TaskScheduler.Default);
        };
        dialog.StepIndex(1);
        string statusBeforeClose = dialog.Status;

        dialog.Close();
        await Task.Delay(50);
        dialog.OnIdle();

        Assert.IsTrue(observed.IsCancellationRequested, "the running read was told to stop");
        Assert.AreEqual(statusBeforeClose, dialog.Status, "a cancelled read writes nothing to the status line");
        Assert.IsNull(dialog.SliceTask);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpeningAnotherFileInTheSameDialogStartsOnItsFirstDataset()
    {
        var reader = new FakeReader();
        HdfViewerDialog dialog = await OpenAsync(reader);
        dialog.Picker.SelectedIndex = 2;
        await LandSliceAsync(dialog);
        Assert.AreEqual("/vec", dialog.CurrentDataset!.Path);

        // A panel reuses one dialog: ShowAsync runs OnInitializeAsync again for the next file.
        dialog.Url = new Url("file:///data/other.h5");
        await dialog.OnInitializeAsync();
        Assert.IsNull(dialog.CurrentDataset, "nothing of the last file is left while the new list loads");
        Assert.AreEqual(0, dialog.Picker.Items.Count);
        await LandListAsync(dialog);
        await LandSliceAsync(dialog);

        Assert.AreEqual("/cube", dialog.CurrentDataset!.Path);
        Assert.AreEqual(0, dialog.Picker.SelectedIndex);
        Assert.AreEqual("/cube  float64 [3 × 4 × 5]", dialog.Status);
        CollectionAssert.AreEqual(new long[] { 0 }, dialog.LeadingIndices.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutAFileThereIsNothingToShow()
    {
        var dialog = new HdfViewerDialog(new FakeReader(), new ThemeService());

        await dialog.OnInitializeAsync();

        Assert.AreEqual("Nothing to show", dialog.Status);
        Assert.IsNull(dialog.ListTask);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ThePickerHoldsAThousandsOfDatasetsWithinTheBudget()
    {
        HdfDatasetInfo[] many = [.. Enumerable.Range(0, HdfViewerDialog.MaxDatasets).Select(i => new HdfDatasetInfo($"/group{i / 100}/data{i}", [4], "int32"))];
        var reader = new FakeReader { List = () => new HdfDatasetList(many, true) };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        HdfViewerDialog dialog = await OpenAsync(reader);
        dialog.Picker.SelectedIndex = many.Length - 1;
        await LandSliceAsync(dialog);
        watch.Stop();

        Assert.AreEqual(many.Length, dialog.Picker.Items.Count);
        Assert.AreEqual(many[^1].Path, dialog.CurrentDataset!.Path);
        Assert.IsTrue(watch.ElapsedMilliseconds < 3_000, $"opening and choosing took {watch.ElapsedMilliseconds} ms");
    }
}
