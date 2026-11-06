using iConsole.Hdf;
using iTerminal.Core.Services.Hdf;
using PureHDF;

namespace iTerminal.Core.Test.Services.Hdf;

[TestClass]
public class Hdf5FileReaderTest
{
    public enum Level : byte
    {
        Low = 1,
        High = 200,
    }

    public struct Record
    {
        public int Id;
        public double Value;
        public string Name;
    }

    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "iterminal-hdf5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static double[,,] Cube()
    {
        var cube = new double[3, 4, 5];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                for (int k = 0; k < 5; k++)
                {
                    cube[i, j, k] = (i * 100) + (j * 10) + k;
                }
            }
        }

        return cube;
    }

    private static int[,,,] Tesseract()
    {
        var tesseract = new int[2, 3, 2, 2];
        for (int a = 0; a < 2; a++)
        {
            for (int b = 0; b < 3; b++)
            {
                for (int c = 0; c < 2; c++)
                {
                    for (int d = 0; d < 2; d++)
                    {
                        tesseract[a, b, c, d] = (a * 1000) + (b * 100) + (c * 10) + d;
                    }
                }
            }
        }

        return tesseract;
    }

    /// <summary>A file with one of everything the viewer decodes, written by PureHDF's own writer.</summary>
    private string Sample()
    {
        string path = Path.Combine(_folder, "sample.h5");
        new H5File
        {
            ["answer"] = 42,
            ["vec"] = new int[] { 10, 20, 30 },
            ["matrix"] = new float[,] { { 1.5f, 2.5f }, { 3.5f, 4.5f } },
            ["cube"] = Cube(),
            ["tesseract"] = Tesseract(),
            ["names"] = new[] { "alpha", "βéta", "" },
            ["bytes"] = new byte[] { 0, 200, 255 },
            ["signed"] = new long[] { -5, long.MinValue },
            ["unsigned"] = new ulong[] { ulong.MaxValue },
            ["levels"] = new Level[] { Level.Low, Level.High },
            ["specials"] = new double[] { 1.5, double.NaN, double.PositiveInfinity },
            ["records"] = new Record[] { new() { Id = 1, Value = 2.5, Name = "x" }, new() { Id = 2, Value = 3.5, Name = "y" } },
            ["grp"] = new H5Group
            {
                ["inner"] = new H5Group { ["deep"] = new short[,] { { 7, 8, 9 } } },
                ["flat"] = new int[] { 1 },
            },
        }.Write(path);
        return path;
    }

    private static Hdf5FileReader Reader() => new();

    private static Task<HdfSlice> Slice(string path, string dataset, params long[] leading) =>
        Reader().ReadSliceAsync(path, dataset, leading, 10_000, 100, 100_000, CancellationToken.None);

    [Timeout(60_000)]
    [TestMethod]
    public async Task EveryDatasetIsListedWithItsPathShapeAndType()
    {
        HdfDatasetList list = await Reader().ListDatasetsAsync(Sample(), 5_000, CancellationToken.None);

        Assert.IsFalse(list.Truncated);
        HdfDatasetInfo Find(string path) => list.Datasets.Single(d => d.Path == path);
        CollectionAssert.AreEqual(Array.Empty<long>(), Find("/answer").Shape);
        Assert.AreEqual("int32", Find("/answer").TypeName);
        CollectionAssert.AreEqual(new long[] { 3, 4, 5 }, Find("/cube").Shape);
        Assert.AreEqual("float64", Find("/cube").TypeName);
        Assert.AreEqual("float32", Find("/matrix").TypeName);
        Assert.AreEqual("uint8", Find("/bytes").TypeName);
        Assert.AreEqual("int64", Find("/signed").TypeName);
        Assert.AreEqual("uint64", Find("/unsigned").TypeName);
        Assert.AreEqual("string", Find("/names").TypeName);
        Assert.AreEqual("compound (3 fields)", Find("/records").TypeName);
        CollectionAssert.AreEqual(new long[] { 1, 3 }, Find("/grp/inner/deep").Shape);
        Assert.IsTrue(list.Datasets.Any(d => d.Path == "/grp/flat"), "datasets in a group are listed under its path");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheListIsInPathOrderAndTheCapSaysItCutTheList()
    {
        string path = Path.Combine(_folder, "many.h5");
        var file = new H5File();
        for (int i = 0; i < 12; i++)
        {
            file[$"d{i:D2}"] = new int[] { i };
        }

        file.Write(path);

        HdfDatasetList list = await Reader().ListDatasetsAsync(path, 5, CancellationToken.None);

        Assert.IsTrue(list.Truncated);
        CollectionAssert.AreEqual(new[] { "/d00", "/d01", "/d02", "/d03", "/d04" }, list.Datasets.Select(d => d.Path).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AScalarIsOneCell()
    {
        HdfSlice slice = await Slice(Sample(), "/answer");

        CollectionAssert.AreEqual(new[] { "value" }, slice.Headers.ToArray());
        Assert.AreEqual("42", slice.Rows.Single().Single());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AVectorIsAnIndexColumnAndAValueColumn()
    {
        HdfSlice slice = await Slice(Sample(), "/vec");

        CollectionAssert.AreEqual(new[] { "#", "value" }, slice.Headers.ToArray());
        CollectionAssert.AreEqual(new[] { "0", "10" }, slice.Rows[0]);
        CollectionAssert.AreEqual(new[] { "2", "30" }, slice.Rows[2]);
        Assert.IsFalse(slice.RowsTruncated);
        Assert.IsFalse(slice.ColumnsTruncated);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMatrixHasTheColumnNumbersAsHeaders()
    {
        HdfSlice slice = await Slice(Sample(), "/matrix");

        CollectionAssert.AreEqual(new[] { "#", "0", "1" }, slice.Headers.ToArray());
        CollectionAssert.AreEqual(new[] { "0", "1.5", "2.5" }, slice.Rows[0]);
        CollectionAssert.AreEqual(new[] { "1", "3.5", "4.5" }, slice.Rows[1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(0L)]
    [DataRow(1L)]
    [DataRow(2L)]
    public async Task EachSliceOfACubeHoldsTheRightCells(long index)
    {
        HdfSlice slice = await Slice(Sample(), "/cube", index);

        Assert.AreEqual(4, slice.Rows.Count);
        CollectionAssert.AreEqual(new[] { "#", "0", "1", "2", "3", "4" }, slice.Headers.ToArray());
        for (int j = 0; j < 4; j++)
        {
            for (int k = 0; k < 5; k++)
            {
                Assert.AreEqual(((index * 100) + (j * 10) + k).ToString(), slice.Rows[j][k + 1], $"cube[{index},{j},{k}]");
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFourDimensionalDatasetIsCutByTwoLeadingIndices()
    {
        HdfSlice slice = await Slice(Sample(), "/tesseract", 1, 2);

        Assert.AreEqual(2, slice.Rows.Count);
        CollectionAssert.AreEqual(new[] { "0", "1200", "1201" }, slice.Rows[0]);
        CollectionAssert.AreEqual(new[] { "1", "1210", "1211" }, slice.Rows[1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnIndexPastTheEndIsClampedAndAMissingOneIsZero()
    {
        HdfSlice past = await Slice(Sample(), "/cube", 99);
        HdfSlice missing = await Reader().ReadSliceAsync(Sample(), "/cube", [], 10_000, 100, 100_000, CancellationToken.None);

        Assert.AreEqual("200", past.Rows[0][1], "clamped to the last index, 2");
        Assert.AreEqual("0", missing.Rows[0][1], "no index given reads index 0");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACompoundVectorHasOneColumnPerField()
    {
        HdfSlice slice = await Slice(Sample(), "/records");

        CollectionAssert.AreEqual(new[] { "#", "Id", "Value", "Name" }, slice.Headers.ToArray());
        CollectionAssert.AreEqual(new[] { "0", "1", "2.5", "x" }, slice.Rows[0]);
        CollectionAssert.AreEqual(new[] { "1", "2", "3.5", "y" }, slice.Rows[1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ValuesReadAsTextWhateverTheirType()
    {
        string path = Sample();

        HdfSlice names = await Slice(path, "/names");
        HdfSlice bytes = await Slice(path, "/bytes");
        HdfSlice signed = await Slice(path, "/signed");
        HdfSlice unsigned = await Slice(path, "/unsigned");
        HdfSlice specials = await Slice(path, "/specials");

        CollectionAssert.AreEqual(new[] { "alpha", "βéta", "" }, names.Rows.Select(r => r[1]).ToArray());
        CollectionAssert.AreEqual(new[] { "0", "200", "255" }, bytes.Rows.Select(r => r[1]).ToArray());
        CollectionAssert.AreEqual(new[] { "-5", long.MinValue.ToString() }, signed.Rows.Select(r => r[1]).ToArray());
        Assert.AreEqual(ulong.MaxValue.ToString(), unsigned.Rows[0][1]);
        CollectionAssert.AreEqual(new[] { "1.5", "NaN", "Infinity" }, specials.Rows.Select(r => r[1]).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnsignedEnumerationReadsAsItsStoredIntegerNotNegative()
    {
        string path = Sample();

        HdfDatasetList list = await Reader().ListDatasetsAsync(path, 5_000, CancellationToken.None);
        HdfSlice slice = await Slice(path, "/levels");

        Assert.AreEqual("enum", list.Datasets.Single(d => d.Path == "/levels").TypeName);
        CollectionAssert.AreEqual(new[] { "1", "200" }, slice.Rows.Select(r => r[1]).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ADatasetInANestedGroupIsFoundByItsFullPath()
    {
        HdfSlice slice = await Slice(Sample(), "/grp/inner/deep");

        CollectionAssert.AreEqual(new[] { "0", "7", "8", "9" }, slice.Rows.Single());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheRowColumnAndCellCapsCutTheSliceAndSayWhichAxis()
    {
        string path = Path.Combine(_folder, "wide.h5");
        var wide = new double[50, 30];
        for (int row = 0; row < 50; row++)
        {
            for (int column = 0; column < 30; column++)
            {
                wide[row, column] = (row * 100) + column;
            }
        }

        new H5File { ["wide"] = wide }.Write(path);

        HdfSlice rowsCut = await Reader().ReadSliceAsync(path, "/wide", [], maxRows: 7, maxColumns: 100, maxCells: 100_000, CancellationToken.None);
        HdfSlice columnsCut = await Reader().ReadSliceAsync(path, "/wide", [], maxRows: 10_000, maxColumns: 4, maxCells: 100_000, CancellationToken.None);
        HdfSlice cellsCut = await Reader().ReadSliceAsync(path, "/wide", [], maxRows: 10_000, maxColumns: 100, maxCells: 90, CancellationToken.None);

        Assert.AreEqual(7, rowsCut.Rows.Count);
        Assert.IsTrue(rowsCut.RowsTruncated);
        Assert.IsFalse(rowsCut.ColumnsTruncated);
        Assert.AreEqual(5, columnsCut.Headers.Count, "the index column and four values");
        Assert.IsTrue(columnsCut.ColumnsTruncated);
        Assert.IsFalse(columnsCut.RowsTruncated);
        Assert.AreEqual("3", cellsCut.Rows.Count.ToString(), "90 cells over 30 columns leaves three rows");
        Assert.IsTrue(cellsCut.RowsTruncated);
        Assert.AreEqual("203", rowsCut.Rows[2][4], "the cells that remain are the right ones");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnlyTheSliceIsDecodedNotTheWholeDataset()
    {
        string path = Path.Combine(_folder, "big.h5");
        var big = new double[1500, 1500];
        big[1499, 1499] = 3.25;
        new H5File { ["big"] = big }.Write(path);
        await Slice(path, "/big"); // warm up the type caches, so the measurement below sees only the read

        long before = GC.GetAllocatedBytesForCurrentThread();
        HdfSlice slice = await Reader().ReadSliceAsync(path, "/big", [], maxRows: 20, maxColumns: 10, maxCells: 200, CancellationToken.None);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(20, slice.Rows.Count);
        Assert.IsTrue(allocated < 4_000_000, $"a 20 x 10 slice of an 18 MB dataset allocated {allocated:N0} bytes");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileThatIsNotHdf5IsRefused()
    {
        string path = Path.Combine(_folder, "fake.h5");
        File.WriteAllText(path, "this is not an HDF5 file at all");

        HdfReadException list = await Assert.ThrowsExactlyAsync<HdfReadException>(() => Reader().ListDatasetsAsync(path, 10, CancellationToken.None));
        HdfReadException slice = await Assert.ThrowsExactlyAsync<HdfReadException>(() => Slice(path, "/x"));

        Assert.AreEqual("Not an HDF5 file", list.Message);
        Assert.AreEqual("Not an HDF5 file", slice.Message);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AZeroByteFileAndAMissingFileAreRefusedWithAMessage()
    {
        string empty = Path.Combine(_folder, "empty.h5");
        File.WriteAllBytes(empty, []);

        HdfReadException emptyFailure = await Assert.ThrowsExactlyAsync<HdfReadException>(() => Reader().ListDatasetsAsync(empty, 10, CancellationToken.None));
        HdfReadException missingFailure = await Assert.ThrowsExactlyAsync<HdfReadException>(() => Reader().ListDatasetsAsync(Path.Combine(_folder, "gone.h5"), 10, CancellationToken.None));

        Assert.AreEqual("Not an HDF5 file", emptyFailure.Message);
        Assert.AreEqual("The file was not found", missingFailure.Message);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMissingDatasetSaysSo()
    {
        HdfReadException failure = await Assert.ThrowsExactlyAsync<HdfReadException>(() => Slice(Sample(), "/nope"));

        StringAssert.Contains(failure.Message, "/nope");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACancelledTokenStopsTheReadWithoutAnError()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        string path = Sample();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Reader().ListDatasetsAsync(path, 10, cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => Reader().ReadSliceAsync(path, "/cube", [0], 10, 10, 100, cancellation.Token));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadingNeverChangesTheFile()
    {
        string path = Sample();
        byte[] before = await File.ReadAllBytesAsync(path);
        DateTime written = File.GetLastWriteTimeUtc(path);

        await Reader().ListDatasetsAsync(path, 5_000, CancellationToken.None);
        await Slice(path, "/cube", 1);
        await Slice(path, "/records");

        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path));
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(path));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheFileIsClosedAfterEachCallSoItCanBeDeleted()
    {
        string path = Sample();
        await Reader().ListDatasetsAsync(path, 5_000, CancellationToken.None);
        await Slice(path, "/cube", 1);

        File.Delete(path);

        Assert.IsFalse(File.Exists(path));
    }
}
