using System.Text.RegularExpressions;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iConsole.Sqlite;
using iConsole.Tabular;
using iConsole.Test.Tabular;
using Moq;

namespace iConsole.Test.Dialogs;

[TestClass]
public class SqliteViewerDialogTest
{
    private sealed class FakeReader : ISqliteDatabaseReader
    {
        public Func<string, CancellationToken, Task<SqlTable>> Run { get; set; } =
            (_, _) => Task.FromResult(new SqlTable(["name"], [], false));

        public List<string> Queries { get; } = [];

        /// <summary>
        /// A statement that is a window of a SELECT (the viewer reads results in blocks) is answered from the table <see cref="Run"/> gives for
        /// the statement: its column names, its row count, and the rows its LIMIT and OFFSET name.
        /// </summary>
        public async Task<SqlTable> QueryAsync(string path, string sql, int maxRows, CancellationToken cancellationToken)
        {
            Queries.Add(sql);
            SqlTable full = await Run(sql, cancellationToken);
            if (sql.Contains("COUNT(*)", StringComparison.Ordinal))
            {
                return new SqlTable(["COUNT(*)"], [[full.Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)]], false);
            }

            if (sql.EndsWith("LIMIT 0", StringComparison.Ordinal))
            {
                return new SqlTable(full.Headers, [], false);
            }

            System.Text.RegularExpressions.Match window = Regex.Match(sql, @"LIMIT (\d+) OFFSET (\d+)$");
            if (window.Success)
            {
                int limit = int.Parse(window.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                int offset = int.Parse(window.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                return new SqlTable(full.Headers, [.. full.Rows.Skip(offset).Take(limit)], false);
            }

            return full;
        }
    }

    private static SqliteViewerDialog CreateDialog(FakeReader reader, IGridQueryAssistant? assistant = null) =>
        new(reader, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), new ThemeService(), assistant)
        {
            Url = new Url("file:///data/app.db"),
        };

    /// <summary>A database with two tables: the schema query and the zero-row column reads each get their own answer.</summary>
    private static FakeReader TwoTableReader() =>
        new()
        {
            Run = (sql, _) => Task.FromResult(sql switch
            {
                _ when sql.Contains("sqlite_master", StringComparison.Ordinal) && sql.Contains("type IN", StringComparison.Ordinal) =>
                    new SqlTable(["name"], [["albums"], ["artists"]], false),
                "SELECT * FROM \"albums\" LIMIT 0" => new SqlTable(["title", "year"], [], false),
                "SELECT * FROM \"artists\" LIMIT 0" => new SqlTable(["name"], [], false),
                _ => new SqlTable(["name"], [], false),
            }),
        };

