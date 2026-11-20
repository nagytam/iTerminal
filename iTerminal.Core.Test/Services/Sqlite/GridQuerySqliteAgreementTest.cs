using iConsole.Sqlite;
using iConsole.Tabular;
using iTerminal.Core.Services.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Test.Services.Sqlite;

/// <summary>
/// The same grid query, run in memory by the file viewers and rendered as SQL for the SQLite viewer, must return the same rows.
/// Every database is a temp file made for the test.
/// </summary>
[TestClass]
public class GridQuerySqliteAgreementTest
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"GridQueryAgreement_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
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

    /// <summary>The table: an INTEGER column for the numbers, so SQLite compares them as numbers as the in-memory engine does.</summary>
    private string CreateDatabase()
    {
        string path = Path.Combine(_directory, "agreement.db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE sales (name TEXT, amount INTEGER, city TEXT);
            INSERT INTO sales VALUES ('Anna', 5, 'Pécs');
            INSERT INTO sales VALUES ('Bela', 20, 'Győr');
            INSERT INTO sales VALUES ('Cecil', NULL, 'Pécs');
            INSERT INTO sales VALUES ('Dora', 100, 'O''Fallon');
            """;
        command.ExecuteNonQuery();
        return path;
    }

    /// <summary>The same four rows as the table, in the text form the file viewers hold: a NULL is an empty cell.</summary>
    private static readonly string[] Headers = ["name", "amount", "city"];

    private static readonly string[][] Rows =
    [
        ["Anna", "5", "Pécs"],
        ["Bela", "20", "Győr"],
        ["Cecil", string.Empty, "Pécs"],
        ["Dora", "100", "O'Fallon"],
    ];

    [TestMethod]
    public async Task TheRenderedSqlReturnsTheRowsTheInMemoryEngineReturns()
    {
        string path = CreateDatabase();
        var reader = new SqliteDatabaseReader();
        string[] queries =
        [
            "SELECT name WHERE amount > 10",
            "WHERE city = 'Pécs' AND amount IS EMPTY",
            "SELECT name, city WHERE name CONTAINS 'E' OR city = 'O''Fallon'",
            "WHERE name LIKE 'D%' OR amount >= 20",
            "SELECT name WHERE name NOT LIKE '%a%'",
            "WHERE amount IS NOT EMPTY ORDER BY amount DESC",
            "SELECT name, amount WHERE amount <> 20 ORDER BY amount LIMIT 2",
            "SELECT * WHERE city != 'Pécs' ORDER BY name DESC",
        ];

        foreach (string query in queries)
        {
            GridQueryResult memory = GridQuery.Run(Headers, Rows, query);
            string sql = GridQuery.ToSql(GridQuery.Parse(query, Headers), "sales");
            SqlTable table = await reader.QueryAsync(path, sql, 10_000, CancellationToken.None);

            string[] fromMemory = [.. memory.Rows.Select(row => string.Join('|', row))];
            string[] fromSql = [.. table.Rows.Select(row => string.Join('|', row.Select(cell => cell == "NULL" ? string.Empty : cell)))];
            if (!query.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase))
            {
                Array.Sort(fromMemory, StringComparer.Ordinal);
                Array.Sort(fromSql, StringComparer.Ordinal);
            }

            CollectionAssert.AreEqual(fromMemory, fromSql, $"query: {query}\nsql: {sql}");
        }
    }
}
