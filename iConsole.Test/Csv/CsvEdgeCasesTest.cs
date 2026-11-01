using System.Diagnostics;
using System.Text;
using iConsole.Csv;
using iConsole.Dialogs;

namespace iConsole.Test.Csv;

/// <summary>Cases at the edges of the CSV reader and the query line that used to give a wrong answer, or none.</summary>
[TestClass]
public class CsvEdgeCasesTest
{
    private static string[] First(CsvQueryResult result) => result.Rows.Select(row => row[0]).ToArray();

    #region Quotes inside quotes

    [TestMethod]
    public void ADoubledSingleQuoteInAValueIsOneQuote()
    {
        CsvDocument document = CsvDocument.Parse("name,city\nO'Brien,Cork\nSmith,Leeds\n");

        CsvQueryResult result = CsvQuery.Run(document, "WHERE name = 'O''Brien'");

        CollectionAssert.AreEqual(new[] { "O'Brien" }, First(result));
    }

    [TestMethod]
    public void ADoubledDoubleQuoteInAColumnNameIsOneQuote()
    {
        CsvDocument document = CsvDocument.Parse("\"say \"\"hi\"\"\",n\nx,1\n");

        CsvQueryResult result = CsvQuery.Run(document, "SELECT \"say \"\"hi\"\"\"");

        Assert.AreEqual("say \"hi\"", result.Headers[0]);
        Assert.AreEqual("x", result.Rows[0][0]);
    }

    [TestMethod]
    public void AValueThatEndsInADoubledQuoteStillCloses()
    {
        CsvDocument document = CsvDocument.Parse("name\nrock'\nrock\n");

        CsvQueryResult result = CsvQuery.Run(document, "WHERE name = 'rock'''");

        CollectionAssert.AreEqual(new[] { "rock'" }, First(result));
    }

    [TestMethod]
    public void AnUnclosedQuoteIsStillReported()
    {
        CsvDocument document = CsvDocument.Parse("name\nx\n");

        Assert.ThrowsExactly<CsvQueryException>(() => CsvQuery.Run(document, "WHERE name = 'abc"));
        Assert.ThrowsExactly<CsvQueryException>(() => CsvQuery.Run(document, "WHERE name = 'abc''"));
    }

    #endregion

    #region LIKE

    [TestMethod]
    [DataRow("apple", "a%", true)]
    [DataRow("apple", "%e", true)]
    [DataRow("apple", "%pp%", true)]
    [DataRow("apple", "a_ple", true)]
    [DataRow("apple", "a_ple_", false)]
    [DataRow("apple", "%", true)]
    [DataRow("", "%", true)]
    [DataRow("", "_", false)]
    [DataRow("APPLE", "apple", true)]
    [DataRow("a(b)", "a(%)", true)]
    [DataRow("a.b", "a.b", true)]
    [DataRow("axb", "a.b", false)]
    [DataRow("100%", "100%", true)]
    [DataRow("a+b", "a+b", true)]
    [DataRow("ab", "a%%b", true)]
    [DataRow("abc", "%b", false)]
    [DataRow("mississippi", "m%iss%ppi", true)]
    [DataRow("mississippi", "m%iss%ppx", false)]
    public void LikeMatchesAsSqlDoes(string text, string pattern, bool expected)
    {
        Assert.AreEqual(expected, CsvQuery.GlobMatch(text, pattern), $"'{text}' LIKE '{pattern}'");
    }

    [TestMethod]
    public void ALikePatternWithManyWildcardsFinishesAtOnce()
    {
        // As a regular expression this backtracked for far longer than the one-second limit.
        string field = new('a', 60);
        CsvDocument document = CsvDocument.Parse($"x\n{field}\n{field}\n");
        Stopwatch clock = Stopwatch.StartNew();

        CsvQueryResult result = CsvQuery.Run(document, "WHERE x LIKE '%a%a%a%a%a%a%a%a%a%a%a%a%b'");

        Assert.AreEqual(0, result.Rows.Count);
        Assert.IsTrue(clock.ElapsedMilliseconds < 500, $"took {clock.ElapsedMilliseconds} ms");
    }

    [TestMethod]
    public void NotLikeKeepsTheRowsThatDoNotMatch()
    {
        CsvDocument document = CsvDocument.Parse("x\napple\nberry\n");

        CollectionAssert.AreEqual(new[] { "berry" }, First(CsvQuery.Run(document, "WHERE x NOT LIKE 'a%'")));
    }

    #endregion

    #region Order

    [TestMethod]
    public void AColumnOfNumbersAndWordsSortsNumbersFirstThenWords()
    {
        // "10" < "1a" and "1a" < "9" as text, but "9" < "10" as numbers: no single order satisfied all three pairs.
        CsvDocument document = CsvDocument.Parse("v\n10\n9\n1a\napple\n2\n");

        CsvQueryResult ascending = CsvQuery.Run(document, "ORDER BY v");
        CsvQueryResult descending = CsvQuery.Run(document, "ORDER BY v DESC");

        CollectionAssert.AreEqual(new[] { "2", "9", "10", "1a", "apple" }, First(ascending));
        CollectionAssert.AreEqual(new[] { "apple", "1a", "10", "9", "2" }, First(descending));
    }

