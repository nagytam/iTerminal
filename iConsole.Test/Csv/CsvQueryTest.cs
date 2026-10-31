using iConsole.Csv;

namespace iConsole.Test.Csv;

[TestClass]
public class CsvQueryTest
{
    private static readonly CsvDocument Sample = CsvDocument.Parse(
        "name,amount,city\n" +
        "Apples,3,Pécs\n" +
        "Pears,12,Budapest\n" +
        "Plums,8,pécs\n" +
        "Figs,,Szeged\n" +
        "Limes,100,Debrecen\n");

    private static string[] Names(CsvQueryResult result) => result.Rows.Select(row => row[0]).ToArray();

    [TestMethod]
    public void AnEmptyQueryShowsEveryRowAndColumn()
    {
        CsvQueryResult result = CsvQuery.Run(Sample, string.Empty);

        CollectionAssert.AreEqual(Sample.Headers.ToArray(), result.Headers.ToArray());
        Assert.AreEqual(5, result.Rows.Count);
        Assert.AreEqual(5, result.Matched);
    }

    [TestMethod]
    public void SelectChoosesAndOrdersTheColumns()
    {
        CsvQueryResult result = CsvQuery.Run(Sample, "SELECT city, name");

        CollectionAssert.AreEqual(new[] { "city", "name" }, result.Headers.ToArray());
        CollectionAssert.AreEqual(new[] { "Pécs", "Apples" }, result.Rows[0]);
    }

    [TestMethod]
    public void NumbersCompareAsNumbers_NotAsText()
    {
        CsvQueryResult result = CsvQuery.Run(Sample, "amount > 5");

        // 12 and 8 and 100; as text "100" would sort below "3" and "12" would fail "> 5" in text order.
        CollectionAssert.AreEqual(new[] { "Pears", "Plums", "Limes" }, Names(result));
    }

    [TestMethod]
    public void WhereWithAndOrAndAndBindsTighterThanOr()
    {
        CsvQueryResult result = CsvQuery.Run(Sample, "SELECT name WHERE city = 'Pécs' AND amount < 5 OR city = 'Szeged'");

        CollectionAssert.AreEqual(new[] { "Apples", "Figs" }, Names(result));
    }

    [TestMethod]
    public void TextComparisonIgnoresCase()
    {
        CsvQueryResult result = CsvQuery.Run(Sample, "city = 'PÉCS'");

        CollectionAssert.AreEqual(new[] { "Apples", "Plums" }, Names(result));
    }

    [TestMethod]
    public void LikeUsesPercentAndUnderscore()
    {
        CollectionAssert.AreEqual(new[] { "Pears", "Plums" }, Names(CsvQuery.Run(Sample, "SELECT name WHERE name LIKE 'P%'")));
        CollectionAssert.AreEqual(new[] { "Figs" }, Names(CsvQuery.Run(Sample, "SELECT name WHERE name LIKE 'Fi_s'")));
        CollectionAssert.AreEqual(new[] { "Apples", "Pears", "Plums", "Figs", "Limes" },
            Names(CsvQuery.Run(Sample, "SELECT name WHERE name NOT LIKE 'zzz'")));
    }

    [TestMethod]
    public void ContainsIsASubstringMatch()
    {
        CollectionAssert.AreEqual(new[] { "Budapest", "Debrecen" }, Names(CsvQuery.Run(Sample, "SELECT city WHERE city CONTAINS 'PEST' OR city CONTAINS 'rec'")));
    }

    [TestMethod]
    public void IsEmptyFindsTheBlankFields()
    {
        CollectionAssert.AreEqual(new[] { "Figs" }, Names(CsvQuery.Run(Sample, "SELECT name WHERE amount IS EMPTY")));
        Assert.AreEqual(4, CsvQuery.Run(Sample, "amount IS NOT EMPTY").Rows.Count);
    }

    [TestMethod]
    public void OrderByAndLimitApplyAfterTheFilter()
    {
        CsvQueryResult result = CsvQuery.Run(Sample, "SELECT name, amount ORDER BY amount DESC LIMIT 2");

        CollectionAssert.AreEqual(new[] { "Limes", "Pears" }, Names(result));
        Assert.AreEqual(5, result.Matched, "Matched counts the rows before the limit");
    }

    [TestMethod]
    public void AColumnNameWithSpacesGoesInQuotesOrBrackets()
    {
        var document = CsvDocument.Parse("First name,Total\nAda,4\n");

        CollectionAssert.AreEqual(new[] { "Ada" }, CsvQuery.Run(document, "SELECT [First name] WHERE \"Total\" = 4").Rows[0]);
    }

    [TestMethod]
    public void AnUnknownColumnIsNamedAndTheColumnsAreListed()
    {
        var error = Assert.Throws<CsvQueryException>(() => CsvQuery.Run(Sample, "SELECT colour"));

        StringAssert.Contains(error.Message, "'colour'");
        StringAssert.Contains(error.Message, "name, amount, city");
    }

    [TestMethod]
    public void AMalformedQueryIsRejectedInWords()
    {
        Assert.Throws<CsvQueryException>(() => CsvQuery.Run(Sample, "amount >"));
        Assert.Throws<CsvQueryException>(() => CsvQuery.Run(Sample, "name = 'open"));
        Assert.Throws<CsvQueryException>(() => CsvQuery.Run(Sample, "name ! 'x'"));
        Assert.Throws<CsvQueryException>(() => CsvQuery.Run(Sample, "LIMIT many"));
        Assert.Throws<CsvQueryException>(() => CsvQuery.Run(Sample, "name = 'x' extra"));
    }
}
