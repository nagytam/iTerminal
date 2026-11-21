using iConsole.Formats.Csv;
using iConsole.Csv;
using iConsole.Tabular;

namespace iConsole.Test.Csv;

[TestClass]
public class CsvQueryTest
{
    private static readonly CsvDocument Sample = new CsvDocumentParser().Parse(
        "name,amount,city\n" +
        "Apples,3,Pécs\n" +
        "Pears,12,Budapest\n" +
        "Plums,8,pécs\n" +
        "Figs,,Szeged\n" +
        "Limes,100,Debrecen\n");

    private static string[] Names(GridQueryResult result) => result.Rows.Select(row => row[0]).ToArray();

    [Timeout(60_000)]
    [TestMethod]
    public void AnEmptyQueryShowsEveryRowAndColumn()
    {
        GridQueryResult result = GridQuery.Run(Sample.Headers, Sample.Rows, string.Empty);

        CollectionAssert.AreEqual(Sample.Headers.ToArray(), result.Headers.ToArray());
        Assert.AreEqual(5, result.Rows.Count);
        Assert.AreEqual(5, result.Matched);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SelectChoosesAndOrdersTheColumns()
    {
        GridQueryResult result = GridQuery.Run(Sample.Headers, Sample.Rows, "SELECT city, name");

        CollectionAssert.AreEqual(new[] { "city", "name" }, result.Headers.ToArray());
        CollectionAssert.AreEqual(new[] { "Pécs", "Apples" }, result.Rows[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NumbersCompareAsNumbers_NotAsText()
    {
        GridQueryResult result = GridQuery.Run(Sample.Headers, Sample.Rows, "amount > 5");

        // 12 and 8 and 100; as text "100" would sort below "3" and "12" would fail "> 5" in text order.
        CollectionAssert.AreEqual(new[] { "Pears", "Plums", "Limes" }, Names(result));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WhereWithAndOrAndAndBindsTighterThanOr()
    {
        GridQueryResult result = GridQuery.Run(Sample.Headers, Sample.Rows, "SELECT name WHERE city = 'Pécs' AND amount < 5 OR city = 'Szeged'");

        CollectionAssert.AreEqual(new[] { "Apples", "Figs" }, Names(result));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextComparisonIgnoresCase()
    {
        GridQueryResult result = GridQuery.Run(Sample.Headers, Sample.Rows, "city = 'PÉCS'");

        CollectionAssert.AreEqual(new[] { "Apples", "Plums" }, Names(result));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LikeUsesPercentAndUnderscore()
    {
        CollectionAssert.AreEqual(new[] { "Pears", "Plums" }, Names(GridQuery.Run(Sample.Headers, Sample.Rows, "SELECT name WHERE name LIKE 'P%'")));
        CollectionAssert.AreEqual(new[] { "Figs" }, Names(GridQuery.Run(Sample.Headers, Sample.Rows, "SELECT name WHERE name LIKE 'Fi_s'")));
        CollectionAssert.AreEqual(new[] { "Apples", "Pears", "Plums", "Figs", "Limes" },
            Names(GridQuery.Run(Sample.Headers, Sample.Rows, "SELECT name WHERE name NOT LIKE 'zzz'")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ContainsIsASubstringMatch()
    {
        CollectionAssert.AreEqual(new[] { "Budapest", "Debrecen" }, Names(GridQuery.Run(Sample.Headers, Sample.Rows, "SELECT city WHERE city CONTAINS 'PEST' OR city CONTAINS 'rec'")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsEmptyFindsTheBlankFields()
    {
        CollectionAssert.AreEqual(new[] { "Figs" }, Names(GridQuery.Run(Sample.Headers, Sample.Rows, "SELECT name WHERE amount IS EMPTY")));
        Assert.AreEqual(4, GridQuery.Run(Sample.Headers, Sample.Rows, "amount IS NOT EMPTY").Rows.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OrderByAndLimitApplyAfterTheFilter()
    {
        GridQueryResult result = GridQuery.Run(Sample.Headers, Sample.Rows, "SELECT name, amount ORDER BY amount DESC LIMIT 2");

        CollectionAssert.AreEqual(new[] { "Limes", "Pears" }, Names(result));
        Assert.AreEqual(5, result.Matched, "Matched counts the rows before the limit");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AColumnNameWithSpacesGoesInQuotesOrBrackets()
    {
        var document = new CsvDocumentParser().Parse("First name,Total\nAda,4\n");

        CollectionAssert.AreEqual(new[] { "Ada" }, GridQuery.Run(document.Headers, document.Rows, "SELECT [First name] WHERE \"Total\" = 4").Rows[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnUnknownColumnIsNamedAndTheColumnsAreListed()
    {
        var error = Assert.Throws<GridQueryException>(() => GridQuery.Run(Sample.Headers, Sample.Rows, "SELECT colour"));

        StringAssert.Contains(error.Message, "'colour'");
        StringAssert.Contains(error.Message, "name, amount, city");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AMalformedQueryIsRejectedInWords()
    {
        Assert.Throws<GridQueryException>(() => GridQuery.Run(Sample.Headers, Sample.Rows, "amount >"));
        Assert.Throws<GridQueryException>(() => GridQuery.Run(Sample.Headers, Sample.Rows, "name = 'open"));
        Assert.Throws<GridQueryException>(() => GridQuery.Run(Sample.Headers, Sample.Rows, "name ! 'x'"));
        Assert.Throws<GridQueryException>(() => GridQuery.Run(Sample.Headers, Sample.Rows, "LIMIT many"));
        Assert.Throws<GridQueryException>(() => GridQuery.Run(Sample.Headers, Sample.Rows, "name = 'x' extra"));
    }
}
