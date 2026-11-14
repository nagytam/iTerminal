using iConsole.Parquet;
using iTerminal.Core.Services.ParquetFiles;
using Parquet;
using Parquet.Serialization;

namespace iTerminal.Core.Test.Services.ParquetFiles;

[TestClass]
public class ParquetFileReaderTest
{
    public sealed class Row
    {
        public long Id { get; set; }

        public string? Name { get; set; }

        public double? Score { get; set; }

        public bool Active { get; set; }

        public DateTime Born { get; set; }
    }

    public sealed class Tagged
    {
        public long Id { get; set; }

        public List<string> Tags { get; set; } = [];
    }

    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "iterminal-parquet-" + Guid.NewGuid().ToString("N"));
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

    private static IEnumerable<Row> SampleRows(int count) => Enumerable.Range(1, count).Select(i => new Row
    {
        Id = i,
        Name = i == 2 ? null : $"name {i}",
        Score = i == 3 ? null : i * 1.5,
        Active = i % 2 == 1,
        Born = new DateTime(2024, 1, i, 0, 0, 0, DateTimeKind.Unspecified),
    });

    private async Task<string> WriteAsync<T>(IEnumerable<T> rows, string name, ParquetOptions? options = null)
    {
        string path = Path.Combine(_folder, name);
        await ParquetSerializer.SerializeAsync(rows, path, options ?? new ParquetOptions());
        return path;
    }

    private static ParquetFileReader NewReader() => new();

    [TestMethod]
    [Timeout(60_000)]
    public async Task ReadsTheSchemaInTheHeadersAndTheValuesAsText()
    {
        string path = await WriteAsync(SampleRows(3), "typed.parquet");

        ParquetPreview preview = await NewReader().ReadPreviewAsync(path, 100, 100, 10_000, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "Id (int64)", "Name (string)", "Score (float64)", "Active (boolean)", "Born (datetime)" },
            preview.Headers.ToArray());
        Assert.AreEqual(3, preview.TotalRows);
        Assert.AreEqual(5, preview.TotalColumns);
        Assert.IsFalse(preview.RowsTruncated);
        Assert.IsFalse(preview.ColumnsTruncated);
        CollectionAssert.AreEqual(new[] { "1", "name 1", "1.5", "True", "2024-01-01" }, preview.Rows[0]);
        // A null is an empty cell, in a string and in a number alike.
        CollectionAssert.AreEqual(new[] { "2", "", "3", "False", "2024-01-02" }, preview.Rows[1]);
        CollectionAssert.AreEqual(new[] { "3", "name 3", "", "True", "2024-01-03" }, preview.Rows[2]);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ReadsEveryRowGroupInOrder()
    {
        string path = await WriteAsync(SampleRows(5), "groups.parquet", new ParquetOptions { RowGroupSize = 2 });

        ParquetPreview preview = await NewReader().ReadPreviewAsync(path, 100, 100, 10_000, CancellationToken.None);

        Assert.AreEqual(5, preview.Rows.Count);
        CollectionAssert.AreEqual(new[] { "1", "2", "3", "4", "5" }, preview.Rows.Select(row => row[0]).ToArray());
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task TheRowLimitCutsTheRowsAndSaysSo()
    {
        string path = await WriteAsync(SampleRows(5), "rows.parquet", new ParquetOptions { RowGroupSize = 2 });

        ParquetPreview preview = await NewReader().ReadPreviewAsync(path, 3, 100, 10_000, CancellationToken.None);

        Assert.AreEqual(3, preview.Rows.Count);
        Assert.AreEqual(5, preview.TotalRows);
        Assert.IsTrue(preview.RowsTruncated);
        Assert.AreEqual("3", preview.Rows[2][0]);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task TheCellLimitCutsTheRowsBeforeTheColumns()
    {
        string path = await WriteAsync(SampleRows(5), "cells.parquet");

        // Five columns and a budget of 10 cells keep two rows, and all five columns.
        ParquetPreview preview = await NewReader().ReadPreviewAsync(path, 100, 100, 10, CancellationToken.None);

        Assert.AreEqual(2, preview.Rows.Count);
        Assert.AreEqual(5, preview.Headers.Count);
        Assert.IsFalse(preview.ColumnsTruncated);
        Assert.IsTrue(preview.RowsTruncated);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task TheColumnLimitCutsTheColumnsAndSaysSo()
    {
        string path = await WriteAsync(SampleRows(2), "columns.parquet");

        ParquetPreview preview = await NewReader().ReadPreviewAsync(path, 100, 2, 10_000, CancellationToken.None);

        Assert.AreEqual(2, preview.Headers.Count);
        Assert.AreEqual(2, preview.Rows[0].Length);
        Assert.AreEqual(5, preview.TotalColumns);
        Assert.IsTrue(preview.ColumnsTruncated);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ANestedColumnShowsAPlaceholderInEveryRow()
    {
        string path = await WriteAsync(
            new[] { new Tagged { Id = 1, Tags = ["a", "b"] }, new Tagged { Id = 2 } },
            "nested.parquet");

        ParquetPreview preview = await NewReader().ReadPreviewAsync(path, 100, 100, 10_000, CancellationToken.None);

        Assert.AreEqual("Tags (list)", preview.Headers[1]);
        Assert.AreEqual(ParquetFileReader.NestedCell, preview.Rows[0][1]);
        Assert.AreEqual(ParquetFileReader.NestedCell, preview.Rows[1][1]);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AFileWithNoRowsHasItsSchemaAndNoRows()
    {
        string path = await WriteAsync(Array.Empty<Row>(), "empty.parquet");

        ParquetPreview preview = await NewReader().ReadPreviewAsync(path, 100, 100, 10_000, CancellationToken.None);

        Assert.AreEqual(0, preview.Rows.Count);
        Assert.AreEqual(0, preview.TotalRows);
        Assert.AreEqual(5, preview.Headers.Count);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ANonParquetFileIsRefused()
    {
        string path = Path.Combine(_folder, "text.parquet");
        await File.WriteAllTextAsync(path, "name,value\na,1\n");

        var error = await Assert.ThrowsAsync<ParquetReadException>(() => NewReader().ReadPreviewAsync(path, 100, 100, 10_000, CancellationToken.None));

        StringAssert.StartsWith(error.Message, "The file could not be read as Parquet:");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AMissingFileIsRefused()
    {
        string path = Path.Combine(_folder, "missing.parquet");

        var error = await Assert.ThrowsAsync<ParquetReadException>(() => NewReader().ReadPreviewAsync(path, 100, 100, 10_000, CancellationToken.None));

        Assert.AreEqual("The file was not found", error.Message);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ACancelledReadIsNotWrappedAsAFileError()
    {
        string path = await WriteAsync(SampleRows(2), "cancel.parquet");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => NewReader().ReadPreviewAsync(path, 100, 100, 10_000, cancellation.Token));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task TheExampleFileInTheTestFilesIsRead()
    {
        // Test/FileTypes/example.parquet: written by Apache Arrow (pyarrow), so the reader is checked against another writer.
        string? root = FindRepositoryRoot();
        Assert.IsNotNull(root, "the repository root holds iTerminal.slnx");
        string path = Path.Combine(root, "Test", "FileTypes", "example.parquet");

        ParquetPreview preview = await NewReader().ReadPreviewAsync(path, 100, 100, 10_000, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "id (int64)", "name (string)", "score (float64)", "active (boolean)", "born (datetime)", "tags (list)" },
            preview.Headers.ToArray());
        Assert.AreEqual(5, preview.TotalRows);
        CollectionAssert.AreEqual(new[] { "1", "Alpha", "1.5", "True", "2024-01-15", ParquetFileReader.NestedCell }, preview.Rows[0]);
        Assert.AreEqual(string.Empty, preview.Rows[2][1]);
        Assert.AreEqual("Delta \"quoted\", with comma", preview.Rows[3][1]);
        Assert.AreEqual(string.Empty, preview.Rows[4][2]);
    }

    private static string? FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "iTerminal.slnx")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
