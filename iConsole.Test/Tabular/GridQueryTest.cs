using iConsole.Tabular;

namespace iConsole.Test.Tabular;

[TestClass]
public class GridQueryTest
{
    private static readonly string[] Headers = ["name", "amount", "city"];

    private static readonly string[][] Rows =
    [
        ["Anna", "5", "Pécs"],
        ["Bela", "20", "Győr"],
        ["Cecil", "", "Pécs"],
        ["Dora", "100", "O'Fallon"],
    ];

    [TestMethod]
    public void ParsesEveryClauseIntoThePlan()
    {
        GridQueryPlan plan = GridQuery.Parse("SELECT name, AMOUNT FROM sales WHERE amount > 10 AND city = 'Pécs' ORDER BY amount DESC LIMIT 20", Headers);

        Assert.AreEqual("sales", plan.From);
        CollectionAssert.AreEqual(new[] { "name", "amount" }, plan.Columns!.ToArray(), "column names come back as the header spells them");
        Assert.AreEqual("amount", plan.OrderBy);
        Assert.IsTrue(plan.Descending);
        Assert.AreEqual(20, plan.Limit);
        IReadOnlyList<GridTerm> group = plan.Where!.Groups.Single();
        Assert.AreEqual(new GridComparison("amount", ">", "10"), group[0]);
        Assert.AreEqual(new GridComparison("city", "=", "Pécs"), group[1]);
    }

    [TestMethod]
    public void OperatorSpellingsAreNormalised()
    {
        GridQueryPlan plan = GridQuery.Parse("amount == 1 OR amount <> 2 OR amount != 3", Headers);

        Assert.AreEqual("=", ((GridComparison)plan.Where!.Groups[0][0]).Operator);
        Assert.AreEqual("!=", ((GridComparison)plan.Where.Groups[1][0]).Operator);
        Assert.AreEqual("!=", ((GridComparison)plan.Where.Groups[2][0]).Operator);
    }

    [TestMethod]
    public void AnEmptyQueryPlansEverything()
    {
        GridQueryPlan plan = GridQuery.Parse(string.Empty, Headers);

        Assert.IsNull(plan.Where);
        Assert.IsNull(plan.Columns);
        Assert.IsNull(plan.OrderBy);
        Assert.IsNull(plan.Limit);
    }

    [TestMethod]
    public void ExecuteAndRunGiveTheSameRows()
    {
        const string text = "SELECT name WHERE city CONTAINS 'PÉCS' OR amount IS EMPTY ORDER BY name";

        GridQueryResult direct = GridQuery.Run(Headers, Rows, text);
        GridQueryResult planned = GridQuery.Execute(Headers, Rows, GridQuery.Parse(text, Headers));

        CollectionAssert.AreEqual(direct.Rows.Select(r => string.Join('|', r)).ToArray(), planned.Rows.Select(r => string.Join('|', r)).ToArray());
        Assert.AreEqual(direct.Matched, planned.Matched);
        CollectionAssert.AreEqual(new[] { "Anna", "Cecil" }, direct.Rows.Select(r => r[0]).ToArray());
    }

    [TestMethod]
    public void BlankValuesSortLastWhicheverWayTheOrderGoes()
    {
        GridQueryResult ascending = GridQuery.Run(Headers, Rows, "ORDER BY amount");
        GridQueryResult descending = GridQuery.Run(Headers, Rows, "ORDER BY amount DESC");

        Assert.AreEqual("Cecil", ascending.Rows.Last()[0]);
        Assert.AreEqual("Cecil", descending.Rows.Last()[0]);
        Assert.AreEqual("Dora", descending.Rows.First()[0]);
    }

    [TestMethod]
    public void AnUnknownColumnNamesTheColumnsItHas()
    {
        GridQueryException error = Assert.ThrowsExactly<GridQueryException>(() => GridQuery.Parse("WHERE salary > 1", Headers));

        StringAssert.Contains(error.Message, "salary");
        StringAssert.Contains(error.Message, "name, amount, city");
    }

