using iTerminal.Core.Services.Compare;

namespace iTerminal.Core.Test.Services.Compare;

[TestClass]
public class BinaryComparerTest
{
    [TestMethod]
    public void IdenticalBytes_HaveNoDifferences()
    {
        BinaryDiffResult result = BinaryComparer.Compare([1, 2, 3], [1, 2, 3]);

        Assert.IsTrue(result.Identical);
        Assert.AreEqual(0, result.DifferingBytes);
    }

    [TestMethod]
    public void TwoEmptyFiles_AreIdentical()
    {
        Assert.IsTrue(BinaryComparer.Compare([], []).Identical);
    }

    [TestMethod]
    public void NeighbouringDifferences_AreOneRun_AndSeparatedOnesAreTwo()
    {
        BinaryDiffResult result = BinaryComparer.Compare([1, 2, 3, 4, 5, 6], [1, 9, 9, 4, 5, 8]);

        Assert.AreEqual(2, result.Runs.Count);
        Assert.AreEqual(new ByteDifference(1, 2), result.Runs[0]);
        Assert.AreEqual(new ByteDifference(5, 1), result.Runs[1]);
        Assert.AreEqual(3, result.DifferingBytes);
    }

    [TestMethod]
    public void TheLongerFile_DiffersPastTheEndOfTheShorter()
    {
        BinaryDiffResult result = BinaryComparer.Compare([1, 2], [1, 2, 3, 4]);

        Assert.AreEqual(2, result.LeftLength);
        Assert.AreEqual(4, result.RightLength);
        Assert.AreEqual(new ByteDifference(2, 2), result.Runs.Single());
    }

    [TestMethod]
    public void IsDifferent_IsTrueOnlyInsideARun()
    {
        BinaryDiffResult result = BinaryComparer.Compare([1, 2, 3, 4, 5, 6], [1, 9, 9, 4, 5, 8]);

        Assert.IsFalse(result.IsDifferent(0));
        Assert.IsTrue(result.IsDifferent(1));
        Assert.IsTrue(result.IsDifferent(2));
        Assert.IsFalse(result.IsDifferent(3));
        Assert.IsTrue(result.IsDifferent(5));
        Assert.IsFalse(result.IsDifferent(100));
    }
}
