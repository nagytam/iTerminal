using iTerminal.Core.Rename;

namespace iTerminal.Core.Test.Rename;

[TestClass]
public class MultiRenamePlannerTest
{
    private static IReadOnlyList<RenameRow> Plan(IEnumerable<(string Old, string New)> entries, params string[] folder) =>
        MultiRenamePlanner.Plan([.. entries], folder);

    [TestMethod]
    [Timeout(60_000)]
    public void ANewNameNoOneElseHoldsIsOk()
    {
        var rows = Plan([("a.txt", "x.txt")], "a.txt");

        Assert.AreEqual(RenameStatus.Ok, rows.Single().Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void TheSameNameIsUnchanged()
    {
        var rows = Plan([("a.txt", "a.txt")], "a.txt");

        Assert.AreEqual(RenameStatus.Unchanged, rows.Single().Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void ABlankNameIsEmpty()
    {
        Assert.AreEqual(RenameStatus.Empty, Plan([("a.txt", "  ")], "a.txt").Single().Status);
        Assert.AreEqual(RenameStatus.Empty, Plan([("a.txt", string.Empty)], "a.txt").Single().Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void ASeparatorDotOrDotDotIsAnInvalidName()
    {
        Assert.AreEqual(RenameStatus.InvalidName, Plan([("a.txt", "x/y.txt")], "a.txt").Single().Status);
        Assert.AreEqual(RenameStatus.InvalidName, Plan([("a.txt", "x\\y.txt")], "a.txt").Single().Status);
        Assert.AreEqual(RenameStatus.InvalidName, Plan([("a.txt", "..")], "a.txt").Single().Status);
        Assert.AreEqual(RenameStatus.InvalidName, Plan([("a.txt", ".")], "a.txt").Single().Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void ANameTheBatchVacatesIsBlocked()
    {
        // a -> b and b -> a: whichever ran first would meet the other's name, so neither runs.
        var rows = Plan([("a.txt", "b.txt"), ("b.txt", "a.txt")], "a.txt", "b.txt");

        Assert.AreEqual(RenameStatus.Blocked, rows[0].Status);
        Assert.AreEqual(RenameStatus.Blocked, rows[1].Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void ANameAnotherEntryInTheFolderHoldsIsExists()
    {
        var rows = Plan([("a.txt", "c.txt")], "a.txt", "c.txt");

        Assert.AreEqual(RenameStatus.Exists, rows.Single().Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void TwoRowsWithOneNewNameAreDuplicates()
    {
        var rows = Plan([("a.txt", "x.txt"), ("b.txt", "x.txt")], "a.txt", "b.txt");

        Assert.AreEqual(RenameStatus.Duplicate, rows[0].Status);
        Assert.AreEqual(RenameStatus.Duplicate, rows[1].Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void DuplicatesIgnoreCase()
    {
        var rows = Plan([("a.txt", "X.txt"), ("b.txt", "x.TXT")], "a.txt", "b.txt");

        Assert.AreEqual(RenameStatus.Duplicate, rows[0].Status);
        Assert.AreEqual(RenameStatus.Duplicate, rows[1].Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void ACaseOnlyChangeIsCaseOnlyAndNotRun()
    {
        // On a case-insensitive volume the engine would find the file itself as the target, so the change is not run on any platform.
        var rows = Plan([("a.txt", "A.txt")], "a.txt");

        Assert.AreEqual(RenameStatus.CaseOnly, rows.Single().Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void ACaseVariantOfABatchNameDoesNotBlockAnotherName()
    {
        // Foo.txt is in the batch; foo.txt is another file in the folder. Foo.txt -> foo.txt only changes case; Foo.txt -> x.txt is free.
        var caseOnly = Plan([("Foo.txt", "foo.txt")], "Foo.txt", "foo.txt");
        var free = Plan([("Foo.txt", "x.txt")], "Foo.txt", "foo.txt");

        Assert.AreEqual(RenameStatus.CaseOnly, caseOnly.Single().Status);
        Assert.AreEqual(RenameStatus.Ok, free.Single().Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void WindowsNameRulesRefuseTrailingDotsAndReservedNames()
    {
        IReadOnlyList<RenameRow> OnWindows(string newName) =>
            MultiRenamePlanner.Plan([("a.txt", newName)], ["a.txt"], windowsNameRules: true);

        Assert.AreEqual(RenameStatus.InvalidName, OnWindows("report.").Single().Status, "a trailing dot");
        Assert.AreEqual(RenameStatus.InvalidName, OnWindows("report ").Single().Status, "a trailing blank");
        Assert.AreEqual(RenameStatus.InvalidName, OnWindows("CON").Single().Status, "a reserved device name");
        Assert.AreEqual(RenameStatus.InvalidName, OnWindows("nul.txt").Single().Status, "a reserved name with an extension");
        Assert.AreEqual(RenameStatus.Ok, OnWindows("console.txt").Single().Status, "a stem that only starts like one");
        Assert.AreEqual(RenameStatus.Ok, OnWindows("report.txt").Single().Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void WithoutWindowsRulesTheseNamesAreAllowed()
    {
        Assert.AreEqual(RenameStatus.Ok, MultiRenamePlanner.Plan([("a.txt", "CON")], ["a.txt"]).Single().Status);
        Assert.AreEqual(RenameStatus.Ok, MultiRenamePlanner.Plan([("a.txt", "report.")], ["a.txt"]).Single().Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void UnchangedWinsOverExists()
    {
        // The name is the row's own, so it is unchanged, even though the folder lists it.
        var rows = Plan([("a.txt", "a.txt")], "a.txt");

        Assert.AreEqual(RenameStatus.Unchanged, rows.Single().Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void BlockedWinsOverDuplicate()
    {
        // a -> b, b -> b: a is blocked by b's old name, and b is unchanged. Two rows share b, but Blocked is decided first.
        var rows = Plan([("a.txt", "b.txt"), ("b.txt", "b.txt")], "a.txt", "b.txt");

        Assert.AreEqual(RenameStatus.Blocked, rows[0].Status);
        Assert.AreEqual(RenameStatus.Unchanged, rows[1].Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void AProblemRowDoesNotChangeTheOtherRows()
    {
        var rows = Plan([("a.txt", "x.txt"), ("b.txt", string.Empty)], "a.txt", "b.txt");

        Assert.AreEqual(RenameStatus.Ok, rows[0].Status);
        Assert.AreEqual(RenameStatus.Empty, rows[1].Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public void RowsKeepTheBatchOrderAndTheirIndex()
    {
        var rows = Plan([("a.txt", "x.txt"), ("b.txt", "y.txt")], "a.txt", "b.txt");

        Assert.AreEqual(0, rows[0].Index);
        Assert.AreEqual("a.txt", rows[0].OldName);
        Assert.AreEqual("y.txt", rows[1].NewName);
    }
}
