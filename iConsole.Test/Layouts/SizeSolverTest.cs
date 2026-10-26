using iConsole.Layouts;

namespace iConsole.Test.Layouts;

[TestClass]
public class SizeSolverTest
{
    private static string Solve(int length, int gap, params SizeRule[] rules) =>
        string.Join(",", SizeSolver.Solve(length, rules, gap));

    [TestMethod]
    public void Empty_ReturnsEmpty() => Assert.AreEqual(string.Empty, Solve(10, 0));

    [TestMethod]
    public void Fixed_KeepsItsCells_AndFillTakesTheRest()
    {
        Assert.AreEqual("3,17", Solve(20, 0, SizeRule.Fixed(3), SizeRule.Fill()));
    }

    [TestMethod]
    public void Percent_IsAShareOfTheSpace_AndFillGetsWhatIsLeft()
    {
        Assert.AreEqual("30,70", Solve(100, 0, SizeRule.Percent(30), SizeRule.Fill()));
        Assert.AreEqual("40,10,50", Solve(100, 0, SizeRule.Percent(40), SizeRule.Fixed(10), SizeRule.Fill()));
    }

    [TestMethod]
    public void Gaps_AreTakenOutBeforePercentIsCounted()
    {
        Assert.AreEqual("9,9", Solve(20, 2, SizeRule.Percent(50), SizeRule.Fill()));
        Assert.AreEqual("4,4,4", Solve(14, 1, SizeRule.Fill(), SizeRule.Fill(), SizeRule.Fill()));
    }

    [TestMethod]
    public void Fill_WeightsSplitInProportion()
    {
        Assert.AreEqual("20,10", Solve(30, 0, SizeRule.Fill(2), SizeRule.Fill(1)));
    }

    [TestMethod]
    public void Fill_RoundingNeverLosesACell()
    {
        int[] sizes = SizeSolver.Solve(10, [SizeRule.Fill(), SizeRule.Fill(), SizeRule.Fill()]);

        Assert.AreEqual(10, sizes.Sum());
        Assert.AreEqual("4,3,3", string.Join(",", sizes));
    }

    [TestMethod]
    public void Fill_MinimumTakesPriority_AndTheOthersShareTheRest()
    {
        Assert.AreEqual("12,8", Solve(20, 0, SizeRule.Fill().WithMin(12), SizeRule.Fill()));
    }

    [TestMethod]
    public void Fill_MaximumCaps_AndTheExcessGoesToTheOthers()
    {
        Assert.AreEqual("5,15", Solve(20, 0, SizeRule.Fill().WithMax(5), SizeRule.Fill()));
    }

    [TestMethod]
    public void Percent_RespectsMinAndMax()
    {
        Assert.AreEqual("10,90", Solve(100, 0, SizeRule.Percent(5).WithMin(10), SizeRule.Fill()));
        Assert.AreEqual("20,80", Solve(100, 0, SizeRule.Percent(50).WithMax(20), SizeRule.Fill()));
    }

    [TestMethod]
    public void TooMuchAskedFor_TheLastChildrenShrinkFirst()
    {
        Assert.AreEqual("8,2,0", Solve(10, 0, SizeRule.Fixed(8), SizeRule.Fixed(8), SizeRule.Fill()));
        Assert.AreEqual("0,0", Solve(0, 0, SizeRule.Fixed(3), SizeRule.Fill()));
    }

    [TestMethod]
    public void MinimumsThatCannotAllFit_NeverExceedTheLength()
    {
        int[] sizes = SizeSolver.Solve(10, [SizeRule.Fill().WithMin(8), SizeRule.Fill().WithMin(8)]);

        Assert.AreEqual(10, sizes.Sum());
    }

    [TestMethod]
    public void GapWiderThanTheLength_GivesEveryoneZero()
    {
        Assert.AreEqual("0,0,0", Solve(3, 5, SizeRule.Fill(), SizeRule.Fill(), SizeRule.Fill()));
    }

    [TestMethod]
    public void BadValues_AreTreatedAsZero()
    {
        Assert.AreEqual(0, SizeRule.Percent(double.NaN).Value);
        Assert.AreEqual(0, SizeRule.Fill(-4).Value);
        Assert.AreEqual(0, SizeRule.Fixed(-4).Value);
        Assert.AreEqual("10", Solve(10, 0, SizeRule.Fill(0)), "a lone fill of weight 0 still takes the space");
        Assert.AreEqual("5,5", Solve(10, 0, SizeRule.Fill(0), SizeRule.Fill(0)));
    }
}
