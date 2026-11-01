using System.Diagnostics;
using System.Security.Cryptography;
using iConsole.Sqlite;
using iTerminal.Core.Services.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Test.Services.Sqlite;

/// <summary>Every database here is a temp file made for the test; the user's own iTerminal.sqlite is never touched.</summary>
[TestClass]
public class SqliteDatabaseReaderTest
{
    private string _directory = string.Empty;
    private SqliteDatabaseReader _reader = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"SqliteReaderTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _reader = new SqliteDatabaseReader();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string NewPath() => Path.Combine(_directory, $"{Guid.NewGuid():N}.db");

    private static void Execute(string path, params string[] statements)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        foreach (string statement in statements)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private string CreatePeople()
    {
        string path = NewPath();
        Execute(path,
            "CREATE TABLE t (id INTEGER, name TEXT, note TEXT, data BLOB)",
            "INSERT INTO t VALUES (1, 'a', NULL, x'0102')",
            "INSERT INTO t VALUES (2, 'b', 'x', NULL)",
            "INSERT INTO t VALUES (3, 'c', 'y', NULL)");
        return path;
    }

    [TestMethod]
    public async Task SelectReturnsTheHeadersAndTheRows()
    {
        string path = CreatePeople();

        SqlTable table = await _reader.QueryAsync(path, "SELECT id, name, note, data FROM t ORDER BY id", 100, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "id", "name", "note", "data" }, table.Headers.ToArray());
        Assert.AreEqual(3, table.Rows.Count);
        CollectionAssert.AreEqual(new[] { "1", "a", "NULL", "<blob 2 bytes>" }, table.Rows[0]);
        Assert.AreEqual("NULL", table.Rows[1][3]);
        Assert.IsFalse(table.Truncated);
    }

    [TestMethod]
    public async Task ARowCapKeepsTheFirstRowsAndSaysSo()
    {
        string path = CreatePeople();

        SqlTable table = await _reader.QueryAsync(path, "SELECT id FROM t ORDER BY id", 2, CancellationToken.None);

        Assert.AreEqual(2, table.Rows.Count);
        Assert.IsTrue(table.Truncated);
    }

    [TestMethod]
    public async Task AResultExactlyAtTheCapIsNotTruncated()
    {
        string path = CreatePeople();

        SqlTable table = await _reader.QueryAsync(path, "SELECT id FROM t", 3, CancellationToken.None);

        Assert.AreEqual(3, table.Rows.Count);
        Assert.IsFalse(table.Truncated);
    }

    [TestMethod]
    public async Task TheSchemaListsTheTables()
    {
        string path = CreatePeople();

        SqlTable table = await _reader.QueryAsync(path, "SELECT type, name FROM sqlite_master", 100, CancellationToken.None);

        Assert.IsTrue(table.Rows.Any(r => r[0] == "table" && r[1] == "t"));
    }

    [TestMethod]
    public async Task AQuotedSemicolonInAStringRuns()
    {
        string path = CreatePeople();

        SqlTable table = await _reader.QueryAsync(path, "SELECT 'a;b' AS v", 10, CancellationToken.None);

        Assert.AreEqual("a;b", table.Rows[0][0]);
    }

    [TestMethod]
    public async Task AMultiStatementInputIsRefusedAndTheTableSurvives()
    {
        string path = CreatePeople();

        await Assert.ThrowsExactlyAsync<SqlQueryException>(() =>
            _reader.QueryAsync(path, "SELECT '/*' ; DROP TABLE t; --'", 10, CancellationToken.None));

        SqlTable table = await _reader.QueryAsync(path, "SELECT count(*) FROM t", 10, CancellationToken.None);
        Assert.AreEqual("3", table.Rows[0][0]);
    }

    [TestMethod]
    public async Task AWithStatementThatWritesLeavesTheFileUnchanged()
    {
        string path = CreatePeople();
        string before = Hash(path);

        // WITH ... DELETE is valid SQLite, so the statement checker lets it through; the read-only connection is what refuses it.
        await Assert.ThrowsExactlyAsync<SqlQueryException>(() =>
            _reader.QueryAsync(path, "WITH x AS (SELECT 1) DELETE FROM t", 10, CancellationToken.None));

        Assert.AreEqual(before, Hash(path));
    }

    [TestMethod]
    public async Task APragmaThatTurnsWritesBackOnIsRefused()
    {
        string path = CreatePeople();
        string before = Hash(path);

        await Assert.ThrowsExactlyAsync<SqlQueryException>(() =>
            _reader.QueryAsync(path, "PRAGMA query_only=OFF", 10, CancellationToken.None));

        Assert.AreEqual(before, Hash(path));
    }

    [TestMethod]
    public async Task LoadingAnExtensionFails()
    {
        string path = CreatePeople();

        await Assert.ThrowsExactlyAsync<SqlQueryException>(() =>
            _reader.QueryAsync(path, "SELECT load_extension('x')", 10, CancellationToken.None));
    }

    [TestMethod]
    public async Task AMissingFileIsRefused()
    {
        await Assert.ThrowsExactlyAsync<SqlQueryException>(() =>
            _reader.QueryAsync(NewPath(), "SELECT 1", 10, CancellationToken.None));
    }

    [TestMethod]
    public async Task ANonSqliteFileIsRefused()
    {
        string path = NewPath();
        File.WriteAllText(path, "this is not a database, it is text");

        SqlQueryException failure = await Assert.ThrowsExactlyAsync<SqlQueryException>(() =>
            _reader.QueryAsync(path, "SELECT 1", 10, CancellationToken.None));

        Assert.AreEqual("Not an SQLite database", failure.Message);
    }

    [TestMethod]
    public async Task AnEmptyDbFileIsRefused()
    {
        string path = NewPath();
        File.WriteAllBytes(path, []);

        SqlQueryException failure = await Assert.ThrowsExactlyAsync<SqlQueryException>(() =>
            _reader.QueryAsync(path, "SELECT 1", 10, CancellationToken.None));

        Assert.AreEqual("Not an SQLite database", failure.Message);
    }

    [TestMethod]
    public async Task AWalDatabaseWhoseWalFileIsPresentReturnsRows()
    {
        string path = NewPath();
        // The writer stays open, so the -wal file is on disk while the reader runs.
        using var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        writer.Open();
        using (SqliteCommand mode = writer.CreateCommand())
        {
            mode.CommandText = "PRAGMA journal_mode=WAL";
            mode.ExecuteNonQuery();
        }

        using (SqliteCommand create = writer.CreateCommand())
        {
            create.CommandText = "CREATE TABLE t (id INTEGER); INSERT INTO t VALUES (1); INSERT INTO t VALUES (2);";
            create.ExecuteNonQuery();
        }

        Assert.IsTrue(File.Exists(path + "-wal"), "the writer left a -wal file for the reader to see");

        SqlTable table = await _reader.QueryAsync(path, "SELECT count(*) FROM t", 10, CancellationToken.None);

        Assert.AreEqual("2", table.Rows[0][0]);
    }

    [TestMethod]
    public async Task AWalDatabaseWithItsShmFileMissingDoesNotCrash()
    {
        // Crash guard, not a pin on one error: SQLite may recreate the -shm file or refuse, depending on the folder and the
        // build. The only contract is that the result is rows or a SqlQueryException, and nothing else escapes.
        string path = NewPath();
        using (var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString))
        {
            writer.Open();
            using SqliteCommand command = writer.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE t (id INTEGER); INSERT INTO t VALUES (1);";
            command.ExecuteNonQuery();
            File.Delete(path + "-shm");
            try
            {
                SqlTable table = await _reader.QueryAsync(path, "SELECT count(*) FROM t", 10, CancellationToken.None);
                Assert.IsNotNull(table);
            }
            catch (SqlQueryException)
            {
                // Refused cleanly: the dialog shows the message on the status line.
            }
        }
    }

    [TestMethod]
    public async Task ARunawayRecursiveQueryIsCancelledPromptly()
    {
        string path = CreatePeople();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        const string runaway = "WITH RECURSIVE c(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM c WHERE n < 1000000000) SELECT count(*) FROM c";
        var clock = Stopwatch.StartNew();

        // ThrowsAsync accepts TaskCanceledException, the subtype the awaited task reports.
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _reader.QueryAsync(path, runaway, 10, cancellation.Token));

        Assert.IsTrue(clock.ElapsedMilliseconds < 5000, $"cancelled in {clock.ElapsedMilliseconds} ms");
    }
}
