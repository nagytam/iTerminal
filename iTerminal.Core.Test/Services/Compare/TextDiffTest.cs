using iTerminal.Core.Services.Compare;

namespace iTerminal.Core.Test.Services.Compare;

/// <summary>The line diff behind Ctrl+F10: what is the same, what moved, and how a run of edits is paired.</summary>
[TestClass]
public class TextDiffTest
{
    private static string[] Lines(string text) => text.Length == 0 ? [] : text.Split(',');

    private static string Kinds(TextDiffResult result) =>
        string.Concat(result.Rows.Select(r => r.Kind switch
        {
            DiffRowKind.Same => '=',
            DiffRowKind.Changed => '~',
            DiffRowKind.LeftOnly => '-',
            _ => '+',
        }));

    [TestMethod]
    public void IdenticalTextsHaveNoDifferences()
    {
        var result = TextDiff.Compare(Lines("a,b,c"), Lines("a,b,c"));

        Assert.IsTrue(result.AreIdentical);
        Assert.AreEqual("===", Kinds(result));
        Assert.AreEqual("The files are identical.", result.Summary());
    }

    [TestMethod]
    public void AnEditedLineIsShownBesideItsOldSelf()
    {
        var result = TextDiff.Compare(Lines("a,b,c"), Lines("a,X,c"));

        Assert.AreEqual("=~=", Kinds(result));
        Assert.AreEqual("b", result.Rows[1].Left);
        Assert.AreEqual("X", result.Rows[1].Right);
        Assert.AreEqual(2, result.Rows[1].LeftNumber);
        Assert.AreEqual(2, result.Rows[1].RightNumber);
        CollectionAssert.AreEqual(new[] { 1 }, result.HunkStarts.ToArray());
    }

    [TestMethod]
    public void AddedAndRemovedLinesKeepTheirOwnNumbers()
    {
        var result = TextDiff.Compare(Lines("a,b,c,d"), Lines("a,c,d,e"));

        Assert.AreEqual("=-==+", Kinds(result));
        Assert.IsNull(result.Rows[1].RightNumber);
        Assert.AreEqual(2, result.Rows[1].LeftNumber);
        Assert.IsNull(result.Rows[4].LeftNumber);
        Assert.AreEqual(4, result.Rows[4].RightNumber);
    }

    [TestMethod]
    public void ARunOfEditsWithUnequalLengthsPairsThenSpills()
    {
        var result = TextDiff.Compare(Lines("a,b,c,z"), Lines("a,X,z"));

        Assert.AreEqual("=~-=", Kinds(result));
        Assert.AreEqual(1, result.HunkStarts.Count);
    }

    [TestMethod]
    public void OneSideEmptyIsAllOnTheOtherSide()
    {
        Assert.AreEqual("+++", Kinds(TextDiff.Compare(Lines(""), Lines("a,b,c"))));
        Assert.AreEqual("--", Kinds(TextDiff.Compare(Lines("a,b"), Lines(""))));
        Assert.IsTrue(TextDiff.Compare(Lines(""), Lines("")).AreIdentical);
    }

    [TestMethod]
    public void SeparateChangesAreSeparateDifferences()
    {
        var result = TextDiff.Compare(Lines("a,b,c,d,e,f"), Lines("a,B,c,d,E,f"));

        CollectionAssert.AreEqual(new[] { 1, 4 }, result.HunkStarts.ToArray());
        StringAssert.StartsWith(result.Summary(), "2 differences");
    }

    [TestMethod]
    public void RowsAccountForEveryLineOfBothSides()
    {
        var left = Lines("1,2,3,4,5,6,7,8,9");
        var right = Lines("1,3,4,x,5,6,y,z,9,10");

        var result = TextDiff.Compare(left, right);

        var leftSeen = result.Rows.Where(r => r.LeftNumber is not null).Select(r => r.Left).ToArray();
        var rightSeen = result.Rows.Where(r => r.RightNumber is not null).Select(r => r.Right).ToArray();
        CollectionAssert.AreEqual(left, leftSeen);
        CollectionAssert.AreEqual(right, rightSeen);
        Assert.IsFalse(result.IsApproximate);
    }

    [TestMethod]
    public void ChangesInWhitespaceCanBeIgnored()
    {
        var left = new[] { "a  b", "c" };
        var right = new[] { "a b", "c " };

        Assert.IsFalse(TextDiff.Compare(left, right).AreIdentical);
        Assert.IsTrue(TextDiff.Compare(left, right, new TextDiffOptions(IgnoreWhitespace: true)).AreIdentical);
    }

    [TestMethod]
    public void CaseCanBeIgnored()
    {
        Assert.IsFalse(TextDiff.Compare(["Hello"], ["hello"]).AreIdentical);
        Assert.IsTrue(TextDiff.Compare(["Hello"], ["hello"], new TextDiffOptions(IgnoreCase: true)).AreIdentical);
    }

    [TestMethod]
    public void IgnoredDifferencesStillShowTheOriginalText()
    {
        var result = TextDiff.Compare(["Hello", "x"], ["hello", "y"], new TextDiffOptions(IgnoreCase: true));

        Assert.AreEqual("=~", Kinds(result));
        Assert.AreEqual("Hello", result.Rows[0].Left);
        Assert.AreEqual("hello", result.Rows[0].Right);
    }

    [TestMethod]
    public void FilesThatShareNothingAreGivenUpOnRatherThanSearchedForever()
    {
        var left = Enumerable.Range(0, TextDiff.MaxEdits + 50).Select(i => "L" + i).ToArray();
        var right = Enumerable.Range(0, TextDiff.MaxEdits + 50).Select(i => "R" + i).ToArray();

        var result = TextDiff.Compare(left, right);

        Assert.IsTrue(result.IsApproximate);
        Assert.AreEqual(left.Length, result.Changed);
    }

    [TestMethod]
    public void ALargeFileWithOneEditIsQuick()
    {
        var left = Enumerable.Range(0, 200_000).Select(i => "line " + i).ToArray();
        var right = (string[])left.Clone();
        right[100_000] = "changed";

        var result = TextDiff.Compare(left, right);

        Assert.AreEqual(1, result.HunkStarts.Count);
        Assert.AreEqual(100_000, result.HunkStarts[0]);
    }
}
