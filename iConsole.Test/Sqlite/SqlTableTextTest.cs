using iConsole.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Sqlite;

[TestClass]
public class SqlTableTextTest
{
    [TestMethod]
    public void Format_AlignsTheHeaderRowsAndACountFooter()
    {
        var table = new SqlTable(["name", "n"], [["a", "1"], ["longer", "22"]], false);

        IReadOnlyList<string> lines = SqlTableText.Format(table);

        Assert.AreEqual(
            "name   | n\n-------+---\na      | 1\nlonger | 22\n\n2 row(s)",
            string.Join("\n", lines.Select(l => l.TrimEnd())));
    }

    [TestMethod]
    public void Format_SaysWhenTheRowCapCutTheResultShort()
    {
        var table = new SqlTable(["id"], [["1"]], true);

        IReadOnlyList<string> lines = SqlTableText.Format(table);

        Assert.AreEqual("1 row(s), truncated", lines[^1]);
    }

    [TestMethod]
    public void Format_CutsALongCellWithAnEllipsisAndFlattensLineBreaks()
    {
        string longValue = new('x', 60);
        var table = new SqlTable(["v"], [[longValue], ["one\ntwo"]], false);

        IReadOnlyList<string> lines = SqlTableText.Format(table);

        Assert.AreEqual(new string('x', 39) + "…", lines[2].TrimEnd());
        Assert.AreEqual("one two", lines[3].TrimEnd());
    }

    [TestMethod]
    public void Format_WithNoColumns_IsEmpty()
    {
        Assert.AreEqual(0, SqlTableText.Format(new SqlTable([], [], false)).Count);
    }

    [TestMethod]
    public void HasSqliteHeader_AcceptsTheExactHeaderOnly()
    {
        byte[] header = "SQLite format 3\0"u8.ToArray();
        byte[] withMore = [.. header, 0x10, 0x00];
        byte[] near = "SQLite format 2\0"u8.ToArray();

        Assert.IsTrue(SqliteFileTypes.HasSqliteHeader(header));
        Assert.IsTrue(SqliteFileTypes.HasSqliteHeader(withMore));
        Assert.IsFalse(SqliteFileTypes.HasSqliteHeader(near));
        Assert.IsFalse(SqliteFileTypes.HasSqliteHeader([]));
        Assert.IsFalse(SqliteFileTypes.HasSqliteHeader(header.AsSpan(0, 15)));
    }

    [TestMethod]
    [DataRow("/data/app.sqlite", true)]
    [DataRow("/data/app.SQLITE3", true)]
    [DataRow("/data/app.db", true)]
    [DataRow("/data/app.db3", true)]
    [DataRow("/data/sales.csv", false)]
    [DataRow("/data/app.dbx", false)]
    public void IsSqliteFile_MatchesTheDatabaseEndings(string path, bool expected)
    {
        Assert.AreEqual(expected, SqliteFileTypes.IsSqliteFile(path));
    }
}