    [TestMethod]
    public void TheOrderDoesNotDependOnWhereTheRowsStarted()
    {
        string[] values = ["10", "9", "1a", "apple", "2", "B", "a", "-3", "0.5", ""];
        string[] expected = [.. CsvQuery.Run(CsvDocument.Parse("v\n" + string.Join("\n", values.Where(v => v.Length > 0)) + "\n"), "ORDER BY v").Rows.Select(r => r[0])];

        for (int shift = 1; shift < values.Length - 1; shift++)
        {
            string[] rotated = [.. values.Skip(shift), .. values.Take(shift)];
            CsvDocument document = CsvDocument.Parse("v\n" + string.Join("\n", rotated.Where(v => v.Length > 0)) + "\n");

            CollectionAssert.AreEqual(expected, CsvQuery.Run(document, "ORDER BY v").Rows.Select(r => r[0]).ToArray(), $"rotated by {shift}");
        }
    }

    [TestMethod]
    public void AnEmptyFieldSortsLastWhicheverWayTheColumnIsSorted()
    {
        CsvDocument document = CsvDocument.Parse("n,v\na,5\nb,\nc,1\n");

        CollectionAssert.AreEqual(new[] { "c", "a", "b" }, First(CsvQuery.Run(document, "ORDER BY v")));
        CollectionAssert.AreEqual(new[] { "a", "c", "b" }, First(CsvQuery.Run(document, "ORDER BY v DESC")));
    }

    [TestMethod]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("-Infinity")]
    public void NanAndInfinityAreWordsNotNumbers(string word)
    {
        CsvDocument document = CsvDocument.Parse($"v\n{word}\n5\n");

        CollectionAssert.AreEqual(new[] { "5", word }, First(CsvQuery.Run(document, "ORDER BY v")));
        Assert.AreEqual(1, CsvQuery.Run(document, $"WHERE v = '{word.ToLowerInvariant()}'").Rows.Count, "compared as text, ignoring case");
        Assert.AreEqual(0, CsvQuery.Run(document, "WHERE v > 4 AND v < 6 AND v = 'x'").Rows.Count);
    }

    #endregion

    #region Column names

    [TestMethod]
    public void ARepeatedNameNeverCollidesWithOneThatIsThere()
    {
        CsvDocument document = CsvDocument.Parse("a,a,a_2,a\n1,2,3,4\n");

        Assert.AreEqual(document.Headers.Count, document.Headers.Distinct(StringComparer.OrdinalIgnoreCase).Count(), string.Join(",", document.Headers));
        CollectionAssert.AreEqual(new[] { "a", "a_2", "a_2_2", "a_3" }, document.Headers.ToArray());
    }

    [TestMethod]
    public void EmptyAndRepeatedNamesTogetherStayDistinct()
    {
        CsvDocument document = CsvDocument.Parse(",Column1,,Column1\n1,2,3,4\n");

        Assert.AreEqual(4, document.Headers.Distinct(StringComparer.OrdinalIgnoreCase).Count(), string.Join(",", document.Headers));
    }

    [TestMethod]
    public void EveryColumnCanBeNamedInAQuery()
    {
        CsvDocument document = CsvDocument.Parse("a,a,a_2\n1,2,3\n");

        CsvQueryResult result = CsvQuery.Run(document, "SELECT a, a_2, a_2_2");

        CollectionAssert.AreEqual(new[] { "1", "2", "3" }, result.Rows[0]);
    }

    #endregion

    #region Reading the file

    [TestMethod]
    public void AUtf16FileWithItsByteOrderMarkIsReadAsText()
    {
        const string text = "név,érték\nalma,3\n";
        byte[] little = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)];
        byte[] big = [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(text)];

        Assert.AreEqual(text, CsvViewerDialog.Decode(little));
        Assert.AreEqual(text, CsvViewerDialog.Decode(big));
    }

    [TestMethod]
    public void AUtf8FileIsStillReadAsUtf8_WithOrWithoutItsMark()
    {
        const string text = "név,érték\nalma,3\n";
        byte[] plain = Encoding.UTF8.GetBytes(text);
        byte[] marked = [.. Encoding.UTF8.GetPreamble(), .. plain];

        Assert.AreEqual(text, CsvViewerDialog.Decode(plain));
        // The mark stays in the text and the reader drops it.
        CsvDocument document = CsvDocument.Parse(CsvViewerDialog.Decode(marked));
        Assert.AreEqual("név", document.Headers[0]);
    }

    [TestMethod]
    public void AShortFileIsNotMistakenForUtf16()
    {
        Assert.AreEqual(string.Empty, CsvViewerDialog.Decode([]));
        Assert.AreEqual("x", CsvViewerDialog.Decode([(byte)'x']));
        Assert.AreEqual("ÿ", CsvViewerDialog.Decode(Encoding.UTF8.GetBytes("ÿ")));
    }

    #endregion
}