    [TestMethod]
    public void SqlRendersTheSamePlanWithQuotedNamesAndLiteralValues()
    {
        GridQueryPlan plan = GridQuery.Parse("SELECT name, amount WHERE amount > 10 AND city = 'O''Fallon' OR name LIKE 'a%' ORDER BY amount DESC LIMIT 5", Headers);

        string sql = GridQuery.ToSql(plan, "sales");

        Assert.AreEqual(
            "SELECT \"name\", \"amount\" FROM \"sales\" WHERE (\"amount\" > '10' AND \"city\" = 'O''Fallon') OR (\"name\" LIKE 'a%') " +
            "ORDER BY (\"amount\" IS NULL OR \"amount\" = '') ASC, \"amount\" DESC LIMIT 5",
            sql);
    }

    [TestMethod]
    public void SqlRendersEveryTermKind()
    {
        GridQueryPlan plan = GridQuery.Parse("name NOT LIKE 'x_' AND city CONTAINS 'pé' AND amount IS NOT EMPTY OR city IS EMPTY", Headers);

        string sql = GridQuery.ToSql(plan, "t");

        StringAssert.Contains(sql, "\"name\" NOT LIKE 'x_'");
        StringAssert.Contains(sql, "instr(lower(CAST(\"city\" AS TEXT)), lower('pé')) > 0");
        StringAssert.Contains(sql, "(\"amount\" IS NOT NULL AND \"amount\" <> '')");
        StringAssert.Contains(sql, "(\"city\" IS NULL OR \"city\" = '')");
    }

    [TestMethod]
    public void SqlQuotesAnIdentifierWithAQuoteInIt()
    {
        string[] headers = ["odd\"name"];

        string sql = GridQuery.ToSql(GridQuery.Parse("SELECT * WHERE \"odd\"\"name\" = 1", headers), "t\"x");

        Assert.AreEqual("SELECT * FROM \"t\"\"x\" WHERE (\"odd\"\"name\" = '1')", sql);
    }

    [TestMethod]
    public void EveryValueIsQuotedSoAZeroPaddedCodeKeepsItsZero()
    {
        GridQueryPlan plan = GridQuery.Parse("name = '0123' AND amount = 2.5", Headers);

        string sql = GridQuery.ToSql(plan, "t");

        StringAssert.Contains(sql, "\"name\" = '0123'");
        StringAssert.Contains(sql, "\"amount\" = '2.5'");
    }

    [TestMethod]
    public void AnUnterminatedQuoteIsRefused()
    {
        Assert.ThrowsExactly<GridQueryException>(() => GridQuery.Parse("WHERE name = 'open", Headers));
    }

    [TestMethod]
    public void TheAxisClauseIsParsedAfterFrom()
    {
        GridQueryPlan plan = GridQuery.Parse("FROM cube AT axis1 = 2 AT axis3 = 0 ROWS axis0 COLUMNS axis2 WHERE name = 'x'", Headers);

        Assert.AreEqual("cube", plan.From);
        Assert.IsNotNull(plan.Axes);
        CollectionAssert.AreEqual(new (string Axis, long Index)[] { ("axis1", 2), ("axis3", 0) }, plan.Axes.Fixed.ToArray());
        Assert.AreEqual("axis0", plan.Axes.Rows);
        Assert.AreEqual("axis2", plan.Axes.Columns);
        Assert.IsNotNull(plan.Where, "the filter still follows the clause");
    }

    [TestMethod]
    public void AQueryWithoutAClauseHasNoAxes()
    {
        Assert.IsNull(GridQuery.Parse("FROM cube WHERE amount > 1", Headers).Axes);
        Assert.IsNull(GridQuery.Parse("rows > 1", ["rows"]).Axes, "rows without FROM is a column test, not a clause");
    }

    [TestMethod]
    public void AnAxisNameThatIsNotAnAxisIsRefused()
    {
        Assert.ThrowsExactly<GridQueryException>(() => GridQuery.Parse("FROM cube AT amount = 1", Headers));
        Assert.ThrowsExactly<GridQueryException>(() => GridQuery.Parse("FROM cube AT axis1 = two", Headers));
    }
}
