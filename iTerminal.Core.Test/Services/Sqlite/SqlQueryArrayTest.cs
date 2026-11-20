using iConsole.Sqlite;
using iConsole.Tabular;
using iTerminal.Core.Services.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Test.Services.Sqlite;

/// <summary>A statement's result read as a two-axis array through the real read-only reader. Every database is a temp file.</summary>
[TestClass]
public class SqlQueryArrayTest
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"SqlQueryArray_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "data.db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE t (id INTEGER, label TEXT); INSERT INTO t VALUES (1,'a'),(2,'b'),(3,'c'),(4,'d'),(5,'e');";
        command.ExecuteNonQuery();
        _path = path;
    }

    private string _path = string.Empty;

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task TheShapeIsTheRowCountAndTheColumnNames()
    {
        var array = new SqlQueryArray(new SqliteDatabaseReader(), _path, "SELECT id, label FROM t ORDER BY id");

        await array.InitializeAsync(CancellationToken.None);

        CollectionAssert.AreEqual(new long[] { 5, 2 }, array.Shape.ToArray());
        CollectionAssert.AreEqual(new[] { "id", "label" }, array.ColumnNames.ToArray());
    }

    [TestMethod]
    public async Task ABlockIsReadWithLimitAndOffsetAndOnlyItsRows()
    {
        var array = new SqlQueryArray(new SqliteDatabaseReader(), _path, "SELECT id, label FROM t ORDER BY id");
        await array.InitializeAsync(CancellationToken.None);

        IReadOnlyList<string> cells = await array.ReadBlockAsync([2, 0], [2, 2], CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "3", "c", "4", "d" }, cells.ToArray(), "rows 2 and 3, read with LIMIT 2 OFFSET 2");
    }

    [TestMethod]
    public async Task AWindowOfTheGridReadsThroughTheArrayAndIsCut()
    {
        var array = new SqlQueryArray(new SqliteDatabaseReader(), _path, "WITH s AS (SELECT id, label FROM t) SELECT * FROM s ORDER BY id DESC");
        await array.InitializeAsync(CancellationToken.None);

        GridTable window = await GridArrayView.Open(array).MaterializeAsync(maxRows: 2, maxColumns: 10, maxCells: 100, CancellationToken.None, withRowNumbers: false);

        Assert.AreEqual("5", window.Rows[0][0], "the first row of the descending order");
        Assert.AreEqual(2, window.Rows.Count);
        Assert.IsTrue(window.RowsTruncated, "five rows, two shown");
    }

    [TestMethod]
    public async Task AStatementThatIsNotASelectIsRefusedByTheReader()
    {
        var array = new SqlQueryArray(new SqliteDatabaseReader(), _path, "DELETE FROM t");

        await Assert.ThrowsExactlyAsync<iConsole.Sqlite.SqlQueryException>(() => array.InitializeAsync(CancellationToken.None));
    }
}
