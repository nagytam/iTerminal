using iTerminal.Core.Rename;

namespace iTerminal.Core.Test.Rename;

[TestClass]
public class MultiRenamePlannerTest
{
    private static IReadOnlyList<RenameRow> Plan(IEnumerable<(string Old, string New)> entries, params string[] folder) =>
        MultiRenamePlanner.Plan([.. entries], folder);

    [TestMethod]
    public void ANewNameNoOneElseHoldsIsOk()
    {
        var rows = Plan([("a.txt", "x.txt")], "a.txt");

        Assert.AreEqual(RenameStatus.Ok, rows.Single().Status);
    }

    [TestMethod]
    public void TheSameNameIsUnchanged()
    {
        var rows = Plan([("a.txt", "a.txt")], "a.txt");

        Assert.AreEqual(RenameStatus.Unchanged, rows.Single().Status);
    }

    [TestMethod]
    public void ABlankNameIsEmpty()
    {
        Assert.AreEqual(RenameStatus.Empty, Plan([("a.txt", "  ")], "a.txt").Single().Status);
        Assert.AreEqual(RenameStatus.Empty, Plan([("a.txt", string.Empty)], "a.txt").Single().Status);
    }

    [TestMethod]
    public void ASeparatorDotOrDotDotIsAnInvalidName()
    {
        Assert.AreEqual(RenameStatus.InvalidName, Plan([("a.txt", "x/y.txt")], "a.txt").Single().Status);
        Assert.AreEqual(RenameStatus.InvalidName, Plan([("a.txt", "x\\y.txt")], "a.txt").Single().Status);
        Assert.AreEqual(RenameStatus.InvalidName, Plan([("a.txt", "..")], "a.txt").Single().Status);
        Assert.AreEqual(RenameStatus.InvalidName, Plan([("a.txt", ".")], "a.txt").Single().Status);
    }

    [TestMethod]
    public void ANameTheBatchVacatesIsBlocked()
    {
        // a -> b and b -> a: whichever ran first would meet the other's name, so neither runs.
        var rows = Plan([("a.txt", "b.txt"), ("b.txt", "a.txt")], "a.txt", "b.txt");

        Assert.AreEqual(RenameStatus.Blocked, rows[0].Status);
        Assert.AreEqual(RenameStatus.Blocked, rows[1].Status);
    }

    [TestMethod]
    public void ANameAnotherEntryInTheFolderHoldsIsExists()
    {
        var rows = Plan([("a.txt", "c.txt")], "a.txt", "c.txt");

        Assert.AreEqual(RenameStatus.Exists, rows.Single().Status);
    }

    [TestMethod]
    public void TwoRowsWithOneNewNameAreDuplicates()
    {
        var rows = Plan([("a.txt", "x.txt"), ("b.txt", "x.txt")], "a.txt", "b.txt");

        Assert.AreEqual(RenameStatus.Duplicate, rows[0].Status);
        Assert.AreEqual(RenameStatus.Duplicate, rows[1].Status);
    }

    [TestMethod]
    public void DuplicatesIgnoreCase()
    {
        var rows = Plan([("a.txt", "X.txt"), ("b.txt", "x.TXT")], "a.txt", "b.txt");

        Assert.AreEqual(RenameStatus.Duplicate, rows[0].Status);
        Assert.AreEqual(RenameStatus.Duplicate, rows[1].Status);
    }

    [TestMethod]
    public void ACaseOnlyRenameOfItselfIsOk()
    {
        // The row's own old name is neither a blocker nor a sibling for itself, so a -> A goes ahead on any volume.
        var rows = Plan([("a.txt", "A.txt")], "a.txt");

        Assert.AreEqual(RenameStatus.Ok, rows.Single().Status);
    }

    [TestMethod]
    public void UnchangedWinsOverExists()
    {
        // The name is the row's own, so it is unchanged, even though the folder lists it.
        var rows = Plan([("a.txt", "a.txt")], "a.txt");

        Assert.AreEqual(RenameStatus.Unchanged, rows.Single().Status);
    }

    [TestMethod]
    public void BlockedWinsOverDuplicate()
    {
        // a -> b, b -> b: a is blocked by b's old name, and b is unchanged. Two rows share b, but Blocked is decided first.
        var rows = Plan([("a.txt", "b.txt"), ("b.txt", "b.txt")], "a.txt", "b.txt");

        Assert.AreEqual(RenameStatus.Blocked, rows[0].Status);
        Assert.AreEqual(RenameStatus.Unchanged, rows[1].Status);
    }

    [TestMethod]
    public void AProblemRowDoesNotChangeTheOtherRows()
    {
        var rows = Plan([("a.txt", "x.txt"), ("b.txt", string.Empty)], "a.txt", "b.txt");

        Assert.AreEqual(RenameStatus.Ok, rows[0].Status);
        Assert.AreEqual(RenameStatus.Empty, rows[1].Status);
    }

    [TestMethod]
    public void RowsKeepTheBatchOrderAndTheirIndex()
    {
        var rows = Plan([("a.txt", "x.txt"), ("b.txt", "y.txt")], "a.txt", "b.txt");

        Assert.AreEqual(0, rows[0].Index);
        Assert.AreEqual("a.txt", rows[0].OldName);
        Assert.AreEqual("y.txt", rows[1].NewName);
    }
}