    /// <summary>Waits for the running query to finish, then lets the dialog take its result on an idle pass, as the UI does.</summary>
    private static async Task LandAsync(SqliteViewerDialog dialog)
    {
        try
        {
            await dialog.QueryTask!;
        }
        catch
        {
            // A failed or cancelled query is handled by OnIdle, which is what is under test.
        }

        dialog.OnIdle();
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task OpensWithTheSchemaListing()
    {
        var reader = new FakeReader();
        SqliteViewerDialog dialog = CreateDialog(reader);

        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        Assert.IsTrue(reader.Queries.Any(query => query.Contains(SqliteViewerDialog.DefaultQuery, StringComparison.Ordinal)), "the default query is read through the array");
        Assert.AreEqual("0 row(s), 1 column(s)", dialog.Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AResultLandsInTheGridWithItsCounts()
    {
        var reader = new FakeReader
        {
            Run = (_, _) => Task.FromResult(new SqlTable(["id", "name"], [["1", "a"], ["2", "b"]], false)),
        };
        SqliteViewerDialog dialog = CreateDialog(reader);

        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        Assert.AreEqual(2, dialog.Grid.RowCount);
        Assert.AreEqual(2, dialog.Grid.ColumnCount);
        Assert.AreEqual("b", dialog.Grid.CellAt(1, 1));
        Assert.AreEqual("2 row(s), 2 column(s)", dialog.Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ATruncatedResultSaysHowManyRowsAreShown()
    {
        var reader = new FakeReader
        {
            Run = (_, _) => Task.FromResult(new SqlTable(["id"], [.. Enumerable.Range(0, 10_001).Select(i => new[] { i.ToString(System.Globalization.CultureInfo.InvariantCulture) })], false)),
        };
        SqliteViewerDialog dialog = CreateDialog(reader);

        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        // The count the reader gives decides the cut: 10,001 rows, of which 10,000 are shown.
        Assert.AreEqual(10_000, dialog.Grid.RowCount);
        StringAssert.Contains(dialog.Status, "only the first 10,000 rows are shown");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AFailedQueryShowsItsMessageAndKeepsTheRowsOnScreen()
    {
        var reader = new FakeReader();
        SqliteViewerDialog dialog = CreateDialog(reader);
        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        reader.Run = (_, _) => Task.FromException<SqlTable>(new SqlQueryException("no such table: missing"));
        dialog.StartQuery("SELECT * FROM missing");
        await LandAsync(dialog);

        Assert.AreEqual("no such table: missing", dialog.Status);
        Assert.IsNotNull(dialog.Table, "the rows of the last good query stay on screen");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task OnlyTheNewestQueryLands()
    {
        var reader = new FakeReader();
        SqliteViewerDialog dialog = CreateDialog(reader);
        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        var slow = new TaskCompletionSource<SqlTable>();
        reader.Run = (sql, _) => sql == "slow" ? slow.Task : Task.FromResult(new SqlTable(["fast"], [["1"]], false));
        dialog.StartQuery("slow");
        dialog.StartQuery("fast");
        await LandAsync(dialog);

        slow.SetResult(new SqlTable(["slow"], [["2"], ["3"]], false));
        dialog.OnIdle();

        Assert.AreEqual(1, dialog.Grid.RowCount);
        Assert.AreEqual("1", dialog.Grid.CellAt(0, 0));
        Assert.AreEqual("1 row(s), 1 column(s)", dialog.Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task CloseCancelsTheRunningQueryAndWritesNothing()
    {
        CancellationToken observed = default;
        var reader = new FakeReader
        {
            Run = (_, token) =>
            {
                observed = token;
                return Task.Delay(Timeout.Infinite, token).ContinueWith(_ => new SqlTable([], [], false), TaskScheduler.Default);
            },
        };
        SqliteViewerDialog dialog = CreateDialog(reader);
        await dialog.OnInitializeAsync();
        string statusBeforeClose = dialog.Status;

        dialog.Close();
        await Task.Delay(50);
        dialog.OnIdle();

        Assert.IsTrue(observed.IsCancellationRequested, "the running query was told to stop");
        Assert.AreEqual(statusBeforeClose, dialog.Status, "a cancelled query writes nothing to the status line");
        Assert.IsNull(dialog.QueryTask);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task F4SwitchesToTheTextOutputAndBack()
    {
        var reader = new FakeReader
        {
            Run = (_, _) => Task.FromResult(new SqlTable(["id"], [["1"]], false)),
        };
        SqliteViewerDialog dialog = CreateDialog(reader);
        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        dialog.ToggleTextMode();
        Assert.IsTrue(dialog.IsTextMode);

        dialog.ToggleTextMode();
        Assert.IsFalse(dialog.IsTextMode);
    }

    [TestMethod]
    public async Task ADraftIsCheckedAgainstTheTablesAndLandsAsTheSqlItMeans()
    {
        FakeGridQueryAssistant assistant = new("SELECT title FROM albums WHERE year > 1990 ORDER BY year DESC LIMIT 5");
        FakeReader reader = TwoTableReader();
        SqliteViewerDialog dialog = CreateDialog(reader, assistant);
        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        dialog.SetQueryText("? the newest albums after 1990");
        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));
        await dialog.DraftTask!;
        dialog.OnIdle();

        Assert.AreEqual("albums", assistant.Schema!.Tables.Single(t => t.Name == "albums").Name);
        CollectionAssert.AreEqual(new[] { "title", "year" }, assistant.Schema.Tables.Single(t => t.Name == "albums").Columns.Select(c => c.Name).ToArray());
        Assert.AreEqual(
            "SELECT \"title\" FROM \"albums\" WHERE (\"year\" > '1990') ORDER BY (\"year\" IS NULL OR \"year\" = '') ASC, \"year\" DESC LIMIT 5",
            dialog.QueryText,
            "the line holds the SQL that will run, so the user sees what it does");
        Assert.IsFalse(reader.Queries.Any(sql => sql.Contains("1990", StringComparison.Ordinal) && sql.StartsWith("SELECT \"title\"", StringComparison.Ordinal)), "nothing ran yet");

        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));
        await LandAsync(dialog);

        Assert.IsTrue(reader.Queries.Any(query => query.Contains(dialog.QueryText, StringComparison.Ordinal)), "Enter runs the draft through the read-only reader");
    }

    [TestMethod]
    public async Task ADraftNamingATableTheDatabaseLacksIsRefused()
    {
        FakeGridQueryAssistant assistant = new("SELECT title FROM songs");
        SqliteViewerDialog dialog = CreateDialog(TwoTableReader(), assistant);
        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        dialog.SetQueryText("? songs");
        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));
        await dialog.DraftTask!;
        dialog.OnIdle();

        Assert.AreEqual("? songs", dialog.QueryText);
        StringAssert.Contains(dialog.Status, "songs");
    }

    [TestMethod]
    public async Task WithoutAiTheQuestionSaysSo()
    {
        SqliteViewerDialog dialog = CreateDialog(TwoTableReader());
        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        dialog.SetQueryText("? anything");
        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));
        dialog.OnIdle();

        Assert.AreEqual("? anything", dialog.QueryText);
        StringAssert.Contains(dialog.Status, "AI is not set up");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AColonLineFiltersTheResultAsOneStatementInTheDatabase()
    {
        var reader = new FakeReader
        {
            Run = (sql, _) => Task.FromResult(sql.Contains("AS result", StringComparison.Ordinal)
                ? new SqlTable(["id", "name"], [["2", "b"]], false)
                : new SqlTable(["id", "name"], [["1", "a"], ["2", "b"]], false)),
        };
        SqliteViewerDialog dialog = CreateDialog(reader);
        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        dialog.SetQueryText(": id > 1");
        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));
        await dialog.GridQueryTask!;
        dialog.OnIdle();

        Assert.AreEqual(1, dialog.Grid.RowCount, "the filtered result is on screen");
        Assert.IsTrue(reader.Queries.Any(query => query.Contains("AS result", StringComparison.Ordinal) && query.Contains("WHERE", StringComparison.Ordinal)),
            "the filter reaches the database inside one statement over the result");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARawSqlLineIsStillRunAsSql()
    {
        var reader = new FakeReader { Run = (_, _) => Task.FromResult(new SqlTable(["id"], [["1"]], false)) };
        SqliteViewerDialog dialog = CreateDialog(reader);
        await dialog.OnInitializeAsync();
        await LandAsync(dialog);

        dialog.SetQueryText("SELECT id FROM t");
        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));
        await LandAsync(dialog);

        Assert.IsTrue(reader.Queries.Any(query => query.Contains("SELECT id FROM t", StringComparison.Ordinal) && !query.Contains("AS result", StringComparison.Ordinal)), "a line without a colon is raw SQL, run as written inside the window");
    }
}
