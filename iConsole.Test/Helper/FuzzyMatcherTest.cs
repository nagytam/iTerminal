using iConsole.Helper;

namespace iConsole.Test.Helper;

[TestClass]
public class FuzzyMatcherTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Match_NotASubsequence_ReturnsNull()
    {
        Assert.IsNull(FuzzyMatcher.Match("xyz", "open file"));
        Assert.IsNull(FuzzyMatcher.Match("ab", "ba"));
        Assert.IsNull(FuzzyMatcher.Match("abc", "ab"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Match_EmptyPattern_MatchesEverything()
    {
        FuzzyMatch? match = FuzzyMatcher.Match("", "anything");

        Assert.IsNotNull(match);
        Assert.AreEqual(0, match.Value.Positions.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Match_IgnoresCase_AndReportsPositions()
    {
        FuzzyMatch? match = FuzzyMatcher.Match("OF", "open file");

        Assert.IsNotNull(match);
        CollectionAssert.AreEqual(new[] { 0, 5 }, match.Value.Positions.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Match_PrefersWordStartsOverEarlierLoneLetters()
    {
        FuzzyMatch? match = FuzzyMatcher.Match("ff", "find file");

        Assert.IsNotNull(match);
        CollectionAssert.AreEqual(new[] { 0, 5 }, match.Value.Positions.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Match_ConsecutiveBeatsScattered()
    {
        int tight = FuzzyMatcher.Match("save", "save as")!.Value.Score;
        int loose = FuzzyMatcher.Match("save", "sxxaxxvxxe")!.Value.Score;

        Assert.IsTrue(tight > loose);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Match_CamelCaseHumpCountsAsWordStart()
    {
        FuzzyMatch? match = FuzzyMatcher.Match("fp", "FilePanel");

        Assert.IsNotNull(match);
        CollectionAssert.AreEqual(new[] { 0, 4 }, match.Value.Positions.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Rank_OrdersBestFirst_AndDropsNonMatches()
    {
        string[] items = ["Close tab", "Copy path", "Open settings", "Compare folders"];

        var ranked = FuzzyMatcher.Rank("cp", items, item => item);

        Assert.AreEqual("Copy path", ranked[0].Item);
        Assert.IsFalse(ranked.Any(entry => entry.Item == "Open settings"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Rank_EqualScores_KeepOriginalOrder()
    {
        var ranked = FuzzyMatcher.Rank("a", new[] { "a1", "a2", "a3" }, item => item);

        CollectionAssert.AreEqual(new[] { "a1", "a2", "a3" }, ranked.Select(entry => entry.Item).ToArray());
    }
}
