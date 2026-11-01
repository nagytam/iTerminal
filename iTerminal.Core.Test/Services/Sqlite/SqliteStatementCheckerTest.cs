using iConsole.Sqlite;
using iTerminal.Core.Services.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Test.Services.Sqlite;

[TestClass]
public class SqliteStatementCheckerTest
{
    [DataTestMethod]
    [DataRow("SELECT 1")]
    [DataRow("select * from t where x = 'a;b'")]
    [DataRow("SELECT '--x'")]
    [DataRow("SELECT \"--x\"")]
    [DataRow("SELECT 'it''s'")]
    [DataRow("SELECT [a;b] FROM t")]
    [DataRow("-- a comment first\nSELECT 1")]
    [DataRow("/* a comment */ SELECT 1")]
    [DataRow("SELECT 1;")]
    [DataRow("SELECT 1; -- trailing comment")]
    [DataRow("WITH x AS (SELECT 1) SELECT * FROM x")]
    [DataRow("EXPLAIN SELECT 1")]
    [DataRow("EXPLAIN QUERY PLAN SELECT 1")]
    [DataRow("PRAGMA table_info(t)")]
    [DataRow("PRAGMA table_info(\"t\")")]
    [DataRow("pragma INDEX_LIST(t)")]
    [Timeout(60_000)]
    public void Validate_AcceptsOneReadOnlyStatement(string sql)
    {
        SqliteStatementChecker.Validate(sql);
    }

    [DataTestMethod]
    [DataRow("SELECT '/*' ; DROP TABLE t; --'")]
    [DataRow("SELECT 1; DROP TABLE t")]
    [DataRow("SELECT 1; SELECT 2")]
    [DataRow("SELECT 1; ATTACH 'x.db' AS y")]
    [DataRow("ATTACH 'x.db' AS y")]
    [DataRow("/* c */ ATTACH 'x.db' AS y")]
    [DataRow("-- c\nDELETE FROM t")]
    [DataRow("INSERT INTO t VALUES (1)")]
    [DataRow("PRAGMA query_only=OFF")]
    [DataRow("PRAGMA schema_version=5")]
    [DataRow("PRAGMA journal_mode=WAL")]
    [DataRow("PRAGMA main.table_info(t)")]
    [DataRow("EXPLAIN DELETE FROM t")]
    [DataRow("EXPLAIN EXPLAIN DELETE FROM t")]
    [DataRow("EXPLAIN QUERY SELECT 1")]
    [DataRow("SELECT 'unterminated")]
    [DataRow("SELECT [unclosed")]
    [DataRow("SELECT /* open")]
    [DataRow("\"SELECT\" 1")]
    [DataRow(";SELECT 1")]
    [DataRow("")]
    [DataRow("   ")]
    [Timeout(60_000)]
    public void Validate_RefusesAnythingElse(string sql)
    {
        Assert.ThrowsExactly<SqlQueryException>(() => SqliteStatementChecker.Validate(sql));
    }

    [TestMethod]
    [Timeout(60_000)]
    public void Validate_RefusesASecondStatementEvenWhenTheFirstIsAllowed()
    {
        SqlQueryException failure = Assert.ThrowsExactly<SqlQueryException>(() => SqliteStatementChecker.Validate("SELECT 1; DROP TABLE t"));

        StringAssert.Contains(failure.Message, "one statement");
    }
}
